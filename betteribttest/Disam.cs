﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Web.UI;
using System.Web;
using System.Runtime.InteropServices;
using System.Diagnostics;
/*
    So why SortedList?  When using the peekhole optimiazer (or deoptimizer?) We will need to get the next instruction 
    rather than the next pc, while OTHEr parts of the decompiler needs the next pc
*/

namespace betteribttest
{
    using Opcode = GM_Disam.Opcode;
    using OffsetOpcode = GM_Disam.OffsetOpcode;
    using System.Text.RegularExpressions;
    class Disam
    {
        string scriptName;
        ChunkReader cr;
        SortedList<int, GM_Disam.Opcode> codes;
        SortedList<int, OffsetOpcode> inlineBranches;
        public static Dictionary<int, string> instanceLookup = new Dictionary<int, string>()
        {
            {  0 , "stack" },
            {  -1, "self" },
            {  -2, "other" },
            {  -3, "all" },
            {  -4, "noone" },
            {  -5, "global" },
        };
        static string lookupInstance(int instance)
        {
            string ret;
            if (Disam.instanceLookup.TryGetValue(instance, out ret)) return ret;
            else return String.Format("Instance({0})", instance);
        }


        static Dictionary<OpType, string> opMathOperation = new Dictionary<OpType, string>()  {
            {  (OpType)0x03, "conv" },
            {  (OpType)0x04, "*" },
            {  (OpType)0x05, "/" },
            { (OpType) 0x06, "rem" },
            {  (OpType)0x07, "%" },
            {  (OpType)0x08, "+" },
            {  (OpType)0x09, "-" },
            {  (OpType)0x0a, "&" },
            {  (OpType)0x0b, "|" },
            { (OpType) 0x0c, "^" },
            { (OpType) 0x0d, "~" },
            {  (OpType)0x0e, "!" },

            {  (OpType)0x0f, "<<" },
            {  (OpType)0x10, ">>" },
            { (OpType) 0x11, "<" },
            { (OpType) 0x12, "<=" },
            { (OpType) 0x13, "==" },
            {  (OpType)0x14, "!=" },
            {  (OpType)0x15, ">=" },
            {  (OpType)0x16, ">" },
        };
        static Dictionary<OpType, OpType> invertOpType = new Dictionary<OpType, OpType>()
        {
            { OpType.Slt, OpType.Sge },
            { OpType.Sge, OpType.Slt },
            { OpType.Sle, OpType.Sgt },
            { OpType.Sgt, OpType.Sle },
            { OpType.Sne, OpType.Seq },
            { OpType.Seq, OpType.Sne },
        };
        // side not, AGAIN, if I wanted to spend the time, I could make an array for each object/script
        // and determan what the types are of all the variables and then act find the object names that way
        // hoewever this will take weeks and its just a few I need to look at

        static Regex RealConstantRegex = new Regex(@"real\((\d+)\)");
        static Dictionary<string, FixVariableDel> fix_variable = new Dictionary<string, FixVariableDel>()
        {
            {"self.sprite_index", delegate(Disam disam, AST toAssign, ref AST value) {
                    if(value.CanEval)
                    {
                        var obj = disam.cr.spriteList[value.Eval()];
                        value = Variable.CreatePsudoVariable(obj.Name);
                    }
                    
                    // sometimes we use the function "real" with a constant inside so lets find it
            }
            // This is a special case.  In all the obj_face files, there is an array of u where
            // all the face sprite names are kept, we detect if we are IN that and then change the
            // constants to numerical values
            },{ "self.u", delegate(Disam disam, AST toAssign, ref AST value) {
                if(disam.scriptName.ToLower().IndexOf("obj_face")!=-1)
                {
                     if(value.CanEval)
                    {
                        var obj = disam.cr.spriteList[value.Eval()];
                        value = Variable.CreatePsudoVariable(obj.Name);
                    }
                }
                }
            }
        };
 
        static Dictionary<string, FixFunctionDel> fix_function = new Dictionary<string, FixFunctionDel>()
        {
            { "instance_create",
                delegate (Disam disam, AST[] args) {
                    AST last = args.Last();
                if(last.CanEval) // if its a numerical instance, find the object name
                {
                    var obj = disam.cr.objList[last.Eval()];
                    args[args.Length - 1] = Variable.CreatePsudoVariable(obj.Name);
                    }
                }
            },
            { "instance_exists",
                delegate (Disam disam, AST[] args) {
                      if(args[0].CanEval) // if its a numerical instance, find the object name
                {
                    var obj = disam.cr.objList[args[0].Eval()];
                    args[0] = Variable.CreatePsudoVariable(obj.Name);
                    }
                }
            },
            { "snd_play",
                delegate (Disam disam, AST[] args) {
                          if(args[0].CanEval) // if its a numerical instance, find the object name
                {
                    var obj = disam.cr.audioList[args[0].Eval()];
                    args[0] = Variable.CreatePsudoVariable("{ Name = " + obj.Name + " , filename = " + obj.filename + "}");
                    }
                }
            },
            { "script_execute",
                delegate (Disam disam, AST[] args) {
                     if(args[0].CanEval) // if its a numerical instance, find the object name
                      {
                        var obj = disam.cr.scriptIndex[args[0].Eval()];
                        string script_name = obj.script_name;
                        args[0] = Variable.CreatePsudoVariable(script_name);
                        FixFunctionDel script_execute_del;
                        if(script_execute_fix.TryGetValue(script_name,out script_execute_del)) script_execute_del(disam,args);
                    }

                }
            },


        };
        public Disam(ChunkReader cr)
        {
            this.cr = cr;
        }

