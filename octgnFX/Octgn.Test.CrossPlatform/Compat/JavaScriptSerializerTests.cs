// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using NUnit.Framework;

namespace Octgn.Test.Compat
{
    /// <summary>
    /// The .NET 10 stand-in for System.Web.Extensions must return what game scripts expect from
    /// .NET Framework's JavaScriptSerializer.
    /// </summary>
    [TestFixture]
    public class JavaScriptSerializerTests
    {
        // A deck as exported by a deck builder (the shape Marvel Champions reads).
        private const string Deck = @"{
            ""name"": ""Spider-Man starter"",
            ""investigator_code"": ""01001a"",
            ""slots"": { ""01001"": 1, ""01089"": 3 },
            ""tags"": [""starter"", 2, 2.5, true, null],
            ""big"": 12345678901,
            ""created"": ""\/Date(1600000000000)\/""
        }";

        [Test]
        public void DeserializeObject_ReturnsTheShapesOfTheOriginal()
        {
            var deck = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(Deck);

            Assert.AreEqual("Spider-Man starter", deck["name"]);
            var slots = (Dictionary<string, object>)deck["slots"];
            Assert.AreEqual(3, slots["01089"]);
            Assert.IsInstanceOf<int>(slots["01089"]);

            var tags = (object[])deck["tags"];
            CollectionAssert.AreEqual(new object[] { "starter", 2, 2.5m, true, null }, tags);
            Assert.IsInstanceOf<decimal>(tags[2]);

            Assert.AreEqual(12345678901L, deck["big"]);
            Assert.AreEqual(new DateTime(2020, 9, 13, 12, 26, 40, DateTimeKind.Utc), deck["created"]);
        }

        [Test]
        public void Serialize_ThenDeserializeObject_RoundTrips()
        {
            var serializer = new JavaScriptSerializer();
            var table = new Hashtable { // Python's dict is a non-generic IDictionary too.
                ["villain"] = "Rhino",
                ["players"] = new List<object> { 1, 2 },
                ["threat"] = 7L,
                ["ratio"] = 0.5,
                ["when"] = new DateTime(2022, 4, 29, 0, 0, 0, DateTimeKind.Utc),
            };

            var json = serializer.Serialize(table);
            var back = (Dictionary<string, object>)serializer.DeserializeObject(json);

            Assert.AreEqual("Rhino", back["villain"]);
            CollectionAssert.AreEqual(new object[] { 1, 2 }, (object[])back["players"]);
            Assert.AreEqual(7, back["threat"]);
            Assert.AreEqual(0.5m, back["ratio"]);
            Assert.AreEqual(table["when"], back["when"]);
            StringAssert.Contains(@"""\/Date(1651190400000)\/""", json);
        }

        public class DeckInfo
        {
            public string Name { get; set; }
            public Dictionary<string, int> Slots { get; set; }
            public List<object> Tags { get; set; }
        }

        [Test]
        public void Deserialize_FillsTypedObjects()
        {
            var deck = new JavaScriptSerializer().Deserialize<DeckInfo>(Deck);

            Assert.AreEqual("Spider-Man starter", deck.Name);
            Assert.AreEqual(3, deck.Slots["01089"]);
            Assert.AreEqual(5, deck.Tags.Count);
            CollectionAssert.AreEqual(new[] { "a", "b" }, new JavaScriptSerializer().Deserialize<string[]>(@"[""a"", ""b""]"));
        }

        [Test]
        public void Serialize_RejectsCircularReferences()
        {
            var list = new List<object>();
            list.Add(list);
            Assert.Throws<InvalidOperationException>(() => new JavaScriptSerializer().Serialize(list));
        }

        [Test]
        public void DeserializeObject_EnforcesMaxJsonLength()
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 10 };
            Assert.Throws<ArgumentException>(() => serializer.DeserializeObject(@"{""name"": ""too long""}"));
        }

        [Test]
        public void DeserializeObject_RejectsInvalidJson()
        {
            Assert.Throws<ArgumentException>(() => new JavaScriptSerializer().DeserializeObject("{name"));
        }
    }
}
