﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Reflection;
using System.Collections.Specialized;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Drawing;

namespace GameMaker
{

    class SimplerLuaFormatter : IFormatter
    {
        SerializationBinder binder;
        StreamingContext context;
        ISurrogateSelector surrogateSelector;

        public SimplerLuaFormatter()
        {
            context = new StreamingContext(StreamingContextStates.File);
        }

        public object Deserialize(System.IO.Stream serializationStream)
        {
           return null;
        }
        bool isSimpleObject(object o)
        {
            Type t = o.GetType();
            if (o is string || t.IsPrimitive) return true;
            if (o is System.Collections.IEnumerable) return false;
            if (t == typeof(KeyValuePair<string, object>) && isSimpleObject(((KeyValuePair<string, object>)o).Value)) return true;
            return false;
        }
        void SaveSimpleObject(int ident, StringBuilder sb, object o)
        {
            Type t = o.GetType();
            if (t == typeof(string))
            {
                sb.Append('"');
                foreach (var c in (o as string)) sb.Append(JISONEscapeString(c));
                sb.Append('"');
            }
            else if (t == typeof(bool)) sb.Append((bool)o ? "true" : "false");
            else if (t.IsPrimitive) sb.Append(o.ToString());
            else if(t == typeof(KeyValuePair<string, object>))
            {
                KeyValuePair<string, object> kv = (KeyValuePair<string, object>)o;
                sb.Append(kv.Key);
                sb.Append(" = ");
                if(!isSimpleObject(kv.Value)) throw new Exception("Not a simple object");
                SaveSimpleObject(ident, sb, kv.Value);
            }
            else throw new Exception("Not a simple object");
        }
        void SaveValue(int ident, StringBuilder sb, object o)
        {
            Type t = o.GetType();
            if (o == null) sb.Append(NullString);
            else if (t.IsPrimitive || o is string) SaveSimpleObject(ident, sb, o);
            else if (t == typeof(KeyValuePair<string, object>))
            {
                KeyValuePair<string, object> kv = (KeyValuePair<string, object>)o;
                sb.Append(kv.Key);
                sb.Append(" = ");
                SaveValue(ident, sb, kv.Value);
            }
            else
            {
                sb.Append('{');
                bool need_comma = false;
                ident++;
                System.Collections.IEnumerable ie = o as System.Collections.IEnumerable;
                if (ie != null)
                {
                    foreach (var io in ie)
                    {
                        if (need_comma) sb.Append(','); else need_comma = true;
                        sb.Append(' ');
                        object ioo;
                        if (io is KeyValuePair<string, object>)
                        {
                            KeyValuePair<string, object> kv = (KeyValuePair<string, object>)io;
                            sb.Append(kv.Key);
                            sb.Append(" = ");
                            ioo = kv.Value;
                        }
                        else ioo = io;
                        SaveValue(ident, sb, ioo);
                        if (!isSimpleObject(ioo)) sb.AppendLineAndIdent(ident);
                    }
                }
                else
                {
                    MemberInfo[] members = FormatterServices.GetSerializableMembers(o.GetType(), Context);
                    object[] objs = FormatterServices.GetObjectData(o, members);
                    Dictionary<string, object> dic = new Dictionary<string, object>();
                    for (int i = 0; i < objs.Length; ++i)
                    {
                        string name = members[i].Name;
                        var match = k__BackingFieldRemoveRegex.Match(name);
                        if (match != null && match.Success) name = match.Groups[1].Value; // hack, but it works, eh
                        dic.Add(name, objs[i]);
                    }
                    SaveValue(ident, sb, (IEnumerable<KeyValuePair<string, object>>)dic);
                }
                ident--;
                sb.Append('}');
            }
        }
        public string NullString = "nil";
        static Regex k__BackingFieldRemoveRegex = new Regex(@"<([\w\d]+)>k__BackingField", RegexOptions.Compiled);
   
  
        
   
        public static string JISONEscapeString(string s)
        {
            StringBuilder sb = new StringBuilder(50);
            sb.Append('"');
            foreach (var c in s) sb.Append(JISONEscapeString(c));
            sb.Append('"');
            return sb.ToString();
        }
        public static string JISONEscapeString(char v)
        {
            switch (v)
            {
                case '\a': return "\\a";
                case '\n': return "\\n";
                case '\r': return "\\r";
                case '\t': return "\\t";
                case '\v': return "\\v";
                case '\\': return "\\\\";
                case '\"': return "\\\"";
                case '\'': return "\\\'";
                //  case '[': return "\\[";
                //   case ']': return "\\]";
                default:
                    if (char.IsControl(v)) return string.Format("\\{0}", (byte)v);
                    else return v.ToString();
            }
        }
        public void Serialize(System.IO.Stream serializationStream, object graph)
        {
            StringBuilder ssw = new StringBuilder(10000);
            SaveValue(0, ssw, graph);
            byte[] bytes = Encoding.ASCII.GetBytes(ssw.ToString());
            serializationStream.Flush();
            serializationStream.Write(bytes, 0, bytes.Length);
            serializationStream.Flush();
        }