        public string TestStreamOutput(string code_name)
        {
            code_name = code_name.ToLower();
            foreach (GMK_Code c in cr.codeList)
            {
                if (c.Name.ToLower().IndexOf(code_name) != -1)
                {
                    System.Diagnostics.Debug.WriteLine("Processing script {0}", c.Name);
                    //System.Diagnostics.Debug.Assert("gml_Object_obj_finalfroggit_Alarm_6" != c.Name);
                    ChunkStream ms = cr.getReturnStream();
                    var lines = processStream2(ms, c.startPosition, c.size,c.Name);

                    StreamWriter s = new StreamWriter(c.Name + ".txt");
                    foreach (var line in lines)
                    {
                        string text = line.DecompileLine();
                        if (!string.IsNullOrEmpty(text)) s.WriteLine(text);
                    }
                    s.Close();
                }
            }
            return null;
        }
        public void writeFile(string code_name)
        {
            bool found = false;
            foreach (GMK_Code c in cr.codeList)
            {
                if (c.Name.IndexOf(code_name) != -1)
                {

                    //  writeHTMLFile(c.Name);
                }
            }
            if (!found) throw new Exception(code_name + " not found");
        }






        class AST
        {
            public GM_Type Type { get; private set; }
            public AST(GM_Type type) { this.Type = type; }

            // Attemted to get an int for either an instance, or for a something else
            public virtual bool CanEval { get { return false; } }
            public virtual int Eval() { return 0; }
        }
        class Variable : AST
        {
            double dvalue;
            long ivalue;
            string svalue;
            bool isInt;
            public Variable Dup()
            {
                return (Variable)this.MemberwiseClone();
            }
            public static Variable CreateString(string str)
            {
                return new Variable(str, GM_Type.String);
            }
            public static Variable CreatePsudoVariable(string str)
            {
                return new Variable(str, GM_Type.Var);
            }
            public string Value { get; private set; }
            public double DValue { get { return dvalue; } }
            public double IValue { get { return ivalue; } }
            public Variable(int value) : base(GM_Type.Int) { ivalue = value; Value = value.ToString(); isInt = true; }
            public Variable(long value) : base(GM_Type.Long) { ivalue = value; Value = value.ToString(); isInt = true; }
            public Variable(ushort value) : base(GM_Type.Short) { ivalue = value; Value = value.ToString(); isInt = true; }
            public Variable(float value) : base(GM_Type.Float) { dvalue = value; Value = value.ToString(); isInt = false; }
            public Variable(double value) : base(GM_Type.Double) { dvalue = value; Value = value.ToString(); isInt = false; }
            public Variable(bool value) : base(GM_Type.Bool) { ivalue = value ? 1 : 0; Value = value.ToString(); isInt = false; }
            public Variable(string value, GM_Type type) : base(type) {
                Value = svalue = GM_Type.String == type ? Value = svalue = GM_Disam.EscapeString(value) :value;
                isInt = long.TryParse(value, out ivalue);
            }
            public Variable(int value, string instance) : base(GM_Type.Instance) {
                ivalue = value;
                Value = instance;
                isInt = true;
            }
            public override bool CanEval
            {
                get
                {
                    return isInt;
                }
            }
            public override int Eval() { return (int)ivalue; }
          
            public override string ToString()
            {

                return Value;
            }
        }
        class Conv : AST
        {
            public AST next;
            GM_Type from;
            GM_Type to;
            public Conv(AST ast, GM_Type from, GM_Type to) : base(to) { this.from = from;  this.to = to; this.next = ast; }
            public override bool CanEval
            {
                get
                {
                    return next.CanEval;
                }
            }
            public override int Eval()
            {
                return next.Eval();
            }
            public override string ToString()
            {
                Variable isVar = next as Variable;
                if (this.Type == next.Type) return next.ToString();

                switch (this.Type)
                {
                    case GM_Type.Int:
                        switch (next.Type)
                        {
                            case GM_Type.Double:
                            case GM_Type.Float:
                            case GM_Type.String:
                                return "(Int)(" + next.ToString() + ")";
                            default:
                                return next.ToString();
                        }
                    case GM_Type.Long:
                        switch (next.Type)
                        {
                            case GM_Type.Double:
                            case GM_Type.Float:
                            case GM_Type.String:
                                return "(Long)(" + next.ToString() + ")";
                            default:
                                return next.ToString();
                        }
                    case GM_Type.Short:
                        switch (next.Type)
                        {
                            case GM_Type.Double:
                            case GM_Type.Float:
                            case GM_Type.String:
                                return "(Short)(" + next.ToString() + ")";
                            default:
                                return next.ToString();
                        }
                    case GM_Type.Float:
                        switch (next.Type)
                        {
                            case GM_Type.Int:
                            case GM_Type.Long:
                            case GM_Type.Short:
                            case GM_Type.String:
                                return "(Float)(" + next.ToString() + ")";
                            default:
                                return next.ToString();
                        }
                    case GM_Type.Double:
                        switch (next.Type)
                        {
                            case GM_Type.Int:
                            case GM_Type.Long:
                            case GM_Type.Short:
                            case GM_Type.String:
                                return "(Double)(" + next.ToString() + ")";
                            default:
                                return next.ToString();
                        }
                    case GM_Type.String: // might have an issue with a double string conversion but should be fine
                        if (isVar != null && isVar.Type == GM_Type.Instance)
                            return "(String)(" + next.ToString() + ")";
                        else
                            return "(String)(" + next.ToString() + ")";
                    case GM_Type.Var:
                        if (isVar != null && next.Type == GM_Type.String) return isVar.ToString(); // might be a normal string
                        else return next.ToString(); // dosn't matter
                    case GM_Type.Instance:
                        if (isVar != null && next.Type == GM_Type.Short || next.Type == GM_Type.Int)
                        {
                            // we have to look up the int type here, need to have a static list somewhere or throw?
                            string instance;
                            if (instanceLookup.TryGetValue((int)isVar.IValue, out instance)) return instance;
                        }
                        return "(Instance)(" + isVar.ToString() + ")";
                    default:
                        return next.ToString();
                }
            }
        }
        class MathOp : AST
        {
            public AST Left { get; private set; }
            public AST Right { get; private set; }

