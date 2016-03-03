﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Collections.ObjectModel;
using System.Collections;
using System.Diagnostics;

namespace betteribttest
{


    public enum GM_Type : byte
    {
        Double = 0,
        Float,
        Int,
        Long,
        Bool,
        Var,
        String,
        Instance,
        Short = 15, // This is usally short anyway
        NoType
    }
    public enum GMCode : byte
    {
        BadOp = 0x00, // used for as a noop, mainly for branches hard jump location is after this
        Conv = 0x03,
        Mul = 0x04,
        Div = 0x05,
        Rem = 0x06,
        Mod = 0x07,
        Add = 0x08,
        Sub = 0x09,
        And = 0x0a,
        Or = 0x0b,
        Xor = 0x0c,
        Neg = 0xd,
        Not = 0x0e,
        Sal = 0x0f,
        Slt = 0x11,
        Sle = 0x12,
        Seq = 0x13,
        Sne = 0x14,
        Sge = 0x15,
        Sgt = 0x16,
        Pop = 0x41,
        Dup = 0x82,
        Ret = 0x9d,
        Exit = 0x9e,
        Popz = 0x9f,
        B = 0xb7,
        Bt = 0xb8,
        Bf = 0xb9,
        Pushenv = 0xbb,
        Popenv = 0xbc,
        Push = 0xc0,
        Call = 0xda,
        Break = 0xff
    }
    public static class GMCodeUtil
    {
        public static int getBranchOffset(uint op)
        {
            if ((op & 0x800000) != 0) op |= 0xFF000000; else op &= 0x00FFFFFF;
            return (int)(op);
        }
        public readonly static Dictionary<GMCode, int> opMathOperationCount = new Dictionary<GMCode, int>()  {
            {  (GMCode)0x04,2},
            {  (GMCode)0x05, 2},
            { (GMCode) 0x06, 2},
            {  (GMCode)0x07, 2 },
            {  (GMCode)0x08, 2},
            {  (GMCode)0x09, 2 },
            {  (GMCode)0x0a, 2 },
            {  (GMCode)0x0b, 2 },
            { (GMCode) 0x0c, 2 },
            { (GMCode) 0x0d,1},
            {  (GMCode)0x0e, 1 },

            {  (GMCode)0x0f, 2},
            {  (GMCode)0x10, 2 },
            { (GMCode) 0x11, 2},
            { (GMCode) 0x12, 2 },
            { (GMCode) 0x13, 2},
            {  (GMCode)0x14, 2},
            {  (GMCode)0x15, 2},
            {  (GMCode)0x16, 2 },
        };
        public static uint toUInt(this GMCode t, int operand)
        {
            byte b = (byte)t;
            return (uint)(b << 24) | (uint)(0x1FFFFFF | operand);
        }
        public static uint toUInt(this GMCode t)
        {
            byte b = (byte)t;
            return (uint)(b << 24);
        }
        public static uint toUInt(this GMCode t, GM_Type type)
        {
            byte b = (byte)t;
            byte bt = (byte)type;
            return (uint)(b << 24) | (uint)(bt << 16);
        }
        public static int getOpTreeCount(this GMCode t)
        {
            int count;
            if (opMathOperationCount.TryGetValue(t, out count)) return count;
            return 0;
        }
        public static string getOpTreeString(this GMCode t)
        {
            string ret;
            if (opMathOperation.TryGetValue(t, out ret)) return ret;
            return null;
        }
        public static GMCode getInvertedOp(this GMCode t)
        {
            switch (t)
            {
                case GMCode.Sne: return GMCode.Seq;
                case GMCode.Seq: return GMCode.Sne;
                case GMCode.Sgt: return GMCode.Sle;
                case GMCode.Sge: return GMCode.Slt;
                case GMCode.Slt: return GMCode.Sge;
                case GMCode.Sle: return GMCode.Sgt;
                default:
                    return GMCode.BadOp;
            }
        }
        public readonly static Dictionary<GMCode, string> opMathOperation = new Dictionary<GMCode, string>()  {
            {  (GMCode)0x03, "conv" },
            {  (GMCode)0x04, "*" },
            {  (GMCode)0x05, "/" },
            { (GMCode) 0x06, "rem" },
            {  (GMCode)0x07, "%" },
            {  (GMCode)0x08, "+" },
            {  (GMCode)0x09, "-" },
            {  (GMCode)0x0a, "&" },
            {  (GMCode)0x0b, "|" },
            { (GMCode) 0x0c, "^" },
            { (GMCode) 0x0d, "~" },
            {  (GMCode)0x0e, "!" },

            {  (GMCode)0x0f, "<<" },
            {  (GMCode)0x10, ">>" },
            { (GMCode) 0x11, "<" },
            { (GMCode) 0x12, "<=" },
            { (GMCode) 0x13, "==" },
            {  (GMCode)0x14, "!=" },
            {  (GMCode)0x15, ">=" },
            {  (GMCode)0x16, ">" },
        };
        public static Dictionary<int, string> instanceLookup = new Dictionary<int, string>()
        {
            {  0 , "stack" },
            {  -1, "self" },
            {  -2, "other" },
            {  -3, "all" },
            {  -4, "noone" },
            {  -5, "global" },
        };
        public static string lookupInstance(short instance, List<string> InstanceLookup = null)
        {
            string ret;
            if (instanceLookup.TryGetValue(instance, out ret)) return ret;
            else if (InstanceLookup != null && instance < InstanceLookup.Count) return InstanceLookup[instance];
            else return String.Format("%{0}%", instance);
        }
        public static string lookupInstanceFromRawOpcode(uint opcode, List<string> InstanceLookup = null)
        {
            return lookupInstance((short)(opcode & 0xFFFF), InstanceLookup);
        }
        public static bool IsArrayPushPop(uint operand)
        {
            return (operand & 0x8000000) != 0;
        }
        public static string EscapeString(string s)
        {
            using (var writer = new StringWriter())
            {
                using (var provider = System.CodeDom.Compiler.CodeDomProvider.CreateProvider("CSharp"))
                {
                    provider.GenerateCodeFromExpression(new System.CodeDom.CodePrimitiveExpression(s), writer, null);
                    return writer.ToString();
                }
            }
        }
        public static GMCode getFromRaw(uint raw)
        {
            return (GMCode)((raw >> 24) & 0xFF);
        }
        public static string GetName(this GMCode code)
        {
            return code.ToString().ToLowerInvariant().TrimStart('_').Replace('_', '.');
        }
        public static int GetSize(this GM_Type t)
        {
            switch (t)
            {
                case GM_Type.Double:
                case GM_Type.Long:
                    return 2;
                case GM_Type.Short:
                    return 0;
                default:
                    return 1;
            }
        }
        public static int OpCodeSize(uint raw)
        {
            switch ((GMCode)(raw >> 24))
            {
                case GMCode.Pop:
                case GMCode.Call:
                    return 2;
                case GMCode.Push:
                    return ((GM_Type)((raw >> 16) & 0xF)).GetSize() + 1;
                default:
                    return 1;
            }
        }
        public static string TypeToStringPostfix(int type) { return TypeToStringPostfix((GM_Type)(type & 15)); }
        public static string TypeToStringPostfix(this GM_Type t)
        {
            switch (t)
            {
                case GM_Type.Double: return ".d";
                case GM_Type.Float: return ".f";
                case GM_Type.Int: return ".i";
                case GM_Type.Long: return ".l";
                case GM_Type.Bool: return ".b";
                case GM_Type.Var: return ".v";
                case GM_Type.String: return ".s";
                case GM_Type.Short: return ".e";
            }
            throw new Exception("Not sure how we got here");
        }
        public static bool IsUnconditionalControlFlow(this GMCode code)
        {
            return code == GMCode.B;
        }
        public static bool IsConditional(this GMCode code)
        {
            return code == GMCode.Bt || code == GMCode.Bf;
        }
        public static bool isBranch(this GMCode code)
        {
            return code == GMCode.Bt || code == GMCode.Bf || code == GMCode.B;
        }
        public static bool IsConditionalStatment(this GMCode code)
        {
            switch (code)
            {
                case GMCode.Seq:
                case GMCode.Sne:
                case GMCode.Sge:
                case GMCode.Sle:
                case GMCode.Sgt:
                case GMCode.Slt:
                    return true;
                default:
                    return false;

            }
        }
    }

