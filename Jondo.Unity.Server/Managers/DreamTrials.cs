using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Infinite Dreams' trials, as <c>content/dreams/trials.json</c> writes them: the dream
    /// hub's «Pruebas» tab starts each from its seed, as a code, at its own difficulty; and each
    /// changes the dream's fights its own way -- monsters with more life, a spell every fighter
    /// holds, a monster every player fights as, the monsters of the rooms.
    /// </summary>
    /// <remarks>See the file's own notes for what comes from the client and what from the guide.</remarks>
    public static class DreamTrials
    {
        public const string AuthoredFile = "dreams/trials.json";

        /// <summary>One trial: see the file.</summary>
        public sealed record Trial(int Id, string Seed, int Difficulty, int Achievement, int Incarnation,
                                   int EveryoneCarries, int MonsterLifePercent, IReadOnlyList<int> Enemies,
                                   IReadOnlyList<int> RareEnemies, IReadOnlyList<int> LateEnemies);

        /// <summary>One draw in this many is a rare enemy (INFERRED: the guide only says «rarer»).</summary>
        public const int RareOneIn = 10;

        /// <summary>The band from which the late enemies come («palier IV-V hard rooms»).</summary>
        public const int LateFromBand = 4;

        private static readonly object _lock = new();
        private static List<Trial>? _trials;

        /// <summary>Every trial of the file.</summary>
        public static IReadOnlyList<Trial> All
        {
            get { Load(); return _trials!; }
        }

        /// <summary>The trial a seed starts, or null.</summary>
        public static Trial? BySeed(string? seed)
            => string.IsNullOrEmpty(seed) ? null
               : All.FirstOrDefault(t => string.Equals(t.Seed, seed, StringComparison.OrdinalIgnoreCase));

        /// <summary>The trial a dream is: one started from a trial's seed, or null.</summary>
        public static Trial? Of(Dreams.Sueno? dream)
            => dream != null && dream.FromSouvenir ? BySeed(dream.Seed) : null;

        /// <summary>
        /// A room's monster of a trial with its own enemies: a rare one now and then, a late one from
        /// <see cref="LateFromBand"/> on and in the last room, the usual ones otherwise.
        /// </summary>
        public static int DrawEnemy(Trial trial, int band, bool lastRoom, Random dice)
        {
            var pool = new List<int>(trial.Enemies);
            if (lastRoom || band >= LateFromBand) pool.AddRange(trial.LateEnemies);
            if (trial.RareEnemies.Count > 0 && dice.Next(RareOneIn) == 0)
                return trial.RareEnemies[dice.Next(trial.RareEnemies.Count)];
            return pool.Count == 0 ? 0 : pool[dice.Next(pool.Count)];
        }

        private static void Load()
        {
            if (_trials != null) return;
            lock (_lock)
            {
                if (_trials != null) return;
                var trials = new List<Trial>();
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(Paths.ContentFile(AuthoredFile)));
                    foreach (var row in doc.RootElement.GetProperty("trials").EnumerateArray())
                    {
                        trials.Add(new Trial(
                            Number(row, "id"),
                            row.TryGetProperty("seed", out var seed) ? seed.GetString() ?? "" : "",
                            Number(row, "difficulty"),
                            Number(row, "achievement"),
                            Number(row, "incarnation"),
                            Number(row, "everyoneCarries"),
                            Number(row, "monsterLifePercent"),
                            Numbers(row, "enemies"),
                            Numbers(row, "rareEnemies"),
                            Numbers(row, "lateEnemies")));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Dreams] Could not read {AuthoredFile}: {ex.Message}");
                }
                _trials = trials;
            }
        }

        private static int Number(JsonElement row, string name)
            => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

        private static IReadOnlyList<int> Numbers(JsonElement row, string name)
            => row.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(v => v.GetInt32()).ToList()
                : Array.Empty<int>();
    }
}
