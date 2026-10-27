using KSA.Core.Data;

namespace KSA.KSP.Data
{
    /// <summary>Converts between the game's ConfigNode and Core's platform-neutral DataNode.</summary>
    public static class ConfigNodeConverter
    {
        public static DataNode ToDataNode(ConfigNode node)
        {
            if (node == null) return null;
            var result = new DataNode(node.name);
            for (int i = 0; i < node.values.Count; i++)
            {
                ConfigNode.Value v = node.values[i];
                result.AddValue(v.name, v.value);
            }
            for (int i = 0; i < node.nodes.Count; i++)
            {
                DataNode child = ToDataNode(node.nodes[i]);
                if (child != null) result.AddChild(child);
            }
            return result;
        }

        public static ConfigNode ToConfigNode(DataNode node)
        {
            if (node == null) return null;
            var result = new ConfigNode(node.Name);
            foreach (var kv in node.Values) result.AddValue(kv.Key, kv.Value);
            foreach (DataNode child in node.Children)
            {
                ConfigNode c = ToConfigNode(child);
                if (c != null) result.AddNode(c);
            }
            return result;
        }
    }
}