            public OpType Op { get; private set; }

            public MathOp(AST left, OpType op, AST right) : base(GM_Type.NoType) { this.Left = left; this.Op = op; this.Right = right; }
            public override string ToString()
            {
                string sop;
                if (opMathOperation.TryGetValue(Op, out sop))
                {
                    return "(" + Left.ToString() + " " + sop + " " + Right.ToString() + ")";
                }
                throw new ArgumentException("Cannot find math operation");
            }
        }
        class Assign : AST
        {
            public string Variable { get; private set; }
            public AST Value { get; private set; }
            public Assign(string variable, GM_Type type, AST value) : base(type) { this.Variable = variable; this.Value = value; }
            public override string ToString()
            {
                return Variable + " = " + Value.ToString();
            }
        }
        class Call : AST
        {
            AST constant;
            public override bool CanEval
            {
                get
                {
                    return constant == null ? false : constant.CanEval;
                }
            }
            public override int Eval()
            {
                return constant.Eval();
            }
            public string FunctionName { get; private set; }
            public int ArgumentCount { get; private set; }
            public AST[] Arguments { get; private set; }
            public Call(string functionname, params AST[] args) : base(GM_Type.Int)
            {
                this.FunctionName = functionname;
                ArgumentCount = args.Length;
                Arguments = args;
                // special case for a constant going though a real function
                if (functionname == "real" && args.Length == 1 && args[0].CanEval) constant = args[0];
                // some sepcial cases for evaling
            }

            public override string ToString()
            {
                string ret = FunctionName + "(";
                if (ArgumentCount > 0)
                {
                    for (int i = 0; i < ArgumentCount - 1; i++)
                        ret += Arguments[i].ToString() + ",";
                    ret += Arguments.Last();
                }
                ret += ")";
                return ret;
            }
        }

        string FormatAssign( string var_name, int index, AST value)
        {
            return String.Format("{0}[{1}] = {2}",  var_name, index, value.ToString());
        }
        string FormatAssign( string var_name, AST index, AST value)
        {
            return String.Format("{0}[{1}] = {2}",  var_name, index.ToString(), value.ToString());
        }
        string FormatAssign( string var_name, int index, string value)
        {
            return String.Format("{0}[{1}] = {2}",  var_name, index, value);
        }
        string FormatAssign(string sinstance, string var_name, string value)
        {
            return String.Format("{0}.{1} = {2}", sinstance, var_name, value);
        }

        // This is an offset
        public string decodeCallName(int operand)
        {
            int string_ref = (int)(operand & 0x0FFFFFFF); // this COULD be 24 bits?

            if (string_ref < cr.stringList.Count)
            {
                return cr.stringList[string_ref].str;
            }
            else throw new Exception("Function not found!"); // return "NOT FOUND: " + string_ref.ToString();
        }