    public interface ITextOut
    {
        /// <summary>
        /// Writes a line of text, normaly its from the toString()
        /// </summary>
        /// <param name="wr">TextWriterStream</param>
        /// <returns>Number of lines written, 0 means no new line was written</returns>
        int WriteTextLine(TextWriter wr);
    }
    public sealed class Label :  IComparable<Label>, IComparable<Instruction>, IEquatable<Label>, ITextOut
    {
        internal List<LinkedListNode<Instruction>> _callsTo; 
        public IReadOnlyList<LinkedListNode<Instruction>> CallsTo { get { return _callsTo; } }
        public int Target { get; internal set; }
   
        public LinkedListNode<Instruction> InstructionOrigin { get; set; }
        private Label(List<LinkedListNode<Instruction>> callsTo, int target, LinkedListNode<Instruction> origin)
        {
            InstructionOrigin = origin;
            _callsTo = new List<LinkedListNode<Instruction>>(callsTo);
            Target = target;
        }
        public Label(Label l)
        {
            Target = l.Target;
            InstructionOrigin = null;
            _callsTo = new List<LinkedListNode<Instruction>>();
        }
        public Label Copy()
        {
            Label l = new Label(this._callsTo,Target,InstructionOrigin);
            return l;
        }
        internal Label(int target)
        {
            this.Target = target;
            _callsTo = new List<LinkedListNode<Instruction>>();
            InstructionOrigin = null;
        }
        internal Label(Instruction i)
        {
            this.Target = i.Offset + GMCodeUtil.getBranchOffset(i.OpCode);
            _callsTo = new List<LinkedListNode<Instruction>>();
            InstructionOrigin = null;
        }
        public int ResolveCallsTo(LinkedList<Instruction> i)
        {
            _callsTo.Clear();
            // can't use linq sadly
            var start = i.First;
            InstructionOrigin = null;
            while (start != null)
            {
                if (start.Value.Offset == Target) start.Value.Label = this;
                Label operand = start.Value.Operand as Label;
                if (operand != null && operand == this) _callsTo.Add(start);
                else if (start.Value.Label == this) InstructionOrigin = start;


                start = start.Next;
            }
            return _callsTo.Count;
        }
        public int WriteTextLine(TextWriter wr)
        {
            wr.Write(this.ToString());
            return 0;
        }
        public override string ToString()
        {
            if (Target == 0)
                return "LStart";
            else
                return String.Format("L{0}", Target);
        }
        public override bool Equals(object obj)
        {
            if (Object.ReferenceEquals(obj, null)) return false;
            if (obj.GetType() != this.GetType()) return false;
            if (Object.ReferenceEquals(obj, this)) return true;
            Label l = obj as Label;
            if (l == null) return false;
            return Equals(l);
        }
        public override int GetHashCode()
        {
            return Target;
        }

