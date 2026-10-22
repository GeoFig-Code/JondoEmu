using Jondo.Unity.Launcher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The idle animations of the client's IdlesDataRoot (datos/idles.json, written by
    /// tools/extract_idles.py): what the appearance window's idles tab offers.
    /// </summary>
    /// <remarks>
    /// AppearanceIdlesTab.KnownIdle takes an idle whose «known» is set with nothing unlocked: each
    /// breed's «AnimStatiqueExploRetro», behind its breed criterion. Id 3 is the plain idle, no
    /// animation of its own, the one to go back to.
    /// </remarks>
    public static class IdleTable
    {
        /// <summary>The plain idle: no animation of its own.</summary>
        public const int Plain = 3;

        private sealed class Idle
        {
            public int Breed { get; init; }
            public bool Known { get; init; }
        }

        private static readonly Lazy<Dictionary<int, Idle>> _idles = new(Load);

        private static Dictionary<int, Idle> Load()
        {
            var idles = new Dictionary<int, Idle>();
            string path = Paths.IdlesJson;
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Idles] {Path.GetFileName(path)} is missing; nobody can change idle. " +
                                  "Generate it with tools/extract_idles.py.");
                return idles;
            }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var entry in doc.RootElement.GetProperty("idles").EnumerateObject())
                {
                    if (!int.TryParse(entry.Name, out int id)) continue;
                    idles[id] = new Idle
                    {
                        Breed = entry.Value.GetProperty("breed").GetInt32(),
                        Known = entry.Value.GetProperty("known").GetInt32() != 0,
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Idles] Could not read {Path.GetFileName(path)}: {ex.Message}");
            }
            return idles;
        }

        /// <summary>Whether that breed may take that idle with nothing unlocked: the plain one, or its own known one.</summary>
        public static bool OfferedFreely(int idleId, int breedId)
            => idleId == Plain && _idles.Value.ContainsKey(Plain)
               || _idles.Value.TryGetValue(idleId, out var idle) && idle.Known && idle.Breed == breedId;
    }
}
