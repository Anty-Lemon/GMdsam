﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using betteribttest.Dissasembler;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace betteribttest
{
    static class Program
    {

        static GMContext context;
        static ILValue CheckConstant(ILExpression expr)
        {
            if (expr.Code == GMCode.Constant)
            {
                ILValue arg = expr.Operand as ILValue;
                if (arg != null) return arg;
            }
            return null;
        }
        static void spriteArgument(ILExpression expr)
        {
            if (expr.Code == GMCode.Constant)
            {
                ILValue arg = expr.Operand as ILValue;
                int instance;
                if (arg.TryParse(out instance))
                {
                    arg.ValueText = "\"" + context.IndexToSpriteName(instance) + "\"";
                }
            }
        }
        static void ordArgument(ILExpression expr)
        {
            if (expr.Code == GMCode.Constant)
            {
                ILValue arg = expr.Operand as ILValue;
                int instance;
                if (arg.TryParse(out instance))
                {
                    char c = (char) instance;
                    if (char.IsControl(c))
                        arg.ValueText = "\'\\x" + instance.ToString("X2") + "\'";
                    else
                        arg.ValueText = "\'" + c + "\'";
                }
            }
        }

        static void soundArgument(ILExpression expr)
        {
            if (expr.Code == GMCode.Constant)
            {
                ILValue arg = expr.Operand as ILValue;
                int instance;
                if (arg.TryParse(out instance))
                {
                    arg.ValueText = "\"" + context.IndexToAudioName(instance) + "\"";
                }
            }
        }
        static void instanceArgument(ILExpression expr)
        {
            if (expr.Code == GMCode.Constant)
            {
                ILValue arg = expr.Operand as ILValue;
                int instance;
                if (arg.TryParse(out instance))
                {
                    arg.ValueText = "\"" + context.InstanceToString(instance) + "\"";
                }
            }
        }
        static void fontArgument(ILExpression expr)
        {
            if (expr.Code == GMCode.Constant)
            {
                ILValue arg = expr.Operand as ILValue;
                int instance;
                if (arg.TryParse(out instance))
                {
                    arg.ValueText = "\"" + context.IndexToFontName(instance) + "\"";
                }
            }
        }
        // This just makes color look easyer to read
        static void colorArgument(ILExpression expr)
        {
            if (expr.Code == GMCode.Constant)
            {
                ILValue arg = expr.Operand as ILValue;
                int color;
                if (arg.TryParse(out color))
                {
                    byte red = (byte) (color & 0xFF);
                    byte green = (byte) (color >> 8 & 0xFF);
                    byte blue = (byte) (color >> 16 & 0xFF);
                    arg.ValueText = "Red=" + red + " ,Green=" + green + " ,Blue=" + blue;
                }
            }
        }
        static void scriptArgument(ILExpression expr)
        {
            if (expr.Code == GMCode.Constant)
            {
                ILValue arg = expr.Operand as ILValue;
                int instance;
                if (arg.TryParse(out instance))
                {
                    arg.ValueText = "\"" + context.IndexToScriptName(instance) + "\"";

                }
            }
        }
        static void scriptExecuteFunction(string n, List<ILExpression> l)
        {
            Debug.Assert(l.Count > 0);
            scriptArgument(l[0]);
        }
        static void instanceCreateFunction(string n, List<ILExpression> l)
        {
            Debug.Assert(l.Count == 3);
            instanceArgument(l[2]);
        }
        static void draw_spriteExisits(string n, List<ILExpression> l)
        {
            Debug.Assert(l.Count > 1);
            spriteArgument(l[0]);
        }

        static void instanceExisits(string n, List<ILExpression> l)
        {
            Debug.Assert(l.Count == 1);
            instanceArgument(l[0]);
        }
        static void instanceCollision_line(string n, List<ILExpression> l)
        {
            Debug.Assert(l.Count > 4);
            instanceArgument(l[3]);
        }
        static void soundPlayStop(string n, List<ILExpression> l)
        {
            Debug.Assert(l.Count > 4);
            instanceArgument(l[0]);
        }
        public class CallFunctionLookup
        {
            public delegate void FunctionToText(string funcname, List<ILExpression> arguments);
            public delegate void FunctionToComment(string funcname, ILExpression expr);
            Dictionary<string, FunctionToText> _lookup = new Dictionary<string, FunctionToText>();
            Dictionary<string, FunctionToComment> _functionComment = new Dictionary<string, FunctionToComment>();
            public void Add(string funcname, FunctionToText func) { _lookup.Add(funcname, func); }
            public void Add(string funcname, FunctionToComment func) { _functionComment.Add(funcname, func); }
            public void FixCalls(ILBlock block)
            {
                foreach (var call in block.GetSelfAndChildrenRecursive<ILExpression>(x => x.Code == GMCode.Call))
                {
                    string funcName = call.Operand.ToString();
                    FunctionToText func;
                    if (_lookup.TryGetValue(funcName, out func)) func(funcName, call.Arguments);
                    FunctionToComment funcCom;
                    if (_functionComment.TryGetValue(funcName, out funcCom)) funcCom(funcName, call);
                }
            }
        }
        public class AssignRightValueLookup
        {
            public delegate void ArgumentToText(ILExpression argument);
            Dictionary<string, ArgumentToText> _lookup = new Dictionary<string, ArgumentToText>();
            public void Add(string varName, ArgumentToText func) { _lookup.Add(varName, func); }
            public void FixCalls(ILBlock block)
            {
                // Check for assigns
                foreach (var push in block.GetSelfAndChildrenRecursive<ILExpression>(x => x.Code == GMCode.Assign))
                {
                    ArgumentToText func;
                    if (_lookup.TryGetValue(push.Arguments[0].ToString(), out func)) func(push.Arguments[0]);
                }
                // Check for equality
                foreach (var condition in block.GetSelfAndChildrenRecursive<ILExpression>(x => x.Code == GMCode.Seq || x.Code == GMCode.Sne))
                {
                    ArgumentToText func;
                    if (_lookup.TryGetValue(condition.Arguments[0].ToString(), out func)) func(condition.Arguments[1]);
                    else if (_lookup.TryGetValue(condition.Arguments[1].ToString(), out func)) func(condition.Arguments[0]);
                }
            }
        }
        static CallFunctionLookup FunctionFix = new CallFunctionLookup();
        static AssignRightValueLookup PushFix = new AssignRightValueLookup();
        static void Instructions()
        {
            Console.WriteLine("Useage <exe> data.win <-asm> [-s search_term] [-all (objects|scripts)");
            Console.WriteLine("search_term will search all scripts or object names for the text and save that file as a *.cpp");
            Console.WriteLine("-asm will also write the bytecode dissasembly");
            Console.WriteLine("There will be some wierd gotos/labels in case statements.  Ignore them, I am still trying to find that bug");
        }
        static void FunctionReplacement()
        {
            FunctionFix.Add("instance_create", instanceCreateFunction);
            FunctionFix.Add("collision_line", instanceCollision_line);
            FunctionFix.Add("instance_exists", instanceExisits);
            FunctionFix.Add("script_execute", scriptExecuteFunction);
            FunctionFix.Add("draw_sprite", draw_spriteExisits);
            FunctionFix.Add("draw_sprite_ext", draw_spriteExisits);
            FunctionFix.Add("snd_stop", (string name, List<ILExpression> l) =>
            {
                Debug.Assert(l.Count > 0);
                soundArgument(l[0]);
            });
            FunctionFix.Add("snd_play", (string name, List<ILExpression> l) =>
            {
                Debug.Assert(l.Count > 0);
                soundArgument(l[0]);
            });


            FunctionFix.Add("draw_set_font", (string funcname, List<ILExpression> l) =>
            {
                Debug.Assert(l.Count == 1);
                fontArgument(l[0]);
            });
            FunctionFix.Add("draw_set_color", (string funcname, List<ILExpression> l) =>
            {
                Debug.Assert(l.Count == 1);
                colorArgument(l[0]);
            });
            CallFunctionLookup.FunctionToComment keyboard_help = (string funcname, ILExpression expr)=> 
            {
                Debug.Assert(expr.Arguments.Count == 1);
                ILExpression arg = expr.Arguments[0];
                if (arg.Code == GMCode.Constant)
                { // this was never changed to ILValue?
                    do
                    {
                        int key = 0;
                        if (arg.Operand is ILValue)
                        {
                            if (!(arg.Operand as ILValue).TryParse(out key)) break;
                        }
                        else if (arg.Operand is int)
                        {
                            key = (int) arg.Operand;
                        }
                        else break;


                        expr.Comment = GMContext.KeyToString(key);

                    } while (false);
                }
               
            };
            FunctionFix.Add("keyboard_key_press", keyboard_help);
            FunctionFix.Add("keyboard_key_release", keyboard_help);
            FunctionFix.Add("keyboard_check", keyboard_help);
            FunctionFix.Add("keyboard_check_direct", keyboard_help);
            FunctionFix.Add("keyboard_check_released", keyboard_help);
            FunctionFix.Add("keyboard_multicheck", keyboard_help);
            FunctionFix.Add("keyboard_check_pressed", keyboard_help);
            FunctionFix.Add("keyboard_clear", keyboard_help);


            FunctionFix.Add("ord", (string funcname, List<ILExpression> l) =>
            {
                Debug.Assert(l.Count == 1);
                ordArgument(l[0]);
            });
            PushFix.Add("self.sym_s", spriteArgument);
            PushFix.Add("self.mycolor", colorArgument);
            PushFix.Add("self.myfont", fontArgument);
            PushFix.Add("self.txtsound", soundArgument);
        }
        static void DebugMain()
        {
            // before I properly set up Main
            //cr = new ChunkReader("D:\\Old Undertale\\files\\data.win", false); // main pc

            //  cr.DumpAllObjects("objects.txt");
            // cr = new ChunkReader("Undertale\\UNDERTALE.EXE", false);
            // cr = new ChunkReader("C:\\Undertale\\UndertaleOld\\data.win", false); // alienware laptop
            //Decompiler dism = new Decompiler(cr);
            FunctionReplacement();
            //  string filename_to_test = "undyne";
            //    string filename_to_test = "gasterblaster"; // lots of stuff  loops though THIS WORKS THIS WORKS!
            //   string filename_to_test = "sansbullet"; //  other is a nice if not long if statements
            // we assume all the patches were done to calls and pushes

            //  string filename_to_test = "gml_Object_OBJ_WRITER_Draw_0";// reall loop test as we got a break in it
            //  string filename_to_test = "gml_Object_OBJ_WRITER";// reall loop test as we got a break in it


            // string filename_to_test = "obj_face_alphys_Step"; // this one is good but no shorts
            // string filename_to_test = "SCR_TEXTTYPE"; // start with something even simpler
            string filename_to_test = "SCR_TEXT"; // start with something even simpler
                                                  //  string filename_to_test = "gml_Object_obj_dmgwriter_old_Draw_0"; // intrsting code, a bt?
                                                  // string filename_to_test = "write"; // lots of stuff
                                                  //string filename_to_test = "OBJ_WRITER";

            // dosn't work, still need to work on shorts too meh
            //  string filename_to_test = "gml_Object_OBJ_WRITER_Alarm_0"; // good switch test WORKS 5/15
            //  string filename_to_test = "GAMESTART";

            //   string filename_to_test = "Script_scr_asgface"; // WORKS 4/12 too simple
            //   string filename_to_test = "gml_Object_obj_emptyborder_s_Step_0"; // slighty harder now WORKS 4/12
            // Emptyboarer is a MUST test.  It has a && in it as well as simple if statments and calls.  If we can't pass this nothing else will work
            //    string filename_to_test = "SCR_DIRECT"; // simple loop works! WORKS 4/12
            // case statement woo! way to long, WORKS 4/14 my god, if this one works, they eveything works!  I hope
            // string filename_to_test = "gml_Script_SCR_TEXT";


            //     string filename_to_test = "gml_Object_obj_battlebomb_Alarm_3"; // hard, has pushenv with a break WORKS 4/14

            filename_to_test = "gml_Object_OBJ_WRITERCREATOR_Create_0";
        }

        static void BadExit(int i)
        {
            Instructions();
            Environment.Exit(i);
        }
        /// <summary>
        /// The main entry point for the application.
        /// </summary>

        static ILBlock DecompileBlock(GMContext context, Stream code, string filename, string header = null)
        {
            var instructionsNew = betteribttest.Dissasembler.Instruction.Dissasemble(code, context);
            if (context.doAsm)
            {
                string asm_filename = filename + ".asm";
                betteribttest.Dissasembler.InstructionHelper.DebugSaveList(instructionsNew.Values, asm_filename);
            }
            string raw_filename = Path.GetFileName(filename);
            ILBlock block = new betteribttest.Dissasembler.ILAstBuilder().Build(instructionsNew, false, context);
            FunctionFix.FixCalls(block);
            PushFix.FixCalls(block);

            if (context.doLua)
            {
                filename += ".lua";
                block.DebugSaveLua(filename, header);
            }
            else
            {
                filename += ".cpp";
                block.DebugSave(filename, header);
            }

            // Console.WriteLine("Writing: "+ filename);
            return block;
        }

        static string DecompileBlockLua(GMContext context, Stream code, string filename, bool debugSave = true)
        {
            var instructionsNew = betteribttest.Dissasembler.Instruction.Dissasemble(code, context);
            if (context.doAsm)
            {
                string asm_filename = filename + ".asm";
                betteribttest.Dissasembler.InstructionHelper.DebugSaveList(instructionsNew.Values, asm_filename);
            }
            string raw_filename = Path.GetFileName(filename);
            ILBlock block = new betteribttest.Dissasembler.ILAstBuilder().Build(instructionsNew, false, context);
            FunctionFix.FixCalls(block);
            PushFix.FixCalls(block);
            filename += ".lua";
            block.DebugSaveLua(filename, "-- LuaFile : " + filename);
            string ret;
            using (StringWriter sw = new StringWriter())
            {
                PlainTextOutput to = new PlainTextOutput(sw);
                block.Body.WriteLuaNodes(to, false);
                StringBuilder sb = new StringBuilder();
                sb.Append(sw.ToString());
                // fix some bugs
                sb.Replace(" && ", " and ");
                sb.Replace(" || ", " or ");
                sb.Replace("stack.self", "self");
                sb.Replace("!=", "~=");
                sb.Replace("\r\n\r\n", "\r\n");
                ret = sb.ToString();
            }
            if (debugSave)
            {
                using (StreamWriter sw = new StreamWriter(filename))
                {
                    sw.WriteLine("-- LuaFile : " + Path.GetFileName(filename));
                    sw.WriteLine(ret);
                }
            }

            return ret;
        }
        static void DoFuncList(ITextOutput sb, string tableName, string partname, Dictionary<int, string> codes, bool keypresses = false)
        {
            if (codes.Count > 0)
            {
                sb.Write("-- Start "); sb.Write(partname); sb.WriteLine(" --");

                sb.Write(tableName);
                sb.WriteLine(" = {}");
                foreach (var a in codes)
                {
                    sb.Write(tableName);
                    sb.Write("[");
                    if (keypresses)
                    {
                        sb.Write("\"");
                        char c = (char) a.Key;
                        if (char.IsControl(c))
                        {
                            sb.Write("\\");
                            sb.Write(a.Key.ToString());
                        }
                        else sb.Write(c);
                        sb.Write("\"");
                    }
                    else sb.Write(a.Key.ToString());
                    sb.WriteLine("] = function()");
                    sb.Indent();
                    sb.Write(a.Value);
                    sb.Unindent();
                    sb.WriteLine("end");
                }
                sb.Write("-- End "); sb.Write(partname); sb.WriteLine(" --");
            }
            else sb.WriteLine("-- No " + partname);
        }
        static void MakeLuaObject(GMContext context, GMK_Object gobj, ChunkReader.CodeData[] codeData, string path)
        {
            string createObject = null;
            string drawObject = null;
            Dictionary<int, string> alarms = new Dictionary<int, string>();
            Dictionary<int, string> steps = new Dictionary<int, string>();
            Dictionary<int, string> keypresses = new Dictionary<int, string>();
            Dictionary<int, string> others = new Dictionary<int, string>();
            List<Thread> threads = new List<Thread>();
            List<string> errors = new List<string>();
            int number = -1;
            ChunkReader.CodeData o = new ChunkReader.CodeData(); // to get rid of compiler message
            Func<string, bool> ObjectPart = (string tosearch) =>
            {
                int index = o.Name.IndexOf(tosearch);
                if (index != -1)
                {
                    index += tosearch.Length + 1;
                    string sub = o.Name.Substring(index);
                    number = int.Parse(sub);
                    return true;
                }
                return false;
            };
            for (int i = 0; i < codeData.Length; i++) {

                o = codeData[i];
                ChunkReader.CodeData data = (ChunkReader.CodeData) o;
                string filename = path + data.Name;
                try
                {
                    string code = DecompileBlockLua(context, data.stream.BaseStream, filename, false);

                    if (ObjectPart("Create"))   // check what part of an object it is
                    { // create code
                        Debug.Assert(createObject == null); // We should only have one of these
                        createObject = code.ToString();
                    }
                    else if (ObjectPart("Draw"))   // check what part of an object it is
                    { // create code
                        Debug.Assert(drawObject == null); // We should only have one of these
                        drawObject = code.ToString();
                    }
                    if (ObjectPart("Alarm"))
                    {
                        Debug.Assert(alarms[number] == null); // We should only have one of these
                        alarms.Add(number, code.ToString());
                    } else if (ObjectPart("KeyPress"))
                    {
                        keypresses.Add(number, code.ToString());

                    } else if (ObjectPart("Step"))
                    {
                        Debug.Assert(steps[number] == null); // We should only have one of these
                        steps.Add(number, code.ToString());
                    }
                    else if (ObjectPart("Other"))
                    {
                        others.Add(number, code.ToString());
                    } else
                    {
                        Debug.Assert(false);
                    }
                }
                catch (Exception e)
                {
                    string error = "-- " + string.Format("Object: {0}  Error: {1}", data.Name, e.Message);
                    errors.Add(error);
                }
            }
            using (PlainTextOutput sw = new PlainTextOutput(new StreamWriter(path + gobj.Name + ".lua")))
            {
                sw.WriteLine("-- Object:     " + gobj.Name);
                sw.WriteLine("-- Parent:     " + gobj.ParentName);
                sw.WriteLine("-- Persistent: " + gobj.Persistent);
                sw.WriteLine("-- Depth:      " + gobj.Depth);
                sw.WriteLine("-- Solid:      " + gobj.Solid);
                sw.WriteLine("-- Visiable:   " + gobj.Visible);

                sw.WriteLine(); // simple header
                sw.Write(gobj.Name); sw.WriteLine(" = function(self)");
                sw.Indent();
                if (gobj.Parent > -1)
                {
                    sw.Write("self.parent="); sw.Write("\""); sw.Write(gobj.ParentName); sw.Write("\""); sw.WriteLine();
                }

                sw.Write("self.depth="); sw.Write(gobj.Depth.ToString()); sw.WriteLine();
                sw.Write("self.solid="); sw.Write(gobj.Solid ? "true" : "false"); sw.WriteLine();
                sw.Write("self.visiable="); sw.Write(gobj.Visible ? "true" : "false"); sw.WriteLine();
                sw.Write("self.persistent="); sw.Write(gobj.Persistent ? "true" : "false"); sw.WriteLine();
                sw.Write("self.sprite_index="); sw.Write(gobj.SpriteIndex.ToString()); sw.WriteLine();
                sw.WriteLine("-- Start Create Code --");
                if (createObject != null) sw.Write(createObject);
                if (drawObject != null)
                {
                    sw.WriteLine("self.drawfunc = function()");
                    sw.Indent();
                    sw.Write(drawObject);
                    sw.Unindent();
                    sw.WriteLine("end");
                }
                sw.WriteLine("-- End Create Code --");
                DoFuncList(sw, "stepfunc", "Step Code", steps);
                DoFuncList(sw, "alarmfunc", "Alarm Code", alarms);
                DoFuncList(sw, "otherfunc", "Other Code", others);
                DoFuncList(sw, "keyfunc", "KeyPress Code", keypresses, true);
                if (errors.Count > 0)
                {
                    sw.WriteLine("-- Errors:");
                    foreach (var e in errors) sw.WriteLine(e);
                }
                sw.Unindent();
                sw.WriteLine("end");
            }
        }
        static void InsertIntoTable(ITextOutput output, string table, int index, string func)
        {
            output.WriteLine("self.{0}[{1}] = {2}", table, index, func);
        }
        static void InsertIntoTable(ITextOutput output, string table, string index, string func)
        {
            output.WriteLine("self.{0}[\"{1}\"] = {2}", table, index, func);
        }
        static void InsertIntoTable(ITextOutput output, string table, List<KeyValuePair<int, string>> actions) {
            output.WriteLine("self.{0} = {{}}", table);
            foreach (var func in actions) InsertIntoTable(output,table, func.Key, func.Value);
        }
        static void objectHeadder(ITextOutput output, GMK_Object obj)
        {
            // output.WriteLine("if !_objects then _objects = {} end");
            //   output.WriteLine("if !_instances then _instances = {} end");
            output.WriteLine("local G = this");
            output.WriteLine("function new_{0}(self)", obj.Name);
            output.Indent();
            output.WriteLine("table.insert(_instances,obj)");
            output.WriteLine("function event_user(v) self.UserEvent[v]() end");
            output.WriteLine("function create_instance(x,y,name)");
            output.Indent();
            output.WriteLine("local func = _objects[name]");
            output.WriteLine("if func then ");
            output.Indent();
            output.WriteLine("local obj = func(__NEWGAMEOBJECT())");
            output.WriteLine("return obj");
            output.Unindent();
            output.WriteLine("end");
            output.Unindent();
            output.WriteLine("end");
            output.WriteLine("function destroy_instance() G.destroy_instance(self) end");
            output.WriteLine("self.destroy_instance =  destroy_instance -- for with statements");
            output.WriteLine();

        }
        static void objectFooter(ITextOutput output, GMK_Object obj)
        {
            output.Unindent();
            output.WriteLine("if  self.CreateEvent then  self.CreateEvent() end");
            output.WriteLine("end");
            output.WriteLine();
            output.WriteLine("_objects[\"{0}\"] = new_{0}", obj.Name);
            output.WriteLine("_objects[{1}] = new_{0}", obj.Name,obj.ObjectIndex); // put it in both to make sure we can look it up by both
        }
        public static void MakeObject(GMContext context, ChunkReader cr, GMK_Object obj)
        {
            HashSet<string> SawEvent = new HashSet<string>();
            using (StreamWriter sw = new StreamWriter(obj.Name + ".lua"))
            {
                PlainTextOutput ptext = new PlainTextOutput(sw);
                objectHeadder(ptext, obj);
                obj.DebugLuaObject(ptext,false);
                ptext.WriteLine("self.events = {}");
                for (int i = 0; i < obj.Events.Length; i++)
                {
                    if (obj.Events[i] == null) continue;
                    List<KeyValuePair<int, string>> codeFunctions = new List<KeyValuePair<int, string>>();
                    foreach (var e in obj.Events[i])
                    {

                        foreach (var a in e.Actions)
                        {
                            var codeData = cr.GetCodeStreamAtIndex(a.CodeOffset);
                            string code = DecompileBlockLua(context, codeData.stream.BaseStream, codeData.Name, false);
                            StringBuilder sb = new StringBuilder(code);
                            code = sb.ToString();
                            ptext.WriteLine("function {0}()", codeData.Name);
                            ptext.Indent();
                            // dosn't handle line endings well
                            ptext.Write(code);
                            ptext.Unindent();
                            ptext.WriteLine();
                            ptext.WriteLine("end");
                            codeFunctions.Add(new KeyValuePair<int, string>(e.SubType, codeData.Name));
                        }
                    }
                    switch (i)
                    {
                        case 0:
                            Debug.Assert(codeFunctions.Count == 1);
                            ptext.WriteLine("self.CreateEvent = {0}", codeFunctions[0].Value);
                            break;
                        case 1:
                            Debug.Assert(codeFunctions.Count == 1);
                            ptext.WriteLine("self.DestroyEvent = {0}", codeFunctions[0].Value);
                            break;
                        case 2:
                            InsertIntoTable(ptext, "AlarmEvent", codeFunctions);
                            break;
                        case 3:
                            foreach (var e in codeFunctions)
                            {
                                switch (e.Key)
                                {
                                    case 0: ptext.WriteLine("self.StepNormalEvent = {0}", e.Value); break;
                                    case 1: ptext.WriteLine("self.StepBeginEvent = {0}", e.Value); break;
                                    case 2: ptext.WriteLine("self.StepEndEvent = {0}", e.Value); break;
                                }
                            }
                            break;
                        case 4:
                            InsertIntoTable(ptext, "CollisionEvent", codeFunctions);
                            break;
                        case 5:
                            InsertIntoTable(ptext, "Keyboard", codeFunctions);
                            break;
                        case 6: // joystick and mouse stuff here, not used much in undertale
                            ptext.WriteLine("self.{0} = {{}}", "ControlerEvents");
                            foreach(var e in codeFunctions)
                                InsertIntoTable(ptext, "ControlerEvents", GMContext.EventToString(i,e.Key), e.Value);
                            break;
                        case 7: // we only really care about user events
                            ptext.WriteLine("self.{0} = {{}}", "UserEvent");

                            foreach (var e in codeFunctions)
                            {
                                Debug.Assert(e.Key > 9 && e.Key < 26);
                                InsertIntoTable(ptext, "UserEvent", e.Key-10, e.Value);
                            }
                            break;
                        case 8:
                            // special case, alot of diffrent draw events are here but undertale mainly just uses
                            // one, so we will figure out if we need a table or not
                            if(codeFunctions.Count == 1) ptext.WriteLine("self.DrawEvent = {0}", codeFunctions[0].Value);
                            else
                            {
                                ptext.WriteLine("self.{0} = {{}}", "DrawEvents");
                                foreach (var e in codeFunctions)
                                    InsertIntoTable(ptext, "DrawEvents", GMContext.EventToString(i, e.Key), e.Value);
                            }
                            break;
                        case 9:
                            InsertIntoTable(ptext, "KeyPressed", codeFunctions);
                            break;
                        case 10:
                            InsertIntoTable(ptext, "KeyReleased", codeFunctions);
                            break;
                        case 11:
                            InsertIntoTable(ptext, "Trigger", codeFunctions);
                            break;
                    }
                }
                objectFooter(ptext, obj);
            }
        }

        static void MakeAllLuaObjects(ChunkReader cr, GMContext context)
        {
            List<Task> tasks = new List<Task>();
            foreach (var a in cr.GetAllObjectCode())
            {
                
               // var info = Directory.CreateDirectory(a.ObjectName);
                ChunkReader.CodeData[] files = a.Streams.ToArray();
                MakeLuaObject(context, a.Obj, files, "");
                //  Thread t = new System.Threading.Thread(() => MakeLuaObject(context, a.Obj, files, info.FullName));
                //  Task t = Task.Factory.StartNew(() => MakeLuaObject(context, a.Obj, files, info.FullName));
                //  tasks.Add(t);
                //   tasks.Add(t);
                //    t.IsBackground = true;
                //    threads.Add(t);
                //     t.Start();
            }
           // Task.WaitAll(tasks.ToArray());

         //   threads
        }

        static void Main(string[] args)
        {
            ChunkReader cr=null;
            string dataWinFileName = args.ElementAtOrDefault(0);
            if (string.IsNullOrWhiteSpace(dataWinFileName))
            {
                Console.WriteLine("Missing data.win file");
                BadExit(1);
            }
#if !DEBUG
            try
            {
#endif
                cr = new ChunkReader(dataWinFileName, false); // main pc
#if !DEBUG
        }
            catch (Exception e)
            {
                Console.WriteLine("Could not open data.win file '" + dataWinFileName + "'");
                Console.WriteLine("Exception: " + e.Message);
                BadExit(1);
            }
#endif
            FunctionReplacement();
            context = new GMContext(cr);
 
            bool all = false;
            string toSearch = null;
           int pos = 1;
            while(pos < args.Length)
            {
                switch (args[pos])
                {
                    case "-s":
                        pos++;
                        toSearch = args.ElementAtOrDefault(pos);
                        pos++;
                        break;
                    case "-o":
                        pos++;
                        toSearch = args.ElementAtOrDefault(pos);
                        context.makeObject = true;
                        context.doLua = true;
                        // DebugLuaObject
                        {
                            GMK_Data data;
                            if(!cr.nameMap.TryGetValue(toSearch,out data))
                            {
                                Console.WriteLine("Could not find {0}", toSearch);
                                Environment.Exit(1);
                            }
                            GMK_Object obj = data as GMK_Object;
                            if(obj == null)
                            {
                                Console.WriteLine("{0} is not an object", toSearch);
                                Environment.Exit(1);
                            }
                            MakeObject(context, cr, obj);
                            Environment.Exit(0);
                        }
                        pos++;
                        break;
                    case "-debug":
                        pos++;
                        context.Debug = true;
                        break;
                    case "-all":
                        all = true;
                        pos++;
                        toSearch = args.ElementAtOrDefault(pos);
                        pos++;
                        break;
                    case "-lua":
                        context.doLua = true;
                        pos++;
                        break;
                    case "-luaobj":
                        pos++;
                        context.doLua = true;
                        context.doLuaObject = true;
                        MakeAllLuaObjects(cr, context);
                        return ;
                        
                        break;
                    case "-asm":
                        context.doAsm = true;
                        pos++;
                        break;
                    default:
                        Console.WriteLine("Bad argument " + args[pos]);
                        BadExit(1);
                        break;
                }
                if (toSearch != null) break;
            }
            if (toSearch == null)
            {
                Console.WriteLine("Missing search field");
                BadExit(1);
            }
            List<string> FilesFound = new List<string>();
            var errorinfo = Directory.CreateDirectory("error");
            StreamWriter errorWriter = null;
            Action<string> WriteErrorLine = (string msg) =>
            {
                if (errorWriter == null) errorWriter = new StreamWriter("error_" + toSearch + ".txt");
                StringBuilder sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("MM-dd-yy HH:mm:ss.ffff"));
                sb.Append(": ");
                sb.Append(msg);
                lock (errorWriter) errorWriter.WriteLine(sb.ToString());
                lock (Console.Out) Console.WriteLine(sb.ToString());
                //lock (Debug.) Debug.WriteLine(sb.ToString());
            };
            if (all)
            {
                switch (toSearch)
                {
                    case "objects":
                        {
                            foreach (var a in cr.GetAllObjectCode())
                            {
                                var info = Directory.CreateDirectory(a.ObjectName);
#if DEBUG
                                foreach (ChunkReader.CodeData files in a.Streams)
                                {
                                    FilesFound.Add(files.Name);
                                    new System.Threading.Thread((object o) =>
                                    {
                                        Thread.CurrentThread.IsBackground = true;
                                        ChunkReader.CodeData data = (ChunkReader.CodeData)o;
                                        string filename = Path.Combine(info.FullName, data.Name);
                                        try
                                        {
                                            DecompileBlock(context, data.stream.BaseStream, filename, "ScriptName: " + data.Name);
                                        }
                                        catch (Exception e)
                                        {
                                            WriteErrorLine(string.Format("Object: {0}  Error: {1}", data.Name, e.Message));
                                        }
                                    }).Start(files);
                                }
#else
                                foreach (var files in a.Streams)
                                {
                                    string filename = Path.Combine(info.FullName, files.ScriptName);
                                    try
                                    {
                                        DecompileBlock(context, files.stream.BaseStream, filename, "ScriptName: " + files.ScriptName);
                                        FilesFound.Add(files.ScriptName);
                                    }
                                    catch (Exception e)
                                    { 
                                        WriteErrorLine(string.Format("Object: {0}  Error: {1}", files.ScriptName, e.Message));
                                    }
                                }
#endif
                            }
                        }
                        break;
                    case "scripts":
                        {
                            var info = Directory.CreateDirectory("scripts");
                            foreach (var files in cr.GetAllScripts())
                            {

                                string filename = Path.Combine(info.FullName, files.Name);
#if !DEBUG
                                try
                                {
#endif
                                    DecompileBlock(context, files.stream.BaseStream, filename, "ScriptName: " + files.Name);
                                    FilesFound.Add(files.Name);
#if !DEBUG
                                }
                                catch (Exception e)
                                {
                                    WriteErrorLine(string.Format("Script: {0}  Error: {1}", files.ScriptName, e.Message));
                                }
#endif
                            }
                        }
                        break;
                    default:
                        Console.WriteLine("Unkonwn -all specifiyer");
                        BadExit(1);
                        break;
                }
            } else
            {
                
                foreach (var files in cr.GetCodeStreams(toSearch))
                {
               
                    //  Instruction.Instructions instructions = null;// Instruction.Create(files.stream, stringList, InstanceList);
                    string filename = files.Name;
                    if(context.doLuaObject)
                    {
                        
                    } else
                    {
                        try
                        {
                            DecompileBlock(context, files.stream.BaseStream, filename, "ScriptName: " + files.Name);
                            FilesFound.Add(files.Name);

                        }
                        catch (Exception e)
                        {
                            WriteErrorLine(string.Format("Script: {0}  Error: {1}", files.Name, e.Message));
                        }
                    }
                   
                }
            }

            if(FilesFound.Count==0)
            {
                Console.WriteLine("No scripts or objects found with '" + toSearch + "' in the name");
            } 
            System.Diagnostics.Debug.WriteLine("Done");
        }
    }
}