        // The new Fangled method
  
            
        string GetInstance(Stack<AST> tree, int instance)
        {
            string sinstance = null;
            if (instance == 0) // in the stack
            {
                AST new_instance = tree.Pop();
                if (!new_instance.CanEval) throw new Exception("Cannot eval this stack value");
                instance = new_instance.Eval();
                if (instance == 0) throw new Exception("Reading a stack, of the instance?");
            }
            if (instance < 0) sinstance = lookupInstance(instance);
            else sinstance = cr.objList[instance].Name;
            return sinstance;
        }
        string  GetInstance(Stack<AST> tree, string instance)
        {
            int instance_test;
            if (int.TryParse(instance, out instance_test)) return GetInstance(tree, instance_test);
            return null;
        }
        void ProcessVarPush(Stack<AST> tree, GM_Disam.PushOpcode op) {
            int instance = op.Instance;
            int operandValue = (int)op.OperandValue;
            int load_type = (operandValue >> 24) & 0xFF;
            string var_name = decodeCallName(operandValue & 0x00FFFFFF);

            AST index = load_type == 0 ? tree.Pop() : null;
            var_name = GetInstance(tree, instance) + "." + var_name;
            AST value = null;
            if (index == null)
                value=Variable.CreatePsudoVariable(var_name);
            else
                value = Variable.CreatePsudoVariable(var_name + "[" + index.ToString() + "]");
            tree.Push(value);
           // throw new NotImplementedException("Push.V convert not implmented"); // we are going to handle it all, no exceptions this time around
        }
        string ProcessAssignPop(Stack<AST> tree, GM_Disam.PopOpcode code)
        {
            GM_Type convertFrom = code.FirstType;
            GM_Type convertTo = code.SecondType;
            int iinstance = code.Instance;
            int load_type = code.Offset >> 24 & 0xFF; // I think this might only be 4 bits
            string var_name = decodeCallName(code.Offset & 0x00FFFFFF);

            AST index = load_type == 0 ? tree.Pop() : null;
            var_name = GetInstance(tree, iinstance) + "." + var_name;
            AST value = tree.Pop();
            FixVariable(this, var_name,ref value);
    
            //AST assign = null;
            string assignString;
            if (index == null)
                assignString =var_name + " = " + value.ToString();
            else
                assignString =var_name + "[" + index.ToString() + "] = " + value.ToString();

            return assignString;
        }
        void ProcessMathOp(Stack<AST> tree, uint op)
        {
            OpType code = (OpType)((byte)(op >> 24));
            GM_Type fromType = (GM_Type)(int)((op >> 16) & 0xF);
            GM_Type tooType = (GM_Type)(int)((op >> 20) & 0xF);
            AST right = tree.Pop();
            AST left = tree.Pop();
            FixVariable(this, left.ToString(), ref right);
            tree.Push(new MathOp(left, code, right));
        }
        delegate void FixFunctionDel(Disam disam, AST[] args);
        delegate void FixVariableDel(Disam disam, AST toAssign, ref AST value);
        static void FixVariable(Disam disam, string var_name, ref AST value)
        {
            FixVariableDel fixVar;
            if (fix_variable.TryGetValue(var_name, out fixVar)) fixVar(disam, null, ref value);
        }
        // takes the stuff from fix execute and repairs the names of the arguments
        static Dictionary<string, FixFunctionDel> script_execute_fix = new Dictionary<string, FixFunctionDel>()
        {
            { "SCR_TEXTSETUP",
              delegate (Disam disam, AST[] args) {
                    if(args[1].CanEval) args[0] = Variable.CreatePsudoVariable(disam.cr.resFonts[args[1].Eval()].Name);  // font index  
                    if(args[3].CanEval) args[2] =  Variable.CreatePsudoVariable(String.Format("Color(0x{0:X8})", args[3].Eval()));  // color, just easyer to read
                    if(args[8].CanEval)  // sound
                    {
                       var obj = disam.cr.audioList[args[8].Eval()];
                        args[8] =  Variable.CreatePsudoVariable("{ Name = " + obj.Name + " , filename = " + obj.filename + "}");
                    }
              }
            },

        };
    
        void ProcessCall(Stack<AST> tree, GM_Disam.CallOpcode op)
        {
            //  byte return_type = (byte)((op >> 16) & 0xFF); // always i
            // string return_type_string = typeLookup[return_type];
            int arg_count = op.ArgumentCount;
            string func_name = decodeCallName(op.Offset);

            AST[] args = new AST[arg_count];
            for (int i = 0; i < arg_count; i++) args[i] = tree.Pop();
            // Call special cases
            FixFunctionDel fix;
            if (fix_function.TryGetValue(func_name, out fix)) fix(this, args);

            tree.Push(new Call(func_name, args.ToArray()));
        }
        public bool invertBranchLogic = true;
        string ProcessBranch(Stack<AST> tree, string gotoLabel, bool testIfTrue)
        {
            string s = "if ";
            AST value = tree.Pop();
            MathOp mathOp = value as MathOp;
            if (mathOp == null)
            {
                if (testIfTrue) s += value.ToString();
                else s += "!" + value.ToString();
            }
            else
            {
                if (invertBranchLogic && !testIfTrue)
                {
                    OpType inverted;
                    if (invertOpType.TryGetValue(mathOp.Op, out inverted))
                    {
                        mathOp = new MathOp(mathOp.Left, inverted, mathOp.Right);
                        testIfTrue = true;
                    }
                }
                if (testIfTrue)
                    s += mathOp.ToString();
                else
                    s += "!(" + mathOp.ToString() + ")";
            }
            s += " then goto " + gotoLabel;
            return s;
        }
        class CodeLine
        {
            public string Text = null;
            public string Label = null;
            public GM_Disam.Opcode code;
            public int pc = 0;
            public long startPc = 0;
            public long endPC = 0;

            public string DecompileLine()
            {
                if (this.Text == null && this.Label == null) return null;
                string s = String.Format("{0,-10} {1}", this.Label == null ? "" : this.Label + ":", this.Text == null ? "" : this.Text);
                return s;
            }
            public override string ToString()
            {
                string s = DecompileLine();
                return s == null ? "Nothing" : s;
            }
        }
        Dictionary<int, string> gotos = new Dictionary<int, string>();
        HashSet<int> markedCode = new HashSet<int>();
        string GetLabel(int pc)
        {
            string label;
            if (gotos.TryGetValue(pc, out label)) return label;
            return gotos[pc] = ("Label_" + gotos.Count);
        }