        public ISurrogateSelector SurrogateSelector
        {
            get { return surrogateSelector; }
            set { surrogateSelector = value; }
        }
        public SerializationBinder Binder
        {
            get { return binder; }
            set { binder = value; }
        }
        public StreamingContext Context
        {
            get { return context; }
            set { context = value; }
        }
    }

    public static class BinaryReaderExtensions
    {
        static File.Chunk FindChunk(int pos)
        {
            foreach (var c in File.fileChunks)
            {
                if (c.Key == "FORM") continue;
                if ((pos > c.Value.start && pos < c.Value.end) || pos == c.Value.start || pos == c.Value.end) return c.Value;
            }
            return null;
        }
        static string FindChunkString(int pos)
        {
            var chunk = FindChunk(pos);
            if (chunk != null) return chunk.ToString();
            return null;
        }
       
        public static void WriteStuff(this BinaryReader r, int count, string name = null)
        {
            if (name != null) Debug.WriteLine("Stuff: " + name);
            var curChunk = FindChunk((int)r.BaseStream.Position);
            Debug.WriteLine("Starting In Chunk: " + curChunk.ToString());
            var pos = r.BaseStream.Position;
            for (int i = 0; i < count; i++)
            {
                int p = (int)r.BaseStream.Position;
                File.Chunk nowChunk = null;
                while ((nowChunk = FindChunk(p)) == null) p++;
                if (nowChunk.start != curChunk.start)
                {
                    Debug.WriteLine("Went to Next Chunk: " + nowChunk.ToString());
                    curChunk = nowChunk;
                }
                int v = r.ReadInt32();
                string c = FindChunkString(v);
                Debug.WriteLine("{0} : {1} | {2}", p.DebugHex(), v.DebugHex(), c);
            }
            r.BaseStream.Position = pos;
        }
        public static T[] ReadArray<T>(this BinaryReader r, int offset, int count) where T : struct
        {
            var pos = r.BaseStream.Position;
            r.BaseStream.Position = offset;
            T[] ret = r.ReadArray<T>(count);
            r.BaseStream.Position = pos;
            return ret;
        }
        public static int ReadBigInt32(this BinaryReader r)
        {
            byte[] data = new byte[4];
            r.Read(data, 0, 4);
            return (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
        }

        public static T[] ReadArray<T>(this BinaryReader r, int count) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] bytes = r.ReadBytes(count * size);
            T[] array = new T[count];
            Buffer.BlockCopy(bytes, 0, array, 0, array.Length * size);
            return array;
        }
        public static uint[] ReadUInt32(this BinaryReader r, int offset, int count)
        {
            return r.ReadArray<uint>(offset, count);
        }
        public static uint[] ReadUInt32(this BinaryReader r, int count)
        {
            return r.ReadArray<uint>(count);
        }
        public static int[] ReadInt32(this BinaryReader r, int offset, int count)
        {
            return r.ReadArray<int>(offset, count);
        }
        public static int[] ReadInt32(this BinaryReader r, int count)
        {
            return r.ReadArray<int>(count);
        }
        public static short[] ReadInt16(this BinaryReader r, int offset, int count)
        {
            return r.ReadArray<short>(offset, count);
        }
        public static short[] ReadInt16(this BinaryReader r, int count)
        {
            return r.ReadArray<short>(count);
        }
        /// <summary>
        /// Reads a Bool as an entire int and chceks if it is 1 or 0
        /// </summary>
        /// <returns></returns>
        public static bool ReadIntBool(this BinaryReader r)
        {
            int b = r.ReadInt32();
            if (b != 1 && b != 0) throw new Exception("Expected bool to be 0 or 1");
            return b != 0;
        }
        /// <summary>
        /// Reads a string of a fixed lenght at position
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        public static string ReadFixedString(this BinaryReader r, int count)
        {
            byte[] bytes = r.ReadBytes(count);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        // reads a zero terminated string from offset
        public static string ReadStringFromNextOffset(this BinaryReader r)
        {
            int offset = r.ReadInt32();
            return r.ReadStringFromOffset(offset);
        }
        public static string ReadStringFromOffset(this BinaryReader r, int offset)
        {
            var pos = r.BaseStream.Position;
            r.BaseStream.Position = offset;
            string s = r.ReadZeroTerminatedString();
            r.BaseStream.Position = pos;
            return s;
        }
        const int MaxZeroTerminatedStringSize = 64;
        /// <summary>
        /// Tries to read a string, assumes strings arn't bigger than 64 charaters
        /// </summary>
        /// <param name="r"></param>
        /// <returns></returns>
        public static string ReadZeroTerminatedString(this BinaryReader r)
        {
            var pos = r.BaseStream.Position;
            byte[] block = r.ReadBytes(MaxZeroTerminatedStringSize);
            for (int i = 0; i < block.Length; i++)
            {
                byte b = block[i];
                if (b == 0)
                {
                    r.BaseStream.Position = pos = i + 1; // rewind, but skip the 0
                    return System.Text.Encoding.UTF8.GetString(block, 0, i);
                }
            }
            throw new Exception("String not zero terminated");
        }
        public struct Entry
        {
            public readonly int Index;
            public readonly int Position;
            public readonly int Next; // Can be used to caculate the size
            public Entry(int Index, int Position, int Next) { this.Index = Index; this.Position = Position; this.Next = Next; }
        }
        public static Entry[] ReadChunkEntries(this BinaryReader r)
        {
            int count = r.ReadInt32();
            if (count == -1) return null;
            else if (count == 0) return new Entry[0];
            else
            {
              
                Entry[] entries = new Entry[count];
                int[] ientries = r.ReadInt32(count);
                for (int i = 0; i < count; i++)
                    entries[i] = new Entry(i, ientries[i], i + 1 < count ? ientries[i] : -1);
                return entries;
            }
        }
        public static Entry[] ReadChunkEntries(this BinaryReader r, int offset)
        {
            var pos = r.BaseStream.Position;
            r.BaseStream.Position = offset;
            var ret = r.ReadChunkEntries();
            r.BaseStream.Position = pos;
            return ret;
        }
        public static IEnumerable<Entry> ForEachEntry(this BinaryReader r, Entry[] entries)
        {
            foreach (var o in entries)
            {
                r.BaseStream.Position = o.Position;
                yield return o;
            }
        }
        public static IEnumerable<Entry> ForEachEntry(this BinaryReader r, int offset)
        {
            var pos = r.BaseStream.Position;
            r.BaseStream.Position = offset;
            var entries = r.ReadChunkEntries();
            foreach (var o in entries)
            {
                r.BaseStream.Position = o.Position;
                yield return o;
            }
            r.BaseStream.Position = pos;
        }
        /// <summary>
        /// special case, we read the table, but save the position AFTER the table
        /// </summary>
        /// <param name="r"></param>
        /// <returns></returns>
        public static IEnumerable<Entry> ForEachEntry(this BinaryReader r)
        {
            var entries = r.ReadChunkEntries();
            var pos = r.BaseStream.Position;
            foreach (var o in entries)
            {
                r.BaseStream.Position = o.Position;
                yield return o;
            }
            r.BaseStream.Position = pos;
        }

    }
    public  static partial class File
    {
      
