// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using Newtonsoft.Json;
using NUnit.Framework;
using Octgn.Core.Media;

namespace Octgn.Test.Core
{
    [TestFixture]
    public class ColorTests
    {
        [TestCase("#5A9ACF", 0xFF, 0x5A, 0x9A, 0xCF)]
        [TestCase("#805A9ACF", 0x80, 0x5A, 0x9A, 0xCF)]
        [TestCase("#F0A", 0xFF, 0xFF, 0x00, 0xAA)]
        [TestCase("#8F0A", 0x88, 0xFF, 0x00, 0xAA)]
        [TestCase(" crimson ", 0xFF, 0xDC, 0x14, 0x3C)]
        public void Parse(string text, int a, int r, int g, int b) {
            Assert.AreEqual(Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b), Color.Parse(text));
        }

        [TestCase("")]
        [TestCase("#12345")]
        [TestCase("#GG0000")]
        [TestCase("notacolor")]
        public void Parse_Rejects(string text) {
            Assert.IsFalse(Color.TryParse(text, out _));
            Assert.Throws<FormatException>(() => Color.Parse(text));
        }

        [Test]
        public void ToString_IsTheWpfForm() {
            Assert.AreEqual("#FF5A9ACF", Color.FromRgb(0x5A, 0x9A, 0xCF).ToString());
            Assert.AreEqual("#00FFFFFF", Colors.Transparent.ToString());
        }

        private class Holder
        {
            public Color Color { get; set; }
        }

        [Test]
        public void Json_UsesTheSameTextAsWpfColors() {
            // Json.NET writes WPF colors through their TypeConverter: "#AARRGGBB".
            var json = JsonConvert.SerializeObject(new Holder { Color = Colors.BlueViolet });
            Assert.AreEqual("{\"Color\":\"#FF8A2BE2\"}", json);
            Assert.AreEqual(Colors.BlueViolet, JsonConvert.DeserializeObject<Holder>(json).Color);
            Assert.AreEqual(Color.FromArgb(0x80, 1, 2, 3), JsonConvert.DeserializeObject<Holder>("{\"Color\":\"#80010203\"}").Color);
        }

        [Test]
        public void Equality() {
            Assert.AreEqual(Colors.Red, Color.FromRgb(255, 0, 0));
            Assert.IsTrue(Colors.Red == Color.FromArgb(255, 255, 0, 0));
            Assert.IsTrue(Colors.Red != Color.FromArgb(254, 255, 0, 0));
            Assert.AreEqual(Colors.Red.GetHashCode(), Color.FromRgb(255, 0, 0).GetHashCode());
        }
    }
}
