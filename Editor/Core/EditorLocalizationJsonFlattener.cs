using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace com.amari_noa.unity_editor_localization_core.editor
{
    internal sealed class EditorLocalizationJsonFlattener
    {
        public bool TryParseAndFlatten(
            string json,
            out Dictionary<string, string> table,
            out IReadOnlyList<string> parseErrors)
        {
            table = new Dictionary<string, string>(StringComparer.Ordinal);
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(json))
            {
                errors.Add("Localization JSON is empty.");
                parseErrors = errors;
                return false;
            }

            object root;
            try
            {
                root = MiniJson.Deserialize(json);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                parseErrors = errors;
                return false;
            }

            if (root is not IDictionary<string, object> rootObject)
            {
                errors.Add("Root must be a JSON object.");
                parseErrors = errors;
                return false;
            }

            FlattenObject(rootObject, string.Empty, table, errors);
            parseErrors = errors;
            return errors.Count == 0;
        }

        private static void FlattenObject(
            IDictionary<string, object> source,
            string prefix,
            Dictionary<string, string> destination,
            IList<string> errors)
        {
            foreach (var pair in source)
            {
                var key = string.IsNullOrEmpty(prefix)
                    ? pair.Key
                    : $"{prefix}.{pair.Key}";
                FlattenValue(pair.Value, key, destination, errors);
            }
        }

        private static void FlattenArray(
            IList source,
            string prefix,
            Dictionary<string, string> destination,
            IList<string> errors)
        {
            for (var i = 0; i < source.Count; i++)
            {
                var key = $"{prefix}.{i}";
                FlattenValue(source[i], key, destination, errors);
            }
        }

        private static void FlattenValue(
            object value,
            string key,
            Dictionary<string, string> destination,
            IList<string> errors)
        {
            switch (value)
            {
                case string text:
                    destination[key] = text;
                    return;
                case IDictionary<string, object> childObject:
                    FlattenObject(childObject, key, destination, errors);
                    return;
                case IList childArray:
                    FlattenArray(childArray, key, destination, errors);
                    return;
                default:
                    errors.Add($"Unsupported value type at key '{key}'. Only string leaves are allowed.");
                    return;
            }
        }

        private static class MiniJson
        {
            public static object Deserialize(string json)
            {
                if (json == null)
                {
                    return null;
                }

                var parser = new Parser(json);
                return parser.ParseValue();
            }

            private sealed class Parser
            {
                private readonly string _json;
                private int _index;

                public Parser(string json)
                {
                    _json = json ?? string.Empty;
                    _index = 0;
                    SkipWhitespace();
                }

                public object ParseValue()
                {
                    SkipWhitespace();
                    if (_index >= _json.Length)
                    {
                        throw new FormatException("Unexpected end of JSON.");
                    }

                    var c = _json[_index];
                    return c switch
                    {
                        '{' => ParseObject(),
                        '[' => ParseArray(),
                        '"' => ParseString(),
                        _ => throw new FormatException($"Only string, object, or array values are supported (at index {_index}).")
                    };
                }

                private IDictionary<string, object> ParseObject()
                {
                    Expect('{');
                    SkipWhitespace();

                    var result = new Dictionary<string, object>(StringComparer.Ordinal);
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
                        SkipWhitespace();
                        var value = ParseValue();
                        result[key] = value;

                        SkipWhitespace();
                        var separator = Peek();
                        if (separator == ',')
                        {
                            _index++;
                            continue;
                        }

                        if (separator != '}')
                        {
                            throw new FormatException($"Invalid object token at index {_index}.");
                        }

                        _index++;
                        break;
                    }

                    return result;
                }

                private IList ParseArray()
                {
                    Expect('[');
                    SkipWhitespace();

                    var result = new List<object>();
                    if (Peek() == ']')
                    {
                        _index++;
                        return result;
                    }

                    while (true)
                    {
                        SkipWhitespace();
                        result.Add(ParseValue());

                        SkipWhitespace();
                        var separator = Peek();
                        if (separator == ',')
                        {
                            _index++;
                            continue;
                        }

                        if (separator != ']')
                        {
                            throw new FormatException($"Invalid array token at index {_index}.");
                        }

                        _index++;
                        break;
                    }

                    return result;
                }

                private string ParseString()
                {
                    Expect('"');
                    var builder = new StringBuilder();

                    while (_index < _json.Length)
                    {
                        var c = _json[_index++];
                        if (c == '"')
                        {
                            return builder.ToString();
                        }

                        if (c != '\\')
                        {
                            builder.Append(c);
                            continue;
                        }

                        if (_index >= _json.Length)
                        {
                            throw new FormatException("Invalid escape sequence.");
                        }

                        var escaped = _json[_index++];
                        switch (escaped)
                        {
                            case '"':
                                builder.Append('"');
                                break;
                            case '\\':
                                builder.Append('\\');
                                break;
                            case '/':
                                builder.Append('/');
                                break;
                            case 'b':
                                builder.Append('\b');
                                break;
                            case 'f':
                                builder.Append('\f');
                                break;
                            case 'n':
                                builder.Append('\n');
                                break;
                            case 'r':
                                builder.Append('\r');
                                break;
                            case 't':
                                builder.Append('\t');
                                break;
                            case 'u':
                                if (_index + 4 > _json.Length)
                                {
                                    throw new FormatException("Invalid unicode escape.");
                                }

                                var hex = _json.Substring(_index, 4);
                                builder.Append((char)Convert.ToInt32(hex, 16));
                                _index += 4;
                                break;
                            default:
                                throw new FormatException($"Invalid escape '\\{escaped}' at index {_index}.");
                        }
                    }

                    throw new FormatException("Unterminated string.");
                }

                private char Peek()
                {
                    return _index < _json.Length ? _json[_index] : '\0';
                }

                private void Expect(char expected)
                {
                    if (Peek() != expected)
                    {
                        throw new FormatException($"Expected '{expected}' at index {_index}.");
                    }

                    _index++;
                }

                private void SkipWhitespace()
                {
                    while (_index < _json.Length && char.IsWhiteSpace(_json[_index]))
                    {
                        _index++;
                    }
                }
            }
        }
    }
}