        public int CompareTo(Label other)
        {
            return Target.CompareTo(other.Target);
        }
        public int CompareTo(Instruction other)
        {
            return Target.CompareTo(other.Offset);
        }
        public bool Equals(Label l)
        {
            return l.Target == Target;
        }
    }
    public sealed class Instruction :  IComparable<Label>, IComparable<Instruction>, IEquatable<Instruction>, ITextOut
    {
        const uint breakPopenvOpcode = (uint)(((uint)(GMCode.Popenv)) << 24 | 0x00F00000);
        byte[] raw_opcode;
        int _operandInt;
        // makes it a bad op code that just has the offset and the operand
        public string Comment { get; set; }
        public int Offset { get; private set; }
        public Label Label { get;  set; }
        public uint OpCode { get; private set; }
        public GMCode GMCode { get { return GMCodeUtil.getFromRaw(OpCode); } }
        public object Operand { get; set; }
        public int OperandInt { get { return _operandInt; } }
        internal Instruction(int offset, uint opCode) 
        {
            this.Offset = offset;
            this.OpCode = opCode;
            this.Operand = null;
            this._operandInt = 0;
            this.Label = null;
            this.Comment = null;
        }
        internal Instruction(int offset, BinaryReader r) 
        {
            this.Operand = null;
            this.Label = null;
            this.Offset = offset;
            // raw_opcode = r.ReadBytes(sizeof(int));
            this.OpCode = r.ReadUInt32();
            raw_opcode = BitConverter.GetBytes(this.OpCode);
            if (Size > 1)
            {
               // Array.Resize(ref raw_opcode, size * sizeof(int));
              //  r.Read(raw_opcode, 4, (size - 1) * sizeof(int));
                switch (this.GMCode)
                {
                    case GMCode.Call:
                    case GMCode.Pop:
                        Operand = _operandInt= r.ReadInt32();
                        break;
                    case GMCode.Push:
                        {
                            GM_Type eVMType = (GM_Type)((OpCode >> 16) & 15);
                            switch (eVMType)
                            {
                                case GM_Type.Long:
                                    Operand = r.ReadInt64();
                                    break;
                                case GM_Type.Double:
                                    Operand = r.ReadDouble();
                                    break;
                                case GM_Type.Float:
                                    Operand = r.ReadSingle();
                                    break;
                                case GM_Type.Bool:
                                    Operand = BitConverter.ToInt32(raw_opcode, 4) != 0 ? true : false;
                                    break;
                                case GM_Type.String: 
                                case GM_Type.Var:
                                     _operandInt = r.ReadInt32() ;
                                     Operand = _operandInt & 0x1FFFFF;
                                    break;
                                case GM_Type.Int:
                                    Operand = r.ReadInt32();
                                    break;
                            }
                        }
                        break;
                }
                System.Diagnostics.Debug.Assert(Operand != null);
            }
        }
        // Screw it, this is hemerging memory anyway
        public List<string> InstanceLookup = null;
        public List<string> StringLookup = null;
        // Common enough for me to put it here
        public short Instance
        {
            get
            {
                return (short)(OpCode & 0xFFFF);
            }
        }
        public int Size
        {
            get
            {
                return GMCodeUtil.OpCodeSize(OpCode);
            }
        }
        public GM_Type FirstType
        {
            get
            {
                return (GM_Type)((OpCode >> 16) & 0x0F);
            }
        }
        public GM_Type SecondType
        {
            get
            {
                return (GM_Type)((OpCode >> 20) & 0x0F);
            }
        }
        public int GetOperandPatch()
        {
            if (Operand != null && Operand is int) return (int)Operand & 0x1FFFFFF;
            throw new Exception("Needed that operand");
        }
        void FormatPrefix(StringBuilder line,int opcode,int type) {
            line.AppendFormat("{0,-5}{1,-5} ", Offset, this.Label == null ? "" : this.Label.ToString());
            line.Append(this.GMCode.GetName());
            if ((opcode & 160) == 128)
            {
                line.Append(GMCodeUtil.TypeToStringPostfix(type & 0xF));
            }
            else if ((opcode & 160) == 0)
            {
                line.Append(GMCodeUtil.TypeToStringPostfix(type & 0xF));
                line.Append(GMCodeUtil.TypeToStringPostfix(type >> 4));
            }
            // so we should have 28 spaces here
            if (line.Length < 21) line.Append(' ', 21 - line.Length);
        }
        void FormatPostFix(StringBuilder line)
        {
            if(Comment != null || Label != null)
            {
                if (line.Length < 40) line.Append(' ', 40 - line.Length); // make sure all the comments line up
                line.Append("; ");
                if(Label != null)
                {
                    line.Append("Label[");
                    bool need_comma = false;
                    foreach(var c in Label.CallsTo)
                    {
                        if (need_comma) line.Append(',');
                        line.Append(c.Value.Offset);
                        need_comma = true;
                    }
                    line.Append("] ");
                }
                if (Comment != null) line.Append(Comment);
             }
        }
        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            WriteTextLine(sw);
            return sw.ToString();
        }
        public int WriteTextLine(TextWriter wr)
        {
            StringBuilder line = new StringBuilder();
            int opcode = (int)((OpCode >> 24) & 0xFF);
            int type = (int)((OpCode >> 16) & 0xFF);
            string str;

            FormatPrefix(line,opcode,type); // both type prints are caculated in prefix

           // if(GMCode.IsConditional() && Operand is Label)
           if(Operand is Label)
            {
                str = Operand.ToString();
                line.Append(str);
            }
            else if ((opcode & 64) != 0) // we have an operand
            {
                GM_Type eVMType = (GM_Type)(type & 15);
                switch (eVMType)
                {
                    case GM_Type.Double:
                    case GM_Type.Int:
                    case GM_Type.Long:
                    case GM_Type.Bool:
                        line.Append(Operand.ToString());
                        break;
                    case GM_Type.Var:
                        line.Append(GMCodeUtil.lookupInstanceFromRawOpcode(OpCode, InstanceLookup));
                        line.Append('.');
                        if (Operand is int)
                        {
                            int operand = (int)Operand & 0x1FFFFFF;
                            if (StringLookup != null) line.Append(StringLookup[operand]);
                            else line.AppendFormat("${0}$", operand);        
                        } else line.Append(Operand.ToString());
                        if (GMCodeUtil.IsArrayPushPop((uint)_operandInt)) line.Append("[]");
                        break;
                    case GM_Type.String:
                        if (Operand is int)
                            line.AppendFormat("\"{0}\"", ((int)Operand & 0x1FFFFFF));
                        else goto case GM_Type.Bool;

                        break;
                    case GM_Type.Short:
                        line.Append((short)(OpCode << 16 >> 16));
                        break;
                }
            }
            else if ((opcode & 32) != 0)
            { //// ooooooooooh
                if(OpCode == breakPopenvOpcode)
                {
                    line.Append("break enviroment");

                } else if (GMCode.IsConditional() || GMCode == GMCode.Pushenv || GMCode == GMCode.Popenv)
                {
                    int offset = GMCodeUtil.getBranchOffset(OpCode);
                    line.Append(Offset-offset);
                } else
                {
                    int new_offset = Offset + (int)(OpCode << 8 >> 8); // << 8 >> 6 // changed cause we are doing pc by int
                    line.AppendFormat("0x{0:x8}", new_offset);
                }
            }
            FormatPostFix(line);
            wr.Write(line.ToString());
            return 0;
        }
        public class Instructions : LinkedList<Instruction>, ITextOut
        {