        void InsertLabels(List<CodeLine> lines, int pc)
        {
            // CodeLine line;
            string label;
            foreach (var line in lines)
            {
                if (gotos.TryGetValue(line.pc, out label))
                {
                    if (line.Label != null) throw new Exception("Label '" + line.Label + "' already assigned for pc = " + line.pc);
                    line.Label = label;
                    gotos.Remove(line.pc);
                }
            }
            if (gotos.Count > 0)
            {
                List<int> keys = new List<int>();
                var line = lines.Last();
                foreach (var g in gotos)
                {
                    if (g.Key > line.pc)
                    {
                        CodeLine n = new CodeLine();
                        n.Label = g.Value;
                        n.Text = null;
                        n.pc = g.Key;
                        n.startPc = g.Key;
                        n.endPC = g.Key;
                        lines.Add(n);
                        keys.Add(g.Key);

                    }
                }
                foreach (var k in keys) gotos.Remove(k);
            }
            foreach (var g in gotos)
            {
                CodeLine n = new CodeLine();
                n.pc = g.Key;
                n.Text = " Label '" + g.Value + "' not used";
                lines.Insert(0, n);
            }
           
        }
        bool OffsetOpcodeTest(Opcode o, OpType op, int offset)
        {
            GM_Disam.OffsetOpcode branch = o as GM_Disam.OffsetOpcode;
            if (branch != null && branch.Op == op && branch.Offset == offset) return true;
            return false;
        }
        bool OffsetOpcodeTest(Opcode o, OpType op)
        {
            GM_Disam.OffsetOpcode branch = o as GM_Disam.OffsetOpcode;
            if (branch != null && branch.Op == op) return true;
            return false;
        }
        bool PushOpcodeTest(Opcode o, GM_Type t, int value)
        {
            GM_Disam.PushOpcode push = o as GM_Disam.PushOpcode;
            if (push != null && push.OperandType == t && push.OperandValue == value) return true;
            return false;
        }
        void ReplaceBranchAddress(int from, int too)
        {
            foreach (var toFix in codes.Where(o => o.Value.isBranch && o.Value.Address == from))
            {
                OffsetOpcode b = toFix.Value as OffsetOpcode;
                System.Diagnostics.Debug.Write("\t B Correct: " + b.ToString());
                System.Diagnostics.Debug.Write(" TO ");
                b.Address = too;
                System.Diagnostics.Debug.WriteLine(b.ToString());
            }
        }
        void ReplaceBranchAddress(int from, int too, OpType fromType, OpType toType)
        {
            foreach (var toFix in codes.Where(o => o.Value.Op == fromType && o.Value.Address == from))
            {
                OffsetOpcode b = toFix.Value as OffsetOpcode;
                System.Diagnostics.Debug.Write("\t B Correct: " + b.ToString());
                System.Diagnostics.Debug.Write(" TO ");
                b.Address = too;
                b.Op = toType;
                System.Diagnostics.Debug.WriteLine(b.ToString());
            }
        }

        List<Opcode> MatchPatern(bool remove, params Func<Opcode, bool>[] funcs)
        {
            var list = codes.Values;
            int pos = 0;
            while (pos < list.Count)
            {
                int match = 0;
                while (match < funcs.Length)
                {
                    if (funcs[match](list[pos + match])) match++;
                    else break;
                }
                if (match == funcs.Length)
                {
                    List<Opcode> ops = new List<Opcode>();
                    for (int i = 0; i < match; i++) ops.Add(list[pos + i]);
                    if (remove) foreach (Opcode o in ops) codes.Remove(o.Pc); // have to do this as the list numbers become flaky
                    return ops;
                }
                pos++;
            }
            return null;
        }

