using System;
using System.Collections.Generic;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class MiniJsonTests
    {
        static Dictionary<string, object> Obj(string json) => (Dictionary<string, object>)MiniJson.Parse(json);

        [Fact] public void Parses_Object_With_AllValueKinds()
        {
            var o = Obj("{\"s\":\"x\",\"n\":12,\"f\":-1.5e2,\"t\":true,\"b\":false,\"z\":null,\"a\":[1,\"two\",[3]],\"o\":{\"k\":\"v\"}}");
            Assert.Equal("x", o["s"]);
            Assert.Equal(12.0, o["n"]);
            Assert.Equal(-150.0, o["f"]);
            Assert.Equal(true, o["t"]);
            Assert.Equal(false, o["b"]);
            Assert.Null(o["z"]);
            var a = (List<object>)o["a"];
            Assert.Equal(3, a.Count);
            Assert.Equal("two", a[1]);
            Assert.Equal("v", ((Dictionary<string, object>)o["o"])["k"]);
        }

        [Fact] public void Parses_Escapes_And_Unicode()
        {
            var o = Obj("{\"s\":\"a\\n\\t\\\"q\\\"\\\\ \\u00eb \\ud83e\\udd84 \\/\"}");
            Assert.Equal("a\n\t\"q\"\\ \u00eb \U0001F984 /", o["s"]);
            Assert.Equal("Zo\u00eb", Obj("{\"s\":\"Zo\u00eb\"}")["s"]);
        }

        [Fact] public void Tolerates_Comments_And_TrailingCommas()
        {
            var o = Obj("// header\n{ /* inline */ \"a\": [1, 2, ], \"b\": \"x\", }");
            Assert.Equal(2, ((List<object>)o["a"]).Count);
            Assert.Equal("x", o["b"]);
        }

        [Fact] public void Tolerates_Bom_And_Whitespace()
        {
            Assert.Equal("v", Obj("\uFEFF  \r\n\t{\"k\" : \"v\"}  \r\n")["k"]);
        }

        [Fact] public void DuplicateKeys_LastWins() => Assert.Equal("2", Obj("{\"k\":\"1\",\"k\":\"2\"}")["k"]);

        [Fact] public void TopLevel_Array_And_Scalars_Parse()
        {
            Assert.IsType<List<object>>(MiniJson.Parse("[1,2]"));
            Assert.Equal("x", MiniJson.Parse("\"x\""));
            Assert.Equal(true, MiniJson.Parse("true"));
            Assert.Null(MiniJson.Parse("null"));
        }

        [Theory]
        [InlineData("")] [InlineData("   ")] [InlineData("{")] [InlineData("{\"a\":}")] [InlineData("{\"a\" 1}")] [InlineData("{a:1}")] [InlineData("[1,,2]")]
        [InlineData("{\"a\":\"unterminated}")] [InlineData("{\"a\":1} extra")] [InlineData("nul")] [InlineData("tru")] [InlineData("{\"a\":01}")]
        [InlineData("{\"a\":\"bad \\x escape\"}")] [InlineData("{\"a\":\"\\u12\"}")] [InlineData("\"line\nbreak\"")] [InlineData("{\"a\":1e}")]
        [InlineData("/* never closed")] [InlineData("{\"a\":-}")] [InlineData("[")] [InlineData("{\"a\":[}")]
        public void Malformed_Throws_FormatException(string json) => Assert.Throws<FormatException>(() => MiniJson.Parse(json));

        [Fact] public void Null_Input_Throws_FormatException() => Assert.Throws<FormatException>(() => MiniJson.Parse(null));

        [Fact] public void DeepNesting_IsRejected_NotAStackOverflow()
        {
            var deep = new string('[', 5000) + new string(']', 5000);
            Assert.Throws<FormatException>(() => MiniJson.Parse(deep));
            var ok = new string('[', 20) + new string(']', 20);
            Assert.NotNull(MiniJson.Parse(ok));
        }

        [Fact] public void OversizeInput_IsRejected()
        {
            var big = "[" + string.Join(",", Enumerable.Repeat("1", 700000)) + "]";
            Assert.Throws<FormatException>(() => MiniJson.Parse(big));
        }

        [Fact] public void Quote_Escapes_ControlCharacters_And_Quotes()
        {
            Assert.Equal("\"a\\\"b\\\\c\\n\\u0001\"", MiniJson.Quote("a\"b\\c\n\u0001"));
            Assert.Equal("\"Zo\u00eb\"", MiniJson.Quote("Zo\u00eb"));
        }

        [Fact] public void Serialize_RoundTrips_Through_Parse()
        {
            var d = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("Name", "Zo\u00eb \"quoted\""),
                new KeyValuePair<string, object>("On", true),
                new KeyValuePair<string, object>("Off", false),
                new KeyValuePair<string, object>("Nothing", null),
                new KeyValuePair<string, object>("Count", 5),
                new KeyValuePair<string, object>("When", new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc)),
                new KeyValuePair<string, object>("List", new List<object> { "a", "b" }),
            };
            var o = Obj(MiniJson.Serialize(d));
            Assert.Equal("Zo\u00eb \"quoted\"", o["Name"]);
            Assert.Equal(true, o["On"]);
            Assert.Equal(false, o["Off"]);
            Assert.Null(o["Nothing"]);
            Assert.Equal(5.0, o["Count"]);
            Assert.Equal(new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc), MiniJson.ToDate(o["When"]));
            Assert.Equal(2, ((List<object>)o["List"]).Count);
        }

        [Fact] public void ToDate_Understands_Iso_Variants_And_Rejects_Junk()
        {
            Assert.Equal(new DateTime(2026, 11, 25, 8, 0, 0), MiniJson.ToDate("2026-11-25T08:00:00"));
            Assert.Equal(DateTimeKind.Utc, MiniJson.ToDate("2026-11-25T08:00:00Z").Value.Kind);
            Assert.Null(MiniJson.ToDate("not a date"));
            Assert.Null(MiniJson.ToDate(5.0));
            Assert.Null(MiniJson.ToDate(null));
        }

        [Fact] public void Helpers_Return_Defaults_On_WrongTypes()
        {
            var o = Obj("{\"s\":5,\"b\":\"yes\",\"x\":\"ok\",\"y\":true}");
            Assert.Null(MiniJson.GetString(o, "s"));
            Assert.Null(MiniJson.GetBool(o, "b"));
            Assert.Equal("ok", MiniJson.GetString(o, "x"));
            Assert.Equal(true, MiniJson.GetBool(o, "y"));
            Assert.Null(MiniJson.GetString(o, "missing"));
            Assert.Empty(MiniJson.GetStrings(o, "x"));
        }

        [Fact] public void GetStrings_Skips_NonStrings_And_Blank()
        {
            var o = Obj("{\"a\":[\"x\",1,\" \",null,\"y\"]}");
            Assert.Equal(new[] { "x", "y" }, MiniJson.GetStrings(o, "a").ToArray());
        }

        [Fact] public void GetInt_WholeNumberInRange_ReturnsIt()
        {
            var o = Obj("{\"a\":42,\"b\":0,\"c\":100,\"d\":1.0e2}");
            Assert.Equal(42, MiniJson.GetInt(o, "a", 0, 100));
            Assert.Equal(0, MiniJson.GetInt(o, "b", 0, 100));
            Assert.Equal(100, MiniJson.GetInt(o, "c", 0, 100));
            Assert.Equal(100, MiniJson.GetInt(o, "d", 0, 100));
        }

        [Fact] public void GetInt_Negative_Fraction_Huge_Text_Bool_Null_Missing_ReturnNull()
        {
            var o = Obj("{\"neg\":-5,\"frac\":12.7,\"huge\":1e99,\"text\":\"abc\",\"flag\":true,\"nothing\":null}");
            foreach (var k in new[] { "neg", "frac", "huge", "text", "flag", "nothing", "absent" })
                Assert.Null(MiniJson.GetInt(o, k, 0, 9999999));
            Assert.Null(MiniJson.GetInt(null, "x", 0, 1));
        }
    }
}