       internal class Chunk
        {
            public readonly int start;
            public readonly int end;
            public readonly int size;
            public readonly string name;
            public Chunk(string name, int start, int size) { this.name = name; this.start = start; this.end = start + size; this.size = size; }
            public override string ToString()
            {
                return string.Format("( Name={0} Start={1} End={2} Size={3} )", name, start, end, size);
            }
        }
        internal static Dictionary<string, Chunk> fileChunks = null;
        static byte[] rawData = null;
        static string filename = null;

        static Dictionary<int, string> stringCache = null;
        static List<string> stringList = null;

        static List<SpriteFrame> spriteframes = null;
        static List<Texture> textures = null;
        static List<Sprite> sprites = null;
        static List<GObject> objects = null;
        static List<Room> rooms = null;
        static List<Background> backgrounds = null;
        static List<AudioFile> sounds = null;
        static List<RawAudio> rawAudio = null;
        static List<Font> fonts = null;
        static List<Code> codes = null;
        static List<Script> scripts = null;
        static Dictionary<string, GameMakerStructure> namedResourceLookup = new Dictionary<string, GameMakerStructure>();
        static void InternalLoad()
        {
            if (filename == null) throw new FileNotFoundException("No file name defined");
            if (rawData != null) return; // already loaded
            // Clear all the tables in case they arn't already
            stringList = null;
            textures = null;
            sprites = null;
            objects = null;
            rooms = null;
            backgrounds = null;
            sounds = null;
            rawAudio = null;
            fonts = null;
            codes = null;
            scripts = null;
            spriteframes = null;


            using (FileStream sr = System.IO.File.Open(filename, FileMode.Open, FileAccess.Read))
            {
                rawData = new byte[sr.Length];
                sr.Read(rawData, 0, rawData.Length);
            }
            fileChunks = new Dictionary<string, Chunk>();
            MemoryStream ms = new MemoryStream(rawData);
            int full_size = rawData.Length;
            Chunk chunk = null;
            using (BinaryReader r = new BinaryReader(ms))
            {
                while (r.BaseStream.Position < full_size)
                {
                    string chunkName = r.ReadFixedString(4);
                    int chunkSize = r.ReadInt32();
                    int chuckStart = (int) r.BaseStream.Position;
                    chunk = new Chunk(chunkName, chuckStart, chunkSize);
                    fileChunks[chunkName] = chunk;
                    if (chunkName == "FORM") full_size = chunkSize; // special case for form
                    else r.BaseStream.Position = chuckStart + chunkSize; // make sure we are always starting at the next chunk               
                }
            }
        }
        public enum VarType
        {
            BuiltIn = 0,
            Local = 1,
            Global = 2,
            LocalOrGlobal = 3
        }
        static Dictionary<string, VarType> newvarTypeLookup = new Dictionary<string, VarType>();
        public static IReadOnlyDictionary<string,VarType> NewVarTypeLookup {  get { return newvarTypeLookup; } }
        public static IEnumerable<INamedResrouce> Search(string name)
        {
            return namedResourceLookup.Where(x => x.Key.Contains(name)).Select(x => (INamedResrouce) x.Value);
        }