        // FIX THIS.  I think I will have to just start doing branch following and detection and to detect this kind of junk
        void PeepholeFix()
        {
            inlineBranches = new SortedList<int, OffsetOpcode>();
            // need to change this to a bunch of delgates, but right now lets find one patern match
            var list = codes.Values;
            int fix_count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var match = MatchPatern(false, o => o.Op == OpType.B && o.Offset == 2, o => PushOpcodeTest(o, GM_Type.Short, 0), o => o.Op == OpType.Bf);

                if (match != null)
                {
                    fix_count++;
                    // Ok, lets start this ride.  Since the first instruction is a B and it jumps over to BF, something is on the stack that we need to compare BF with
                    OffsetOpcode inlineBranch = new OffsetOpcode(match[2].Op, match[2].Offset, match[0].Pc);
                    int final_destnation = match[2].Address;
                    match[0].Op = OpType.Bf;
                    match[0].Address = final_destnation; // fix the jump
                    match[1].Op = OpType.BadOp;
                    match[2].Op = OpType.BadOp;

                    // lets start this ball rolling, first lets find any and all branches to the three code we have here
                    // First, a jump to B 2 means its really a BF <somehwere> so both of these instructons are equilvelent

                    var indirectB = MatchPatern(false, o => o.isBranch && o.Address == match[1].Pc);
                    if (indirectB != null) foreach (Opcode o in indirectB) o.Address = final_destnation; // we don't care about the target branch type

                    // Ran into this on gml_Object_obj_dialogurer_Step  a push was done for a conditional, then a B
                 //   var stackIssue = MatchPatern(false, o => o.isConditional, o => o.Op == OpType.B && o.Pc == final_destnation); // This should be changed to BF humm
               //     if (stackIssue != null) foreach (Opcode o in indirectB) o.Op = OpType.Bf; // we don't care about the target branch type

                    // order matters since we could have braches at the top that might go here
                    var indirectBF = MatchPatern(false, o => o.isBranch && (o.Address == match[0].Pc || o.Address == match[2].Pc));
                    System.Diagnostics.Debug.Assert(indirectBF == null); // There is NO reason why this shouldn't be nul.  No reason to branch to these two address
                                                                         // Since this poitns to a push.E 0, and the next instruction is a BF so it will ALWAYS jump, change the offset of this to 3
                    if (indirectB != null) foreach (Opcode o in indirectB) { o.Address = final_destnation; }//  o.Op = OpType.B; }
                    // we don't care about the target branch type
                    // Change that B to BF but not yet
                     //  codes.Add(inlineBranch.Pc, inlineBranch); // put it back cause we need it for the previous conditional!

                    // This saves some code.  Its either watch it on the branch decompile stage for inline opcodes
                    // OR modify the existing opcodes to fix this issue
                    //      inlineBranches.Add(match[0].Pc, inlineBranch); // make it inline as well for any targets
                    //       inlineBranches.Add(match[2].Pc, inlineBranch); // make it inline as well for any targets

                    // Second, we need to find, and there SHOULD be one out there, of some branch that points to the Push.E 0
                    System.Diagnostics.Debug.WriteLineIf(indirectB == null, "No B match for the scond bit?  What is the compiler thinking?");
                    

                }
            }
            for (int i = 0; i < list.Count; i++)
            {
                var match = MatchPatern(false, o => o.isConditional, o => o.Op == OpType.B);
                if(match != null)
                {
                  //  Debug.WriteLine("Meh " + i);
                }
            }
                System.Diagnostics.Debug.WriteLineIf(fix_count > 0, "Amount of peephole fixes: " + fix_count);
        }
        Stack<AST> tree = new Stack<AST>(); // used to caculate stuff
        Stack<AST> enviroment = new Stack<AST>();
        CodeLine processOpcode(Opcode code, int pc, ref int start_pc, ref int last_pc)
        {
            last_pc = pc;
            pc = code.Pc;
            string codeLine = null;
            if (code.Op != OpType.BadOp && start_pc == -1) start_pc = pc;
            CodeLine line = new CodeLine();
            line.code = code;
            line.pc = pc;
            line.startPc = -1;
            switch (code.Op)
            {
                case OpType.BadOp:
                    break;
                // A hack for push enviroment right here.
                // It SEEMS that push enviroment is set up so we can call a function that is in an instance
                // aka self.myobject.instance_destroy()
                // Sence we know the start and the end of the call, lets see if we can simplfiy this with 
                // recording the start of a push env and back.
                // This might not work well with recursive returns or calls though
                case OpType.Pushenv:
                    enviroment.Push(tree.Pop());
                    codeLine = "Pushing Enviroment : " + GetInstance(tree, enviroment.Peek().ToString());
                    // codeLine = "Push Enviroment " + tree.Pop().ToString() + "goto on error " + checkLabel(op);
                    break;
                case OpType.Popenv:
                    {
                        AST env = enviroment.Pop();
                        string instance = GetInstance(tree, env.ToString());
                        codeLine = "Poping  Envorment :  " + instance;
                    }
                        break;
                case OpType.Exit:
                    codeLine = "Exit";
                    System.Diagnostics.Debug.Assert(tree.Count == 0);
                    break;
                case OpType.Not: 
                    // This op is CLEARLY an op.  It was hard verified after I saw a return call from a function, a conv from a var to a bool
                    // and THIS statment doing a bool to a double
                    {
                        AST value = tree.Pop();
                        tree.Push(new Variable("!(" + value.ToString() + ")", GM_Type.Var));
                    }
                    break;
                case OpType.Neg: // sure this is a negitive
                    {
                        AST value = tree.Pop();
                        tree.Push(new Variable("-(" + value.ToString() + ")", GM_Type.Var));
                    }
                    break;
                case OpType.Ret:
                    {
                        AST value = tree.Pop();
                        codeLine = "return " + value.ToString();
                    }
                    break;
                case OpType.Mul:
                case OpType.Div:
                case OpType.Rem:
                case OpType.Mod:
                case OpType.Add:
                case OpType.Sub:
                case OpType.Or:
                case OpType.And:
                case OpType.Xor:
                case OpType.Sal:
                case OpType.Slt:
                case OpType.Sle:
                case OpType.Seq:
                case OpType.Sge:
                case OpType.Sgt:
                case OpType.Sne:

                    ProcessMathOp(tree, code.Raw);
                    break;

                case OpType.Conv:
                    {
                         GM_Type fromType = (GM_Type)(int)((code.Raw >> 16) & 0xF);
                         GM_Type tooType = (GM_Type)(int)((code.Raw >> 20) & 0xF);
                        tree.Push(new Conv(tree.Pop(), fromType, tooType));  
                    }
                    break;
                case OpType.Push: // most common.  Used for all math operations
                    {
                        GM_Disam.PushOpcode popcode = code as GM_Disam.PushOpcode;
                        switch (popcode.OperandType)
                        {
                            case GM_Type.Double: tree.Push(new Variable(popcode.OperandValueDouble)); break;
                            case GM_Type.Float: tree.Push(new Variable((float)popcode.OperandValueDouble)); break;
                            case GM_Type.Long: tree.Push(new Variable((long)popcode.OperandValue)); break;
                            case GM_Type.Int: tree.Push(new Variable((int)popcode.OperandValue)); break;
                            case GM_Type.Var: ProcessVarPush(tree, popcode); break;
                            case GM_Type.String:
                                {
                                    string value = cr.stringList[(int)popcode.OperandValue].str;
                                    tree.Push(Variable.CreateString(value));
                                }
                                break;
                            case GM_Type.Short: tree.Push(new Variable((short)popcode.OperandValue)); break;
                            default:
                                throw new Exception("Bad type");
                        }
                        
                    }
                    break;
                case OpType.Pop:
                    codeLine = ProcessAssignPop(tree, code as GM_Disam.PopOpcode);
                    break;
                case OpType.Popz: // usally on void funtion returns, so just pop the stack and print it
                    {
                        codeLine = tree.Pop().ToString();
                    }
                    break;
                case OpType.Dup:
                    {
                        short extra = (short)(code.Raw & 0xFFFF); // humm extra data? OR COPY THE TOP TWO ON THE STACK MABE
                        if (extra > 0)
                        {
                            AST top = tree.ElementAt(0);
                            AST second = tree.ElementAt(1);
                            tree.Push(second);
                            tree.Push(top);
                        } else tree.Push(tree.Peek());
                        System.Diagnostics.Debug.Assert(extra == 0 || extra == 1);
                        System.Diagnostics.Debug.Assert(extra == 0);



                        //    tree.Push(tree.First()); // will this work?
                        //   Variable v = tree.First() as Variable;
                        //   tree.First()
                        //   if (v == null) throw new ArgumentException("Dup didn't work, meh");
                        //  tree.Push(v.Dup());
                    }
                    break;
                case OpType.Break:
                    {
                        codeLine = String.Format("break {0}", code.Raw & 0xFFFF); // have yet to run into this
                    }
                    break;
                case OpType.Call:
                    ProcessCall(tree, code as GM_Disam.CallOpcode);
                    break;
                default:
                    //      System.Diagnostics.Debug.WriteLine("Not implmented at {0} value {1} valuehex {1:X}", r.Position, op, op);
                    throw new NotImplementedException("Opcode not implmented"); // NO MORE FUCKING AROUND HERE
            }
            //  int startPosition = r.Position;
            if (codeLine != null)
            {
                line.startPc = start_pc;
                start_pc = -1;
                line.endPC = pc;
                line.Text = codeLine;
                codeLine = null;
            }
            return line;
        }

