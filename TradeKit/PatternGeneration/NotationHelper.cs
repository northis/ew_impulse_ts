using System.Collections.Generic;
using System.Linq;
using TradeKit.Impulse;

namespace TradeKit.PatternGeneration
{
    public static class NotationHelper
    {
        private static readonly Dictionary<string, string> ROME_MAP = new()
        {
            {PatternGenerator.IMPULSE_ONE, "i"},
            {PatternGenerator.IMPULSE_TWO, "ii"},
            {PatternGenerator.IMPULSE_THREE, "iii"},
            {PatternGenerator.IMPULSE_FOUR, "iv"},
            {PatternGenerator.IMPULSE_FIVE, "v"},
        };

        /// <summary>
        /// Gets the proper Elliott Waves notation data for charts.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="level">The level.</param>
        /// <returns>The notation data.</returns>
        public static NotationItem[] GetNotation(ElliottModelType type, byte level)
        {
            string[] keys = PatternGenerator.ModelRules[type].Models.Keys.ToArray();
            NotationItem[] res = new NotationItem[keys.Length];

            for (int i = 0; i < res.Length; i++)
            {
                string key = keys[i];
                string keyView = key;
                byte size = 1;

                switch (level % 15)
                {
                    case 0:// Minuscule
                        keyView = key.ToUpperInvariant();
                        break;
                    case 1:// Submicro
                        keyView = $"({key.ToUpperInvariant()})";
                        break;
                    case 2:// Micro
                        keyView = $"[{key.ToUpperInvariant()}]";
                        break;
                    case 3:// Subminuette
                        size = 2;
                        if (ROME_MAP.ContainsKey(key)) 
                            keyView = ROME_MAP[key];
                        break;
                    case 4:// Minuette
                        size = 2;
                        keyView = ROME_MAP.ContainsKey(key) 
                            ? $"({ROME_MAP[key]})" 
                            : $"({key})";
                        break;
                    case 5:// Minute
                        size = 2;
                        keyView = ROME_MAP.ContainsKey(key)
                            ? $"[{ROME_MAP[key]}]"
                            : $"[{key}]";
                        break;
                    case 6:// Minor
                        size = 3;
                        keyView = key.ToUpperInvariant();
                        break;
                    case 7:// Intermediate
                        size = 3;
                        keyView = $"({key.ToUpperInvariant()})";
                        break;
                    case 8:// Primary
                        size = 3;
                        keyView = $"[{key.ToUpperInvariant()}]";
                        break;
                    case 9:// Cycle
                        size = 4;
                        if (ROME_MAP.ContainsKey(key))
                            keyView = ROME_MAP[key].ToUpperInvariant();
                        break;
                    case 10:// Supercycle
                        size = 4;
                        keyView = ROME_MAP.ContainsKey(key)
                            ? $"({ROME_MAP[key].ToUpperInvariant()})"
                            : $"({key})";
                        break;
                    case 11:// Grand supercycle
                        size = 4;
                        keyView = ROME_MAP.ContainsKey(key)
                            ? $"[{ROME_MAP[key].ToUpperInvariant()}]"
                            : $"[{key}]";
                        break;
                    case 12:// Submillenium
                        size = 5;
                        keyView = key.ToUpperInvariant();
                        break;
                    case 13:// Millenium
                        size = 5;
                        keyView = $"({key.ToUpperInvariant()})";
                        break;
                    case 14:// Supermillenium
                        size = 5;
                        keyView = $"[{key.ToUpperInvariant()}]";
                        break;
                }

                res[i] = new NotationItem(type, level, key, keyView, size);
            }
            return res;
        }
    }
}
