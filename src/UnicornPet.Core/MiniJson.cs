using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UnicornPet.Core
{
    /// <summary>
    /// A small, dependency-free JSON reader and writer. It exists so the shipped program needs no extra libraries.
    /// Parse returns Dictionary&lt;string,object&gt;, List&lt;object&gt;, string, double, bool or null and throws FormatException on any problem.
    /// It tolerates comments and trailing commas (hand-edited files) and refuses very deep or very large input.
    /// </summary>
    public static class MiniJson
    {
        public const int MaxDepth = 32;
        public const int MaxLength = 1000000;

        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("no text");
            if (text.Length > MaxLength) throw new FormatException("too large");
            var p = new Reader(text);
            p.SkipBlank();
            if (p.AtEnd) throw new FormatException("empty");
            var v = p.Value(0);
            p.SkipBlank();
            if (!p.AtEnd) throw new FormatException("unexpected text after the value");
            return v;
        }

        sealed class Reader
        {
            readonly string _s;
            int _i;
            public Reader(string s) { _s = s; _i = s.Length > 0 && s[0] == '﻿' ? 1 : 0; }
            public bool AtEnd => _i >= _s.Length;

            public void SkipBlank()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { _i++; continue; }
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                    {
                        while (_i < _s.Length && _s[_i] != '\n') _i++;
                        continue;
                    }
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
                    {
                        int end = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                        if (end < 0) throw new FormatException("unterminated comment");
                        _i = end + 2;
                        continue;
                    }
                    break;
                }
            }

            char Peek() { if (_i >= _s.Length) throw new FormatException("unexpected end"); return _s[_i]; }

            public object Value(int depth)
            {
                if (depth > MaxDepth) throw new FormatException("nested too deeply");
                char c = Peek();
                if (c == '{') return Obj(depth);
                if (c == '[') return Arr(depth);
                if (c == '"') return Str();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                if (c == '-' || (c >= '0' && c <= '9')) return Num();
                throw new FormatException("unexpected character");
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw new FormatException("bad literal");
                _i += word.Length;
            }

            object Num()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                if (_i >= _s.Length) throw new FormatException("bad number");
                if (_s[_i] == '0') { _i++; if (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') throw new FormatException("leading zero"); }
                else if (_s[_i] >= '1' && _s[_i] <= '9') { while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') _i++; }
                else throw new FormatException("bad number");
                if (_i < _s.Length && _s[_i] == '.')
                {
                    _i++;
                    int d = _i;
                    while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                    if (d == _i) throw new FormatException("bad fraction");
                }
                if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    _i++;
                    if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                    int d = _i;
                    while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                    if (d == _i) throw new FormatException("bad exponent");
                }
                double v;
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsInfinity(v) || double.IsNaN(v)) throw new FormatException("bad number"); // 1e999 is Infinity on .NET Core and an error on .NET Framework: refuse it on both
                return v;
            }

            string Str()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (_i >= _s.Length) throw new FormatException("unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw new FormatException("control character in string");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _s.Length) throw new FormatException("unterminated escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw new FormatException("bad unicode escape");
                            int code;
                            if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)) throw new FormatException("bad unicode escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default: throw new FormatException("bad escape");
                    }
                }
            }

            List<object> Arr(int depth)
            {
                _i++;
                var list = new List<object>();
                SkipBlank();
                if (Peek() == ']') { _i++; return list; }
                while (true)
                {
                    SkipBlank();
                    list.Add(Value(depth + 1));
                    SkipBlank();
                    char c = Peek();
                    if (c == ',')
                    {
                        _i++;
                        SkipBlank();
                        if (Peek() == ']') { _i++; return list; }
                        continue;
                    }
                    if (c == ']') { _i++; return list; }
                    throw new FormatException("expected , or ]");
                }
            }

            Dictionary<string, object> Obj(int depth)
            {
                _i++;
                var d = new Dictionary<string, object>();
                SkipBlank();
                if (Peek() == '}') { _i++; return d; }
                while (true)
                {
                    SkipBlank();
                    if (Peek() != '"') throw new FormatException("expected a property name");
                    var key = Str();
                    SkipBlank();
                    if (Peek() != ':') throw new FormatException("expected :");
                    _i++;
                    SkipBlank();
                    d[key] = Value(depth + 1);
                    SkipBlank();
                    char c = Peek();
                    if (c == ',')
                    {
                        _i++;
                        SkipBlank();
                        if (Peek() == '}') { _i++; return d; }
                        continue;
                    }
                    if (c == '}') { _i++; return d; }
                    throw new FormatException("expected , or }");
                }
            }
        }

        // ---------- writing ----------

        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Writes an ordered object. Values may be null, string, bool, numbers, DateTime or a list of such values.</summary>
        public static string Serialize(IEnumerable<KeyValuePair<string, object>> properties)
        {
            var sb = new StringBuilder("{\n");
            bool first = true;
            foreach (var kv in properties)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("  ").Append(Quote(kv.Key)).Append(": ");
                WriteValue(sb, kv.Value);
            }
            return sb.Append("\n}\n").ToString();
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { sb.Append(Quote((string)v)); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is DateTime) { sb.Append(Quote(((DateTime)v).ToString("o", CultureInfo.InvariantCulture))); return; }
            if (v is System.Collections.IEnumerable)
            {
                sb.Append('[');
                bool first = true;
                foreach (var x in (System.Collections.IEnumerable)v)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    WriteValue(sb, x);
                }
                sb.Append(']');
                return;
            }
            sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        // ---------- typed access helpers (wrong types give null, never throw) ----------

        public static string GetString(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) ? v as string : null;
        }

        public static bool? GetBool(Dictionary<string, object> o, string key)
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v is bool) return (bool)v;
            return null;
        }

        /// <summary>A whole number within min..max, or null for anything else (fractions, out of range, text, bool, null, missing).</summary>
        public static int? GetInt(Dictionary<string, object> o, string key, int min, int max)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || !(v is double)) return null;
            double d = (double)v;
            if (double.IsNaN(d) || double.IsInfinity(d) || d != Math.Floor(d) || d < min || d > max) return null;
            return (int)d;
        }

        public static List<string> GetStrings(Dictionary<string, object> o, string key)
        {
            var list = new List<string>();
            object v;
            if (o != null && o.TryGetValue(key, out v) && v is List<object>)
                foreach (var x in (List<object>)v)
                {
                    var s = x as string;
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            return list;
        }

        public static DateTime? ToDate(object v)
        {
            var s = v as string;
            DateTime d;
            if (s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)) return d;
            return null;
        }
    }
}
