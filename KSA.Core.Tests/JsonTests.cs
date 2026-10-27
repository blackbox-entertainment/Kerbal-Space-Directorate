using System.Collections.Generic;
using KSA.Core.Data;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    public class JsonTests
    {
        [Test]
        public void ParsesNestedObjectsArraysAndScalars()
        {
            object v = Json.Parse("{\"a\":1,\"b\":[true,null,\"x\\n\\u0041\"],\"c\":{\"d\":-2.5e1}}");
            Dictionary<string, object> obj = Json.AsObject(v);
            Assert.That(Json.GetNumber(obj, "a"), Is.EqualTo(1));
            List<object> b = Json.GetArray(obj, "b");
            Assert.That(b[0], Is.True);
            Assert.That(b[1], Is.Null);
            Assert.That(b[2], Is.EqualTo("x\nA"));
            Assert.That(Json.GetNumber(Json.GetObject(obj, "c"), "d"), Is.EqualTo(-25));
        }

        [Test]
        public void RoundTripsThroughWriter()
        {
            var obj = new Dictionary<string, object> { { "name", "Kerbal \"One\"" }, { "n", 3.5 }, { "list", new List<object> { 1.0, "two", false } } };
            string text = Json.Write(obj);
            Dictionary<string, object> back = Json.AsObject(Json.Parse(text));
            Assert.That(Json.GetString(back, "name"), Is.EqualTo("Kerbal \"One\""));
            Assert.That(Json.GetNumber(back, "n"), Is.EqualTo(3.5));
            Assert.That(Json.GetArray(back, "list").Count, Is.EqualTo(3));
        }

        [Test]
        public void RejectsTrailingGarbageAndBadInput()
        {
            object v; string err;
            Assert.That(Json.TryParse("{\"a\":1} extra", out v, out err), Is.False);
            Assert.That(Json.TryParse("{\"a\":}", out v, out err), Is.False);
            Assert.That(Json.TryParse("", out v, out err), Is.False);
        }

        [Test]
        public void ExtractObjectStripsProseAndFences()
        {
            string reply = "Sure! Here is the plan:\n```json\n{\"design_name\":\"A\",\"x\":{\"y\":\"}\"}}\n```\nHope that helps.";
            Assert.That(Json.ExtractObject(reply), Is.EqualTo("{\"design_name\":\"A\",\"x\":{\"y\":\"}\"}}"));
            Assert.That(Json.ExtractObject("no json here"), Is.Null);
        }
    }
}
