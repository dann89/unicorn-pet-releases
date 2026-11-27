using System;
using System.Collections.Generic;
using System.IO;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class MessageBankTests
    {
        static string Tmp(string content)
        {
            var p = Path.Combine(Path.GetTempPath(), "up-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(p, content);
            return p;
        }

        [Fact]
        public void Load_MissingFile_ReturnsDefault()
        {
            var b = MessageBank.Load(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".json"));
            Assert.Equal(MessageBank.Default.Scheduled.Count, b.Scheduled.Count);
            Assert.NotEmpty(b.Random);
        }

        [Fact]
        public void Load_CorruptJson_ReturnsDefault()
        {
            var b = MessageBank.Load(Tmp("{ not json ]]"));
            Assert.Equal(MessageBank.Default.Scheduled.Count, b.Scheduled.Count);
            Assert.NotEmpty(b.Cheeky);
        }

        [Fact]
        public void Load_EmptyPools_FallsBackToDefaultPool()
        {
            var b = MessageBank.Load(Tmp("{\"name\":\"Alex\",\"scheduled\":[{\"id\":\"start\",\"time\":\"08:00\",\"pool\":[]}],\"random\":[],\"cheeky\":[]}"));
            Assert.Equal("Alex", b.Name);
            Assert.All(b.Scheduled, s => Assert.NotEmpty(s.Pool));
            Assert.NotEmpty(b.Random);
            Assert.NotEmpty(b.Cheeky);
        }

        [Fact]
        public void Load_ParsesFiveScheduledSlots()
        {
            var s = MessageBank.Default.Scheduled;
            Assert.Equal(5, s.Count);
            Assert.Equal(new[]
            {
                new TimeSpan(8, 0, 0), new TimeSpan(12, 45, 0), new TimeSpan(14, 0, 0),
                new TimeSpan(16, 50, 0), new TimeSpan(17, 0, 0)
            }, new List<TimeSpan> { s[0].Time, s[1].Time, s[2].Time, s[3].Time, s[4].Time });
            Assert.All(s, x => Assert.True(x.Pool.Count >= 6));
            Assert.True(MessageBank.Default.Random.Count >= 25);
            Assert.True(MessageBank.Default.Cheeky.Count >= 15);
        }
    }
}