            public SortedDictionary<int, Label> labels = new SortedDictionary<int, Label>();
            public SortedList<int, LinkedListNode<Instruction>> list = new SortedList<int, LinkedListNode<Instruction>>();
            public LinkedList<Instruction> linkedList = new LinkedList<Instruction>();
            public Instructions() : base() { }
            public LinkedListNode<Instruction> MatchFrom(LinkedListNode<Instruction> start, Predicate<Instruction> match)
            {
                while (start != null) if (match(start.Value)) return start;
                return null;
            }
            public IEnumerable<Instruction> MatchMany(Predicate<Instruction> match)
            {
                var start = First;
                while (start != null) if (match(start.Value)) yield return start.Value;
            }
            public Instruction MatchOne(Predicate<Instruction> match)
            {
                var start = MatchFrom(First, match);
                return start != null ? start.Value : null;
            }

            public IEnumerable<LinkedListNode<Instruction>> MatchMany(params Predicate<Instruction>[] toMatch)
            {
                var start = First;
                List<Instruction> ret = new List<Instruction>();
                while (start != null)
                {
                    var match_node = start;
                    var testNode = start;
                    ret.Clear();
                    foreach (var match in toMatch)
                    {
                        if (testNode == null) yield break; // we are at the end of the list
                        if (!match(testNode.Value)) break; 
                        ret.Add(testNode.Value);
                        testNode = testNode.Next;
                    }
                    start = testNode.Next; // get the next node in case we delete the previous nodes
                    if (ret.Count == toMatch.Length) yield return match_node;
                }
            }
            public int RemoveAll(Predicate<Instruction> match)
            {
                if (list == null)
                {
                    throw new ArgumentNullException("list");
                }
                if (match == null)
                {
                    throw new ArgumentNullException("match");
                }
                var count = 0;
                var node = First;
                
                while (node != null)
                {
                    var next = node.Next;
                    if (match(node.Value))
                    {
                        Remove(node);
                        count++;
                    }
                    node = next;
                }
                return count;
            }