        void DoBranch(Stack<AST> stack, List<CodeLine> lines, OffsetOpcode code, int pc, ref int start_pc, ref int last_pc)
        {
            string codeLine = null;
            var last = codes.Last();
            CodeLine line = new CodeLine();
            line.code = code;
            line.pc = pc;
            line.startPc = start_pc;
            line.endPC = pc;
            line.Text = codeLine;
            lines.Add(line);

            start_pc = -1;
            switch (code.Op)
            {
                case OpType.Bf:
                    line.Text  = ProcessBranch(stack, GetLabel(code.Address), false);
                    break;
                case OpType.Bt:
                    line.Text = ProcessBranch(stack, GetLabel(code.Address), true);
                    break;
                case OpType.B:
                    Debug.Assert(stack.Count == 0);
                    line.Text = "goto " + GetLabel(code.Address);
                    break;
                default:
                    throw new Exception("Can't be called here");
            }
            OffsetOpcode vop; // if we have an inline branch, we got to call it here
            if (inlineBranches.TryGetValue(code.Address, out vop) ){
                // love recursion
                int fake_start_pc = 0, fake_end_pc = 0; // dosn't matter as -1 pc means its virtual
                DoBranch(stack, lines, vop, -1, ref fake_start_pc, ref fake_end_pc);
            }
        }
        void PurgeExtraLines(List<CodeLine> lines)
        {

        }
        // We find if statments that are single statments and try to consoidate them int a single one
        //  Since we have eveything flattened, photoshop term look it up, we have to use regedit to fix
        // the strings and gotos
        // because of my shit coading and the lack of proper AST processing, we can do this, so ERASE THIS
        // if we ever do proper AST trees
        // can probery make this a class
        // Sigh.  I was SOOOOO CLOSE to scraping the CodeLine object and making proper Statment and AST
        // tree objects.  But this works.  Its either that or properly ASTing labels.  Sigh.
        static Dictionary<string, string> invertIfString = new Dictionary<string, string>()
        {
            {"==","!=" },
              {"!=","==" },
            {">=","<" },
            {"<=",">" },
            {">","<=" },
            {"<",">=" },
            { "!", "" }
        };
        void FixSimpleEnviroment(List<CodeLine> lines)
        {
            Regex findPushEnv = new Regex(@"Pushing\s+Enviroment\s*:\s*(\w+)", RegexOptions.Compiled);
            Regex findPopEnv = new Regex(@"Poping\s+Envorment\s*:\s*(\w+)", RegexOptions.Compiled);
            Regex findInstanceDestroy = new Regex(@"(instance_destroy\(\))", RegexOptions.Compiled);
            int index = 0;
            int start = 0;
            Func<Regex, string> FindMatch = (Regex r) =>
            {
                for (int i = index ; i < lines.Count; i++)
                {
                    if (lines[i].Text == null) continue;
                    Match m = r.Match(lines[i].Text);
                    if (m.Success) {
                        index = i;
                        return m.Groups[1].Value;
                    }
                }
                return null;
            };
            while(index < lines.Count)
            {
                string env = FindMatch(findPushEnv);
                if (env == null) break;
                start = index;
                string destroy = FindMatch(findInstanceDestroy);
                if (destroy == null) continue;
                string endenv = FindMatch(findPopEnv);
                Debug.Assert(env == endenv);
                destroy = env + "." + destroy;
                for (int j = start+1; j <= index; j++)
                {
                    CodeLine c = lines[j];
                    c.Text = null;
                    c.Label = null;
                }
                lines[start].Text = destroy;
            }


        }
        void SimplifyIFStatements(List<CodeLine> lines)
        {
            Regex findLabelRegex = new Regex("Label_\\d+", RegexOptions.Compiled);
            Regex findConditionalStatment = new Regex(@"(?<=if).*?(?=then)", RegexOptions.Compiled);
            Regex replaceLabel = new Regex("goto\\s*Label_\\d+", RegexOptions.Compiled);
            Regex InvertLogic = new Regex(@"==|!=|>=|<=|>|<|!", RegexOptions.Compiled);
            int index = 0;
            string fixed_if = "";
            string currentLabel = "";
            CodeLine current = null;
            CodeLine statment = null;
            int start = 0;
            Func<string, string> ProcessIfStatment = (string func) =>
            {
                Match conditional = findConditionalStatment.Match(func);
                return InvertLogic.Replace(conditional.Value, m => invertIfString[m.Value]);
            };
            Func<CodeLine> FindNextStatment = () =>
            {
                for (int i = index + 1; index < lines.Count; i++)
                {
                    CodeLine c = lines[i];
                    if (c.Text != null && c.Label == null)
                    {
                        if (c.Text.IndexOf("if") == 0)
                        { // if we have another if statment going to the same label right AFTER this one, then lets combine them
                            if (c.Text.IndexOf(currentLabel) != -1)
                            {
                                fixed_if += " && " + ProcessIfStatment(c.Text);
                                continue;
                            }
                            return null;
                        }
                        index = i;
                        return c;
                    }
                }
                return null;
            };
            Func<CodeLine> CheckIfNextisLabel = () =>
            {
                for (int i = index + 1; index < lines.Count; i++)
                {
                    CodeLine c = lines[i];
                    if (c.Text == null && c.Label == null) continue;
                    if (c.Text != null) return null;
                    index = i;
                    return c;
                }
                return null;
            };
            int conditional_max_length = 0;
            for (index = 0; index < lines.Count; index++)
            {
                current = lines[index];
                if (current.Text == null || current.Text.IndexOf("if") != 0) continue; // find the if statement
                start = index;
                fixed_if = ProcessIfStatment(current.Text);
                currentLabel = findLabelRegex.Match(current.Text).Value;
                statment = FindNextStatment();
                if (statment == null) continue;
                CodeLine label = CheckIfNextisLabel();
                if (label == null || label.Label != currentLabel) continue;
                // WE FOUND A MATCH LETS FIX THE FUCKER
                if (fixed_if.Length > conditional_max_length) conditional_max_length = fixed_if.Length;
                fixed_if = "if " + fixed_if + " then " + statment.Text;
                for (int j = start; j <= index; j++)
                {
                    CodeLine c = lines[j];
                    c.Text = null;
                    c.Label = null;
                }

                current.Text = fixed_if;
            }
            string newFormat = "{0,-" + (conditional_max_length + 3) + "} {1}";
            for (index = 0; index < lines.Count; index++)
            {
                current = lines[index];
                if (current.Text == null || current.Text.IndexOf("if") != 0 || current.Text.IndexOf("goto") != -1) continue; // find one of the modified statments
                int thenIndex = current.Text.IndexOf("then");
                string firstpart = current.Text.Substring(0, thenIndex).Trim();
                string secondpart = current.Text.Substring(thenIndex).Trim();
                current.Text = String.Format(newFormat, firstpart, secondpart);
            }
        }
        List<CodeLine> processStream2(ChunkStream r, int codeOffset, int code_size,string scriptName)
        {
            this.scriptName = scriptName;
            GM_Disam disam = new GM_Disam(r);
            this.codes = disam.ReadOpCode(codeOffset, code_size, scriptName);
            PeepholeFix();
            gotos = new Dictionary<int, string>();
            int limit = (int)(codeOffset + code_size);
            tree = new Stack<AST>(); // used to caculate stuff
            enviroment = new Stack<AST>();

            List<CodeLine> lines = new List<CodeLine>();
            int start_pc = -1, last_pc = 0;
            int end_pc = codes.Last().Key;
            for (int pc = 0; pc < end_pc; pc++) {
                if (!codes.ContainsKey(pc)) continue;
                Opcode code = codes[pc];
                //System.Diagnostics.Debug.Assert(pc != 1951);
                switch (code.Op)
                {
                    
                    case OpType.Bf: // branch if false
                    case OpType.Bt: // branch if true
                    case OpType.B:
                         DoBranch(tree, lines, code as OffsetOpcode, pc, ref start_pc, ref last_pc);
                        break;
        
                    default:
                        lines.Add(processOpcode(code, pc, ref start_pc, ref last_pc));
                        continue;
                }
            }
            InsertLabels(lines, end_pc);
            FixSimpleEnviroment(lines);
            SimplifyIFStatements(lines);
            return lines;
        }
    }
}