using System;
using System.Collections.Generic;
using System.Text;

namespace KSA.Core.Data
{
    /// <summary>
    /// Reads and writes the brace-delimited text format used by the game's config files:
    ///   NAME { key = value  CHILD { ... } }
    /// Only used by tests and tooling; in-game the host converts the game's own node objects directly.
    /// Supports // line comments. Values run to end of line.
    /// </summary>
    public static class DataNodeText
    {
        /// <summary>Parses a whole file into its top-level nodes.</summary>
        public static List<DataNode> ParseAll(string text)
        {
            var root = new DataNode("ROOT");
            var stack = new Stack<DataNode>();
            stack.Push(root);
            string pendingName = null;

            foreach (string rawLine in (text ?? string.Empty).Split('\n'))
            {
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                int i = 0;
                while (i < line.Length)
                {
                    char c = line[i];
                    if (c == '{')
                    {
                        string name = pendingName ?? string.Empty;
                        pendingName = null;
                        DataNode child = stack.Peek().AddChild(name);
                        stack.Push(child);
                        i++;
                        continue;
                    }
                    if (c == '}')
                    {
                        if (stack.Count > 1) stack.Pop();
                        i++;
                        continue;
                    }
                    if (char.IsWhiteSpace(c)) { i++; continue; }

                    // A token: either "key = value" (to end of line) or a bare node name.
                    string rest = line.Substring(i);
                    int brace = rest.IndexOf('{');
                    int eq = rest.IndexOf('=');
                    if (eq >= 0 && (brace < 0 || eq < brace))
                    {
                        string key = rest.Substring(0, eq).Trim();
                        string valuePart = rest.Substring(eq + 1);
                        int close = valuePart.IndexOf('}');
                        if (close >= 0)
                        {
                            // Inline "key = value }" : the value stops at the closing brace, which is processed next.
                            stack.Peek().AddValue(key, valuePart.Substring(0, close).Trim());
                            i += eq + 1 + close;
                        }
                        else
                        {
                            stack.Peek().AddValue(key, valuePart.Trim());
                            i = line.Length;
                        }
                    }
                    else if (brace >= 0)
                    {
                        pendingName = rest.Substring(0, brace).Trim();
                        i += brace;
                    }
                    else
                    {
                        pendingName = rest.Trim();
                        i = line.Length;
                    }
                }
            }
            return root.Children;
        }

        public static string Write(DataNode node)
        {
            var sb = new StringBuilder();
            Write(node, sb, 0);
            return sb.ToString();
        }

        private static void Write(DataNode node, StringBuilder sb, int depth)
        {
            string pad = new string('\t', depth);
            sb.Append(pad).Append(node.Name).Append('\n').Append(pad).Append("{\n");
            foreach (KeyValuePair<string, string> kv in node.Values)
                sb.Append(pad).Append('\t').Append(kv.Key).Append(" = ").Append(kv.Value).Append('\n');
            foreach (DataNode child in node.Children)
                Write(child, sb, depth + 1);
            sb.Append(pad).Append("}\n");
        }

        private static string StripComment(string line)
        {
            int idx = line.IndexOf("//", StringComparison.Ordinal);
            return idx >= 0 ? line.Substring(0, idx) : line;
        }
    }
}
