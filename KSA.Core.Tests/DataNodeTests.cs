using System.Collections.Generic;
using KSA.Core.Data;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class DataNodeTests
    {
        [Test]
        public void Values_RepeatedKeys_AreAllReturned()
        {
            var n = new DataNode("X").AddValue("a", "1").AddValue("a", "2").AddValue("b", "3");
            Assert.That(n.GetValue("a"), Is.EqualTo("1"));
            Assert.That(n.GetValues("a"), Is.EqualTo(new[] { "1", "2" }));
            Assert.That(n.GetValue("missing", "dflt"), Is.EqualTo("dflt"));
        }

        [Test]
        public void TypedGetters_ParseInvariantAndFallBack()
        {
            var n = new DataNode("X").AddValue("i", 42).AddValue("d", 1.5).AddValue("t", true).AddValue("bad", "zzz").AddValue("list", " a, b ,,c ");
            Assert.That(n.GetInt("i", 0), Is.EqualTo(42));
            Assert.That(n.GetDouble("d", 0), Is.EqualTo(1.5));
            Assert.That(n.GetBool("t", false), Is.True);
            Assert.That(n.GetInt("bad", 7), Is.EqualTo(7));
            Assert.That(n.GetList("list"), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Text_RoundTrips()
        {
            string text = "KSA_TEST\n{\n\tid = one // comment\n\tCHILD\n\t{\n\t\tk = v = with equals\n\t}\n\tCHILD { k = inline }\n}\n";
            List<DataNode> nodes = DataNodeText.ParseAll(text);

            Assert.That(nodes.Count, Is.EqualTo(1));
            Assert.That(nodes[0].Name, Is.EqualTo("KSA_TEST"));
            Assert.That(nodes[0].GetValue("id"), Is.EqualTo("one"));
            Assert.That(nodes[0].GetChildren("CHILD").Count, Is.EqualTo(2));
            Assert.That(nodes[0].GetChildren("CHILD")[0].GetValue("k"), Is.EqualTo("v = with equals"));
            Assert.That(nodes[0].GetChildren("CHILD")[1].GetValue("k"), Is.EqualTo("inline"));

            List<DataNode> again = DataNodeText.ParseAll(DataNodeText.Write(nodes[0]));
            Assert.That(again[0].GetChildren("CHILD")[0].GetValue("k"), Is.EqualTo("v = with equals"));
        }
    }
}
