using System;
using System.Collections.Generic;
using System.Globalization;

namespace KSA.Core.Data
{
    /// <summary>
    /// A platform-neutral config tree: named node, ordered key/value pairs (keys may repeat), child nodes.
    /// KSA.KSP converts the game's ConfigNode to and from this; Core and tests never see ConfigNode.
    /// Used for both ModuleManager configs (careers, medals) and the save-file state.
    /// </summary>
    public sealed class DataNode
    {
        public string Name { get; }
        public List<KeyValuePair<string, string>> Values { get; } = new List<KeyValuePair<string, string>>();
        public List<DataNode> Children { get; } = new List<DataNode>();

        public DataNode(string name)
        {
            Name = name ?? string.Empty;
        }

        // --- Writing ---

        public DataNode AddValue(string key, string value)
        {
            Values.Add(new KeyValuePair<string, string>(key, value ?? string.Empty));
            return this;
        }

        public DataNode AddValue(string key, int value) { return AddValue(key, value.ToString(CultureInfo.InvariantCulture)); }
        public DataNode AddValue(string key, double value) { return AddValue(key, value.ToString("R", CultureInfo.InvariantCulture)); }
        public DataNode AddValue(string key, bool value) { return AddValue(key, value ? "True" : "False"); }

        public DataNode AddChild(string name)
        {
            var child = new DataNode(name);
            Children.Add(child);
            return child;
        }

        public DataNode AddChild(DataNode child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            Children.Add(child);
            return child;
        }

        // --- Reading ---

        public bool HasValue(string key)
        {
            for (int i = 0; i < Values.Count; i++)
                if (string.Equals(Values[i].Key, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>First value for the key, or <paramref name="fallback"/> if absent.</summary>
        public string GetValue(string key, string fallback = null)
        {
            for (int i = 0; i < Values.Count; i++)
                if (string.Equals(Values[i].Key, key, StringComparison.OrdinalIgnoreCase)) return Values[i].Value;
            return fallback;
        }

        /// <summary>Every value for a repeated key, in order.</summary>
        public List<string> GetValues(string key)
        {
            var result = new List<string>();
            for (int i = 0; i < Values.Count; i++)
                if (string.Equals(Values[i].Key, key, StringComparison.OrdinalIgnoreCase)) result.Add(Values[i].Value);
            return result;
        }

        public int GetInt(string key, int fallback)
        {
            string s = GetValue(key);
            int v;
            return s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public double GetDouble(string key, double fallback)
        {
            string s = GetValue(key);
            double v;
            return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public bool GetBool(string key, bool fallback)
        {
            string s = GetValue(key);
            if (s == null) return fallback;
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) || s == "0") return false;
            return fallback;
        }

        /// <summary>Comma-separated list value, trimmed, empties dropped.</summary>
        public List<string> GetList(string key)
        {
            var result = new List<string>();
            string s = GetValue(key);
            if (string.IsNullOrEmpty(s)) return result;
            foreach (string part in s.Split(','))
            {
                string t = part.Trim();
                if (t.Length > 0) result.Add(t);
            }
            return result;
        }

        public DataNode GetChild(string name)
        {
            for (int i = 0; i < Children.Count; i++)
                if (string.Equals(Children[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Children[i];
            return null;
        }

        public List<DataNode> GetChildren(string name)
        {
            var result = new List<DataNode>();
            for (int i = 0; i < Children.Count; i++)
                if (string.Equals(Children[i].Name, name, StringComparison.OrdinalIgnoreCase)) result.Add(Children[i]);
            return result;
        }
    }
}
