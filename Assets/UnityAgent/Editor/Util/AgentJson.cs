using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UnityAgent.Editor.Util
{
    /// <summary>
    /// Lightweight JSON helper for LLM payloads. Avoids hard dependency on Newtonsoft.
    /// </summary>
    public static class AgentJson
    {
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        public static object Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            var parser = new Parser(json);
            return parser.ParseValue();
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            var parsed = Parse(json);
            return parsed as Dictionary<string, object>;
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback = null)
        {
            if (obj == null || !obj.TryGetValue(key, out var value) || value == null)
                return fallback;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback = false)
        {
            if (obj == null || !obj.TryGetValue(key, out var value) || value == null)
                return fallback;
            if (value is bool b) return b;
            if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
                return parsed;
            return fallback;
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int fallback = 0)
        {
            if (obj == null || !obj.TryGetValue(key, out var value) || value == null)
                return fallback;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        public static float GetFloat(Dictionary<string, object> obj, string key, float fallback = 0f)
        {
            if (obj == null || !obj.TryGetValue(key, out var value) || value == null)
                return fallback;
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        public static Dictionary<string, object> GetObject(Dictionary<string, object> obj, string key)
        {
            if (obj == null || !obj.TryGetValue(key, out var value))
                return null;
            return value as Dictionary<string, object>;
        }

        public static List<object> GetArray(Dictionary<string, object> obj, string key)
        {
            if (obj == null || !obj.TryGetValue(key, out var value))
                return null;
            return value as List<object>;
        }

        public static T GetEnum<T>(Dictionary<string, object> obj, string key, T fallback) where T : struct
        {
            var s = GetString(obj, key);
            if (string.IsNullOrEmpty(s)) return fallback;
            return Enum.TryParse(s, true, out T result) ? result : fallback;
        }

        static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value)
            {
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case Enum e:
                    WriteString(sb, e.ToString());
                    break;
                case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case IDictionary dict:
                    sb.Append('{');
                    var first = true;
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                        sb.Append(':');
                        WriteValue(sb, entry.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable enumerable when value is not string:
                    sb.Append('[');
                    first = true;
                    foreach (var item in enumerable)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (value != null)
            {
                foreach (var c in value)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 32)
                                sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                            else
                                sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string _json;
            int _index;

            public Parser(string json)
            {
                _json = json;
                _index = 0;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (_index >= _json.Length) return null;
                var c = _json[_index];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't' || c == 'f') return ParseBool();
                if (c == 'n') return ParseNull();
                return ParseNumber();
            }

            Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>();
                Expect('{');
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _index++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    var key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    var value = ParseValue();
                    result[key] = value;
                    SkipWhitespace();
                    var next = Peek();
                    if (next == ',')
                    {
                        _index++;
                        continue;
                    }
                    if (next == '}')
                    {
                        _index++;
                        break;
                    }
                    throw new FormatException($"Unexpected character '{next}' in JSON object at {_index}");
                }

                return result;
            }

            List<object> ParseArray()
            {
                var result = new List<object>();
                Expect('[');
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _index++;
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    var next = Peek();
                    if (next == ',')
                    {
                        _index++;
                        continue;
                    }
                    if (next == ']')
                    {
                        _index++;
                        break;
                    }
                    throw new FormatException($"Unexpected character '{next}' in JSON array at {_index}");
                }

                return result;
            }

            string ParseString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (_index < _json.Length)
                {
                    var c = _json[_index++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (_index >= _json.Length)
                        throw new FormatException("Unterminated escape in JSON string");

                    var e = _json[_index++];
                    switch (e)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            sb.Append(e);
                            break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _json.Length)
                                throw new FormatException("Invalid unicode escape");
                            var hex = _json.Substring(_index, 4);
                            _index += 4;
                            sb.Append((char)Convert.ToInt32(hex, 16));
                            break;
                        default:
                            sb.Append(e);
                            break;
                    }
                }

                throw new FormatException("Unterminated JSON string");
            }

            object ParseNumber()
            {
                var start = _index;
                if (Peek() == '-') _index++;
                while (_index < _json.Length && char.IsDigit(_json[_index])) _index++;
                var isFloat = false;
                if (Peek() == '.')
                {
                    isFloat = true;
                    _index++;
                    while (_index < _json.Length && char.IsDigit(_json[_index])) _index++;
                }
                if (Peek() == 'e' || Peek() == 'E')
                {
                    isFloat = true;
                    _index++;
                    if (Peek() == '+' || Peek() == '-') _index++;
                    while (_index < _json.Length && char.IsDigit(_json[_index])) _index++;
                }

                var token = _json.Substring(start, _index - start);
                if (isFloat)
                    return double.Parse(token, CultureInfo.InvariantCulture);
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    return l;
                return double.Parse(token, CultureInfo.InvariantCulture);
            }

            object ParseBool()
            {
                if (_json.Substring(_index).StartsWith("true", StringComparison.Ordinal))
                {
                    _index += 4;
                    return true;
                }
                if (_json.Substring(_index).StartsWith("false", StringComparison.Ordinal))
                {
                    _index += 5;
                    return false;
                }
                throw new FormatException($"Invalid boolean at {_index}");
            }

            object ParseNull()
            {
                if (_json.Substring(_index).StartsWith("null", StringComparison.Ordinal))
                {
                    _index += 4;
                    return null;
                }
                throw new FormatException($"Invalid null at {_index}");
            }

            void SkipWhitespace()
            {
                while (_index < _json.Length && char.IsWhiteSpace(_json[_index]))
                    _index++;
            }

            char Peek() => _index < _json.Length ? _json[_index] : '\0';

            void Expect(char c)
            {
                SkipWhitespace();
                if (_index >= _json.Length || _json[_index] != c)
                    throw new FormatException($"Expected '{c}' at {_index}");
                _index++;
            }
        }
    }
}