        public static bool TryLookup<T>(string name, out T ret) where T : GameMakerStructure
        {
            GameMakerStructure data;
            if (namedResourceLookup.TryGetValue(name, out data))
            {
                T t = data as T;
                if (t != null)
                {
                    ret = t;
                    return true;
                }
            }
            ret = default(T);
            return false;
        }
        static void CheckStrings()
        {
            if (stringList == null)
            {
                stringList = new List<string>();
                stringCache = new Dictionary<int, string>();
                Chunk chunk = fileChunks["STRG"];
                using (BinaryReader r = new BinaryReader(new MemoryStream(rawData)))
                {
                    foreach (var e in r.ForEachEntry(chunk.start))
                    {
                        int string_size = r.ReadInt32(); //size 
                        byte[] bstr = r.ReadBytes(string_size);
                        string str = System.Text.Encoding.UTF8.GetString(bstr, 0, string_size);
                        stringList.Add(str);
                        stringCache[e.Position + 4] = str;
                    }
                }
            }
        }
        // Ok, this is stupid, but I get it
        // All the vars and function names are on a link list from one to another that tie to the name
        // to make this much easyer for the disassembler, we are going to change all these refrences
        // to be string indexes to strlist.  To DO this however, requires us to read the entire code section
        // as a bunch of ints, go though the etnire thing with the refrences, repeat then figure out the code section
        // into seperate scripts blocks.  Meh
        // Also a side note is that object refrences as well as instance creation MUST fit in a 16bit value
        // If thats the case are objects manged by an index or am I reading objects wrong?
        class RefactorCodeManager
        {
            BinaryReader r;
            Dictionary<string, CodeNameRefrence> refs = new Dictionary<string, CodeNameRefrence>();
            Dictionary<int, CodeNameRefrence> offsetLookup = new Dictionary<int, CodeNameRefrence>();
            Dictionary<string, int> stringToIndex = new Dictionary<string, int>();
            public RefactorCodeManager(byte[] rawData)
            {
                r = new BinaryReader(new MemoryStream(rawData, true));
                for (int i = 0; i < File.Strings.Count; i++) stringToIndex[File.Strings[i]] = i;
            }
            static string formatint(int i)
            {
                return string.Format("({0,-8} , {0:X8})",i);
            }
           static  Chunk FindChunk(int pos)
            {
                foreach (var c in fileChunks)
                {
                    if (c.Key == "FORM") continue;
                    if ((pos > c.Value.start && pos < c.Value.end) || pos == c.Value.start || pos == c.Value.end) return c.Value;
                }
                return null;
            }
            static string FindChunkString(int pos)
            {
                var chunk = FindChunk(pos);
                if (chunk != null) return chunk.ToString();
                return null;
            }
            static void WriteStuff(BinaryReader r, int count, string name = null)
            {
                if (name != null) Debug.WriteLine("Stuff: "+name);
                var curChunk = FindChunk((int)r.BaseStream.Position);
                Debug.WriteLine("Starting In Chunk: " + curChunk.ToString());
                var pos = r.BaseStream.Position;
                for (int i = 0; i < count; i++)
                {
                    int p = (int)r.BaseStream.Position;
                    Chunk nowChunk = null;
                    while ((nowChunk = FindChunk(p)) == null) p++;
                    if (nowChunk.start != curChunk.start)
                    {
                        Debug.WriteLine("Went to Next Chunk: " + nowChunk.ToString());
                        curChunk = nowChunk;
                    }
                    int v = r.ReadInt32();
                    string c = FindChunkString(v);
                    Debug.WriteLine("{0} : {1} | {2}", formatint(p), formatint(v), c);
                }
                r.BaseStream.Position = pos;
            }
            void WriteStuff(int count,string name = null)
            {
                WriteStuff(r, count, name);
            }
            // Slightly changed, now the start number is the count
            class NewFunctionIndex
            {
                public string Name;
                public string Value;
                public NewFunctionIndex(BinaryReader r)
                {
                    Debug.Assert(r.ReadIntBool());
                    Name = r.ReadStringFromNextOffset();
                    Debug.Assert(Name != null);
                    Debug.Assert(!r.ReadIntBool());
                    Value = r.ReadStringFromNextOffset();
                    Debug.Assert(Value != null);
                }
                public override string ToString()
                {
                    return "( Name=" + Name + " Value=" + Value + " )";
                }
            }
            List<NewFunctionIndex> newFunctionIndex;
            // Ok, the new FUNC structure is the same as the old, except the opcode it changes is 
            // diffrent AND it has a trailing list of evey function/code name in some wierd
            // structure that dosn't seem to change?( 1, Name, 0, "arguments")  Not sure on its purpose
            void NewFunctionChunk(Chunk func)
            {
                r.BaseStream.Position = func.start;
                WriteStuff(10, "funcStart");
                
                int refCount = r.ReadInt32();
                int calCount = func.size / 12; // the size dosn't match up though?  Why the extra data?
                for (int i = 0; i < refCount; i++)
                {
                    NewFuncNameRefrence nref = new NewFuncNameRefrence(r);
                    refs.Add(nref.Name, nref);
                    foreach (var o in nref.Offsets) offsetLookup.Add(o, nref);
                }
                // humm some wierd mapping?
                // I think its an index to string map of all the code?
                int endCount = r.ReadInt32();
                newFunctionIndex = new List<NewFunctionIndex>();
                for (int i = 0; i < endCount; i++)
                {
                    newFunctionIndex.Add(new NewFunctionIndex(r));
                }
                // goes to the string chunk here so we are finaly at the end
            }
            abstract class  CodeNameRefrence
            {
                public string Name=null;
                public int Count;
                public int Start;
                public int[] Offsets=null;
                protected CodeNameRefrence() { }
                public override string ToString()
                {
                    return "Name =" + Name + " Count=" + Count;
                }
            }
            class NewVarNameRefrence : CodeNameRefrence
            {
                public int Type;
                public int Index;
                public override string ToString()
                {
                    return base.ToString() + " Type=" + Type + " Index=" + Index;
                }
                public NewVarNameRefrence(BinaryReader r)
                {
                    this.Name = r.ReadStringFromNextOffset();
                    Type = r.ReadInt32();
                    Index = r.ReadInt32();
                    Count = r.ReadInt32();
                    Start = r.ReadInt32();
                    if (Count != 0) 
                    {
                      //  WriteStuff(r, 10, "NewVarNameRefrence");
                        Offsets = new int[Count];
                        var save = r.BaseStream.Position;
                        r.BaseStream.Position = Start;
                        if (Count > 0)
                        {
                            for (int i = 0; i < Count; i++)
                            {
                                uint first = r.ReadUInt32(); // skip the first pop/push/function opcode
                                int position = (int)r.BaseStream.Position;
                                int code = (int)(first >> 24);
                                Debug.Assert(code == 69 || code == 195 || code == 192 || code == 194); 
                                // 69 is the new pop
                                // 195 pushb?
                                // 192 push general?  Old push code
                                // OOOH 194 push global, I am seeing a partern!
                                int offset = r.ReadInt32() & 0x00FFFFFF;
                                Offsets[i] = position;
                                r.BaseStream.Position += offset - 8;
                            }
                        }
                        r.BaseStream.Position = save;
                    }else
                    {
                        Debug.Assert(Start == -1); // seems to be the case
                    }
                }
            }
            void NewVarChunk(Chunk func)
            {
                r.BaseStream.Position = func.start;
                int globalCount = r.ReadInt32();
                int localCount = r.ReadInt32();
                int nextCount = r.ReadInt32();
                List<NewVarNameRefrence> locals = new List<NewVarNameRefrence>();
                List<NewVarNameRefrence> builtin = new List<NewVarNameRefrence>();
                List<NewVarNameRefrence> globals = new List<NewVarNameRefrence>();
                List<NewVarNameRefrence> trash = new List<NewVarNameRefrence>();
                while (r.BaseStream.Position < func.end)
                {
                    NewVarNameRefrence nref = new NewVarNameRefrence(r);
                   
                    if (nref.Type == -1)
                    {
                        if (nref.Index == -6)
                        {
                            Debug.Assert(!newvarTypeLookup.ContainsKey(nref.Name));
                            newvarTypeLookup[nref.Name] = VarType.BuiltIn;
                            builtin.Add(nref);
                        }
                        else {
                            if (!newvarTypeLookup.ContainsKey(nref.Name)) newvarTypeLookup.Add(nref.Name, VarType.Local);
                            else newvarTypeLookup[nref.Name] |= VarType.Local;
                            locals.Add(nref);
                        }
                    }
                    else if (nref.Type == -5)
                    {
                        if (!newvarTypeLookup.ContainsKey(nref.Name)) newvarTypeLookup.Add(nref.Name, VarType.Global);
                        else newvarTypeLookup[nref.Name] |= VarType.Global;
                        globals.Add(nref);
                    }
                    else trash.Add(nref);
                    if(nref.Offsets!= null) foreach (var o in nref.Offsets) offsetLookup.Add(o, nref);
                }
                Debug.Assert(globalCount == globals.Count);
                Debug.Assert(localCount == locals.Count);
                // trash is all just arguments?


                WriteStuff(10, "varend");
            }
            public void RefactorNewChunks(Chunk func, Chunk vars)
            {
                NewFunctionChunk(func);
                NewVarChunk(vars);
                //      WriteStuff(10, "func");
                //    r.BaseStream.Position = vars.start;
                //    stuff = r.ReadInt32(10);
                //  WriteStuff(stuff,"var");
            }
            class NewFuncNameRefrence : CodeNameRefrence
            {
                public NewFuncNameRefrence(BinaryReader r)
                {
                    this.Name = r.ReadStringFromNextOffset();
                    Count = r.ReadInt32();
                    Start = r.ReadInt32();
                    Offsets = new int[Count];
                    var save = r.BaseStream.Position;
                    r.BaseStream.Position = Start;
                    if (Count > 0)
                    {
                        for (int i = 0; i < Count; i++)
                        {
                            uint first = r.ReadUInt32(); // skip the first pop/push/function opcode
                            int position = (int)r.BaseStream.Position;
                            var code = GMCodeUtil.getFromRaw(first);
                            Debug.Assert((int)code == 217); // new call opcode
                            int offset = r.ReadInt32() & 0x00FFFFFF;
                            Offsets[i] = position;
                            r.BaseStream.Position += offset - 8;
                        }
                    }
                    r.BaseStream.Position = save;
                }
            }
            class OldCodeNameRefrence : CodeNameRefrence
            {
                public OldCodeNameRefrence(BinaryReader r)
                {
                    Name = r.ReadStringFromNextOffset();
                    Count = r.ReadInt32();
                    Start = r.ReadInt32();
                    Offsets = new int[Count];
                    var save = r.BaseStream.Position;
                    r.BaseStream.Position = Start;
                    if (Count > 0)
                    {
                        for (int i = 0; i < Count; i++)
                        {
                            uint first = r.ReadUInt32(); // skip the first pop/push/function opcode
                            int position = (int) r.BaseStream.Position;
                            var code = GMCodeUtil.getFromRaw(first);
                                Debug.Assert(code == GMCode.Push || code == GMCode.Pop || code == GMCode.Call);
                            int offset = r.ReadInt32() & 0x00FFFFFF;
                            Offsets[i] = position;
                            r.BaseStream.Position += offset - 8;
                        }
                    }
                    r.BaseStream.Position = save;
                }
            }
            public void AddRefs(int start, int size)
            {
                r.BaseStream.Position = start;
                int refCount = size / 12;

                //   int refCount = r.ReadInt32(); // what is this first number? refrence count?
                //   System.Diagnostics.Debug.WriteLine("Reading {0} refrences from {1}", refCount, refChunk.name);
                //  System.Diagnostics.Debug.WriteLine("Reading {0} refrences from {1}", count, refChunk.name);
                // Each ref is 3 ints long, firt is name refrence, second is number of refs, and thrid is the chain
                for (int i = 0; i < refCount; i++)
                {
                    OldCodeNameRefrence nref = new OldCodeNameRefrence(r);
                    refs.Add(nref.Name, nref);
                    foreach (var o in nref.Offsets) offsetLookup.Add(o, nref);
                }
            }
            public void WriteAllChangesToBytes()
            {
                
                foreach (var r in offsetLookup)
                {
                    int offset = r.Key;
                //    int item2 = item2 & 0xF0000000; ///  | (int)(struct32s[key1][i + 1] - struct32s[key1][i]) & 0xFFFFFFF;
                    int debug0 = BitConverter.ToInt32(rawData, offset);
                    int index = stringToIndex[r.Value.Name];
                    rawData[offset + 0] = (byte) ((index) & 0xFF);
                    rawData[offset + 1] = (byte) ((index >> 8) & 0xFF);
                    rawData[offset + 2] = (byte) ((index >> 16) & 0xFF);
                }
            }
        }
        static void RefactorCode()
        {
            if (codes == null)
            {
                CheckList("CODE", ref codes); // fill out all the scripts first
                RefactorCodeManager rcm = new RefactorCodeManager(File.rawData);
                Chunk funcChunk = fileChunks["FUNC"]; // function names
                Chunk varChunk = fileChunks["VARI"]; // var names

                rcm.AddRefs(funcChunk.start, funcChunk.size);
                rcm.AddRefs(varChunk.start, varChunk.size); // add all the reffs
                rcm.WriteAllChangesToBytes();
            }
        }
        static void NewRefactorCode()
        {
            if (codes == null)
            {
          
                /*
            int codepositionStart = 0;
            //  // fill out all the scripts first

            {
                BinaryReader r = new BinaryReader(new MemoryStream(rawData));
                r.BaseStream.Position = fileChunks["CODE"].start;
                var entries = r.ReadChunkEntries();
                codepositionStart = (int)r.BaseStream.Position; 
                // ment for debug, to show where all the raw code starts at
            }
            */

                RefactorCodeManager rcm = new RefactorCodeManager(File.rawData);
                Chunk funcChunk = fileChunks["FUNC"]; // function names
                Chunk varChunk = fileChunks["VARI"]; // var names
                if(Context.Version == UndertaleVersion.V10000)
                {
                    CheckList("CODE", ref codes);
                    rcm.AddRefs(funcChunk.start, funcChunk.size);
                    rcm.AddRefs(varChunk.start, varChunk.size); // old method
                }else
                {
                    List<NewCode> new_codes = new List<NewCode>();
                    // The tricky dicky with CODE is that the raw code block is here RIGHT after the entries
                    // It goes (count, entries, BLOCKOFALLCODE, entry, entry, .. ) and the offset positions are 
                    // based off the start of BLOCKOFCODE?
                    CheckList("CODE", ref new_codes);

                    codes = new_codes.Select(x => (File.Code)x).ToList();
                    rcm.RefactorNewChunks(funcChunk, varChunk);
                }
                rcm.WriteAllChangesToBytes();
            }
        }
        public static HashSet<System.Drawing.Color>[] CollectTextureColors()
        {
            var textureCache = Sprite.TextureCache;
            HashSet<System.Drawing.Color> allcoclors = new HashSet<System.Drawing.Color>();
            HashSet<System.Drawing.Color>[] textureColor = new HashSet<System.Drawing.Color>[textureCache.Count];
            for (int i = 0; i < textureColor.Length; i++)
            {
                Bitmap bmp = new Bitmap(textureCache[i]);
                var currentHash = textureColor[i] = new HashSet<System.Drawing.Color>();

                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        System.Drawing.Color c = bmp.GetPixel(x, y);
                        currentHash.Add(c);
                        allcoclors.Add(c);
                    }
                Context.Info("Texture {0} has {1} colors",i, currentHash.Count);
            }
            Context.Info("All textures have  {0} colors", allcoclors.Count);
            return textureColor;
        }
        public static void LoadEveything()
        {
            CheckStrings();
            CheckList("TXTR", ref textures);
            CheckList("BGND", ref backgrounds);
            CheckList("TPAG", ref spriteframes);
            CheckList("SPRT", ref sprites);
            CheckList("ROOM", ref rooms);
            CheckList("AUDO", ref rawAudio);
            CheckList("SOND", ref sounds);
            CheckList("FONT", ref fonts);
            CheckList("OBJT", ref objects);



            CheckList("SCPT", ref scripts);
            // CheckList("CODE", ref codes);
        //    CollectTextureColors();
            NewRefactorCode();
          
            DebugPring();
        }
        public static IReadOnlyList<Script> Scripts { get { CheckList("SCPT", ref scripts); return scripts; } }
        public static IReadOnlyList<string> Strings { get { CheckStrings(); return stringList; } }
        public static IReadOnlyList<Code> Codes { get { RefactorCode(); return codes; } }
        public static IReadOnlyList<Font> Fonts { get { CheckList("FONT", ref fonts); return fonts; } }
        public static IReadOnlyList<Texture> Textures { get { CheckList("TXTR", ref textures); return textures; } }
        public static IReadOnlyList<SpriteFrame> SpriteFrames { get { CheckList("TPAG", ref spriteframes); return spriteframes; } }
        public static IReadOnlyList<Sprite> Sprites { get { CheckList("SPRT", ref sprites); return sprites; } }
        public static IReadOnlyList<GObject> Objects { get { CheckList("OBJT", ref objects); return objects; } }
        public static IReadOnlyList<Room> Rooms { get { CheckList("ROOM", ref rooms); return rooms; } }
        public static IReadOnlyList<Background> Backgrounds { get { CheckList("BGND", ref backgrounds); return backgrounds; } }
        public static IReadOnlyList<AudioFile> Sounds { get { CheckList("AUDO", ref rawAudio); CheckList("SOND", ref sounds); return sounds; } }
        static void CheckList<T>(string chunkName, ref List<T> list) where T : GameMakerStructure, new()
        {
            if (rawData == null) throw new FileLoadException("Data.win file not open");
            if (list == null) list = new List<T>();
            if (list.Count == 0)
            {
                Chunk chunk;
                BinaryReader r = new BinaryReader(new MemoryStream(rawData));
                if (fileChunks.TryGetValue(chunkName, out chunk)) ReadList(list, r, chunk.start, chunk.end);// textures
            }
        }
        static void ReadList<T>(List<T> list, BinaryReader r, int start, int end) where T : GameMakerStructure, new()
        {
            bool isNamedResouce = typeof(T).GetInterfaces().Contains(typeof(INamedResrouce));
            list.Clear();
            foreach (var e in r.ForEachEntry(start))
            {
                T t = new T();
                t.Read(r, e.Index);
                list.Add(t);
                if (isNamedResouce)
                {
                    INamedResrouce nr = t as INamedResrouce;
                    if (namedResourceLookup == null) namedResourceLookup = new Dictionary<string, GameMakerStructure>();
                    namedResourceLookup.Add(nr.Name, t);
                }
            }
        }

      
        public static void DebugPring()
        {
            using (StreamWriter sr = new StreamWriter("object_info.txt"))
            {
                for (int i = 0; i < objects.Count; i++)
                {
                    var o = objects[i];
                    sr.Write("{0,-4}: Name: {1:-20}", i, o.Name);
                    if (o.Parent > 0) sr.Write("  Parent({0}): {1}", o.Parent, File.Objects[o.Parent].Name);
                    sr.WriteLine();
                }
            }
          //  using (StreamWriter sr = new StreamWriter("sprite_info.lua"))
         //   {
             //   DebugPrint(sprites, sr, "_sprites", true);
          //  }
          //  using (StreamWriter sr = new StreamWriter("font_info.lua"))
         //   {
            //    DebugPrint(fonts, sr, "_fonts", true);
          //  }
            using (StreamWriter sr = new StreamWriter("room_info.txt"))
            {
                for (int i = 0; i < rooms.Count; i++)
                {
                    var o = rooms[i];
                    sr.Write("{0,-4}: Name: {1:-20} Size({2},{3})", i, o.Name, o.Width, o.Height);
                    sr.WriteLine();
                    if (o.Objects.Length > 0)
                        for (int j = 0; j < o.Objects.Length; j++)
                        {
                            var oo = o.Objects[j];
                            var obj = objects[oo.Index];
                            sr.WriteLine("       Object: {0}  Pos({1},{2}", obj.Name, oo.X, oo.Y);
                        }
                }
            }
            { // xmltest
                const string filename = "room_all.xml";
                if (System.IO.File.Exists(filename)) System.IO.File.Delete(filename);
                using (System.IO.FileStream file = new System.IO.FileStream(filename, FileMode.Create))
                {
                    System.Xml.Serialization.XmlSerializer writer = new System.Xml.Serialization.XmlSerializer(typeof(File.AudioFile));
                    writer.Serialize(file, Sounds[45]);
                    file.Flush();
                } 
            }
            {
                const string filename = "room_all.lua";
                if (System.IO.File.Exists(filename)) System.IO.File.Delete(filename);
                using (StreamWriter file = new StreamWriter(filename))
                {
                    file.WriteLine("local rooms = {");
                    foreach(var room in Rooms)
                    {
                        var helper = room.CreateHelper();
                        helper.DebugSave(file);
                    }
                    file.WriteLine("}");
                    file.WriteLine("_rooms = rooms");
                }
            }
            //  
            // var path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "//SerializationOverview.xml";
        }
        
        public static void LoadDataWin(string filename)
        {
            if (File.filename != null && File.rawData != null && File.filename == filename) return; // don't do anything, file already loaded
            File.rawData = null; // clear old data
            File.filename = filename;
            InternalLoad();


        }
    }
}
