using System;
using System.Collections.Generic;
using System.Linq;

namespace UnicornPet.Core
{
    public class MessagePicker
    {
        readonly Random _rng;
        public MessagePicker(Random rng) { _rng = rng; }

        public string Pick(IReadOnlyList<string> pool, string avoid)
        {
            if (pool == null || pool.Count == 0) return "";
            if (pool.Count == 1) return pool[0];
            var candidates = pool.Where(p => p != avoid).ToList();
            if (candidates.Count == 0) candidates = pool.ToList();
            return candidates[_rng.Next(candidates.Count)];
        }

        string _lastRandom;

        public string PickRandom(MessageBank bank, bool cheeky)
        {
            IReadOnlyList<string> pool = cheeky && bank.Cheeky.Count > 0 ? bank.Random.Concat(bank.Cheeky).ToList() : bank.Random;
            var text = Pick(pool, _lastRandom);
            _lastRandom = text;
            return Substitute(text, bank.Name);
        }

        public static string Substitute(string text, string name)
        {
            if (text == null) return "";
            if (!string.IsNullOrWhiteSpace(name)) return text.Replace("{name}", name.Trim());
            return text.Replace(", {name}", "").Replace("{name}, ", "").Replace(" {name}", "").Replace("{name}", "");
        }
    }
}