            public int WriteTextLine(TextWriter wr)
            {
                int count = 0;
                foreach(var i in this)
                {
                    i.WriteTextLine(wr);
                    wr.WriteLine();
                    count++;
                }
                return count;
            }
        }
        // first pass simple create form a stream
        
        public static Instructions Create(System.IO.BinaryReader r, List<string> StringIndex, List<string> InstanceList=null)
        {
            r.BaseStream.Position = 0;
            Instructions instructions = new Instructions();
            int pc = 0;
            List<Label> LabelsOutsideOfFuntion = new List<Label>();
            long lastpc = r.BaseStream.Length / 4;
            while (r.BaseStream.Length > r.BaseStream.Position)
            {
                Instruction inst = new Instruction(pc, r);
                inst.InstanceLookup = InstanceList; // hack I know but I want prity print
                inst.StringLookup = StringIndex; 
                instructions.AddLast(inst);
                System.Diagnostics.Debug.Assert(inst.Offset == pc);
                if (inst.GMCode.isBranch() || inst.GMCode == GMCode.Pushenv || (inst.GMCode == GMCode.Popenv && inst.OpCode != breakPopenvOpcode))// || inst.GMCode == GMCode.Pushenv || inst.GMCode == GMCode.Popenv) // pushenv also has a branch 
                { 
                    int target = GMCodeUtil.getBranchOffset(inst.OpCode);
                    target += pc;
                    Label l;
                    if (!instructions.labels.TryGetValue(target, out l))
                    {
                        l = new Label(inst);
                        instructions.labels.Add(target, l);
                        if (target >= lastpc) LabelsOutsideOfFuntion.Add(l);
                    }
                    inst.Operand = l;
                }
                pc += inst.Size;
            }
            LabelsOutsideOfFuntion.Sort();
            foreach (var l in LabelsOutsideOfFuntion)
            {
                Instruction i = new Instruction(l.Target, GMCode.Exit.toUInt(GM_Type.Var));// add filler op 
                i.Label = l; // give it the label
                instructions.AddLast(i);
            }
            if (instructions.labels.Count > 0) foreach (var l in instructions.labels) l.Value.ResolveCallsTo(instructions);
           
            // now fix string indexes 
            foreach(var i in instructions)
            {
                if(i.Operand != null)
                {
                    switch (i.GMCode)
                    {
                        case GMCode.Push:
                            switch (i.FirstType)
                            {
                                case GM_Type.String:
                                    i.Operand = GMCodeUtil.EscapeString(StringIndex[(int)i.Operand]);
                                    break;
                                case GM_Type.Var:
                                    i.Operand = StringIndex[(int)i.Operand];
                                    break;
                            }
                            break;
                        case GMCode.Call:
                            i.Operand = StringIndex[(int)i.Operand & 0x1FFFFF];
                            break;
                        case GMCode.Pop:
                            i.Operand = StringIndex[(int)i.Operand & 0x1FFFFF];
                            break;
                    }
                }
            }
            return instructions;
        }
        public override bool Equals(object obj)
        {
            if (Object.ReferenceEquals(obj, this)) return true;
            Instruction l = obj as Instruction;
            if (l == null) return false;
            return Equals(l);
        }
        public override int GetHashCode()
        {
            return Offset;
        }
        public int CompareTo(Label other)
        {
            return Offset.CompareTo(other.Target);
        }
        public int CompareTo(Instruction other)
        {
            return Offset.CompareTo(other.Offset);
        }
        public bool Equals(Instruction other)
        {
            if (Object.ReferenceEquals(other, this)) return true;
            return other.Offset == Offset;
        }
 
    }
}