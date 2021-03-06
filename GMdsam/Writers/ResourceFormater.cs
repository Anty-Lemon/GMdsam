﻿using GameMaker.Dissasembler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GameMaker.Ast;
using static GameMaker.File;
using System.Runtime.Serialization.Json;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace GameMaker.Writers
{
    public class ResourceFormater : PlainTextWriter
    {
        #region Pritty JSON Print Parser
        static Regex string_match = new Regex(@"""[^ ""\\] * (?:\\.[^ ""\\] *)*""", RegexOptions.Compiled);
        static Regex number_match = new Regex(@"\d+", RegexOptions.Compiled);
        static Regex float_match = new Regex(@"(?:^|(?<=\s))[0-9]*\.?[0-9](?=\s|$)");

        internal abstract class TokenStream
        {
            public abstract void Restart();
            protected abstract char _Next();
            public char Next()
            {
                char c;
                while (char.IsWhiteSpace(c = _Next())) ;
                if (c == 0) return Current = c;
                Prev = Current;
                return Current = char.ToLower(c);
            }
            public char Prev { get; private set; }
            public char Current { get; private set; }
        }
        class FromStream : TokenStream
        {
            StreamReader _reader;
            public FromStream(Stream stream) { this._reader = new StreamReader(stream); Restart(); }
            public override void Restart() { _reader.BaseStream.Position = 0; }
            protected override char _Next()
            {
                int c = _reader.Read();
                return c == -1 ? default(char) : (char) c;
            }
        }
        class FromString : TokenStream
        {
            string _string;
            int _pos;
            public FromString(string str) { this._string = str; Restart(); }
            public override void Restart() { this._pos = 0; }
            protected override char _Next()
            {
                return _pos < _string.Length ? _string[_pos++] : default(char);
            }
        }

        // cause I am lazy


        // Really simple state machine.  I guess I could work on it to reduce the need for 
        // last and from, but I don't want thie recursive decent parser to need more than 4 
        // functions:P
        char ParseValue(TokenStream stream, int level, char from = default(char))
        {
            char ch = default(char);
            char last = default(char);
            do
            {
                last = ch;
                ch = stream.Next();
                switch (ch)
                {
                    case '"':
                        Write(ch);
                        while (ch != 0)
                        {
                            ch = stream.Next();
                            if (stream.Prev != '\\' && ch == '"') break;
                            Write(ch);
                        }
                        Write(ch);
                        break;
                    case '{':
                        Indent++;
                        if (from == '[')// object array
                        {
                            if (last != ',') // first start
                            {

                                WriteLine();
                                Write(ch);
                                Write(' ');
                                ch = ParseValue(stream, level++, '('); // object inline
                            }
                            else
                            {
                                Write(ch);
                                Write(' ');
                                ch = ParseValue(stream, level++, '('); // object inline
                            }
                        }
                        else if (last == ',')
                        {
                            Write(ch);
                            WriteLine();// usally first level
                            ch = ParseValue(stream, level++, '{');
                        }
                        else WriteLine();// usally first level
                        Indent--;
                        Write(' '); // final space
                        Write(ch); // write the ending bracket
                        break;
                    case '[':

                        Write(ch);

                        ch = ParseValue(stream, level++, '[');
                        Write(ch);
                        break;
                    case '}': return '}';
                    case ']':
                        //writer.Write(ch);
                        if (!char.IsNumber(last)) WriteLine();
                        return ']';
                    case ',':
                        Write(ch);
                        if (from == '[')
                        {
                            if (!char.IsNumber(last)) WriteLine();
                            else if (last == '}') WriteLine();
                            else Write(' ');
                        }
                        else if (from != '(') WriteLine(); // its an object but we want it inline
                        else Write('\t');
                        break;
                    case ':':
                        Write(ch);
                        Write(' ');
                        break;
                    default:
                        Write(ch);
                        break;
                }

            } while (ch != 0);
            return default(char);
        }

        // I just gave up here and set up the first level
        void WriteStart(TokenStream stream, int level = 0) // test if we are starting on an object or on an array
        {
            char ch = stream.Next();
            if (ch == '{')
            {
                WriteLine('{');
                Indent++;

                ParseValue(stream, level);
                WriteLine();
                Indent--;
                Write('}');
            }
            else if (ch == '[')
            {
                WriteLine('[');
                while (ch != ']')
                {
                    Indent++;

                    ParseValue(stream, 0);
                    WriteLine();
                    Indent--;
                    ch = stream.Next();
                    if (ch == ',')
                    {
                        Write(',');
                        ch = stream.Next();
                    }
                }
                Indent--;
                WriteLine(']');
            }
        }
        #endregion
        bool make_pretty;
        public ResourceFormater(TextWriter writer, bool make_pretty = true) : base(writer) { this.make_pretty = make_pretty; }
        public ResourceFormater(bool make_pretty = true) : base() { this.make_pretty = make_pretty; }
        public ResourceFormater(string filename, bool make_pretty = true) : base(filename) { this.make_pretty = make_pretty; }

        string DefaultJSONSerilizeToString<T>(T o) where T : GameMakerStructure
        {
            using (MemoryStream ssw = new MemoryStream())
            {
                DataContractJsonSerializerSettings set = new DataContractJsonSerializerSettings();
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(T));

                ser.WriteObject(ssw, o);
                ssw.Position = 0;
                StreamReader sr = new StreamReader(ssw);
                return sr.ReadToEnd();
            }
        }

        public string FindFieldPositionsAndFixString(string line, List<int> fieldmaxes)
        {
            StringBuilder sb = new StringBuilder(); // Instead of the chain of Replace statments, might as well do it here
          int fieldpos = 0;
            int vlength = 0;
            for (int i = 0; i < line.Length; i++, vlength++)
            {
                char c = line[i];
                switch(c){
                    case '{':
                        sb.Append(c);
                        sb.Append(' ');
                        vlength++;
                        break;
                    case ':':
                    case '}':
                        sb.Append(' ');
                        sb.Append(c);
                        vlength++;
                        break;
                    case ',':

                        sb.Append(c);
                        sb.Append('\t');
                        if(fieldpos == fieldmaxes.Count) fieldmaxes.Add(vlength);
                        int nvlength = fieldmaxes[fieldpos];
                        if (vlength > nvlength) nvlength = vlength;
                        vlength = nvlength;
                        fieldmaxes[fieldpos] = nvlength;
                        fieldpos++;
                        break;
                    default:
                        sb.Append(c);
                        break;

                }
            }
            return sb.ToString();
        }
        public virtual void WriteAll<T>(IEnumerable<T> all) where T : File.GameMakerStructure
        {
            
            List<string> lines = new List<string>();
            List<int> fieldmaxes = new List<int>();
            foreach (var a in all)
            {
                using (MemoryStream ssw = new MemoryStream())
                {
                    DataContractJsonSerializerSettings set = new DataContractJsonSerializerSettings();
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(T));
                    ser.WriteObject(ssw, a);
                    ssw.Position = 0;
                    StreamReader sw = new StreamReader(ssw);
                    string line = FindFieldPositionsAndFixString(sw.ReadToEnd(), fieldmaxes);
                    lines.Add(line);
                }
            }
            Write('[');
            bool needComma = false;
            fieldmaxes[6] += 5;
            fieldmaxes[7] += 5;
            fieldmaxes[8] += 5;


            this.Indent++;
            TabStops = fieldmaxes.ToArray();
            WriteLine();
            PrintTabStops();
            foreach (var line in lines)
            {
                if (needComma) Write(",");
                else needComma = false;
                WriteLine();
                Write(line);
            }
            WriteLine();
            TabStops = null;
            this.Indent--;
            WriteLine(']');
            Flush(); // flush it
        }
   
       
     
        public virtual void Write(File.Code code)
        {
            using (var output = new BlockToCode(new ErrorContext(code.Name), this))
            {
                new GameMaker.Writer(output).WriteCode(code);
                Write(output.ToString());
            }
        }
    }
}
