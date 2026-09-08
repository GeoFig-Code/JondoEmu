using System.Collections.Generic;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>What a bomb does, straight from the client's own table.</summary>
    public sealed class BombData
    {
        /// <summary>The spell the bomb casts when something sets it off.</summary>
        public int Explosion { get; init; }

        /// <summary>What the caster throws instead when the target cell is taken.</summary>
        public int OnTarget { get; init; }

        /// <summary>What an explosion chains into the bombs it reaches.</summary>
        public int ChainReaction { get; init; }

        /// <summary>Which wall it raises. The wall table itself is not in the dump.</summary>
        public int Wall { get; init; }

        public int ComboCoefficient { get; init; }
    }

    /// <summary>
    /// The bombs, read from the client instead of written down.
    /// </summary>
    /// <remarks>
    /// THIS REPLACED TWO HAND-WRITTEN TABLES, and the story is worth keeping because the mistake
    /// was mine. Wiring up effect 1009 I needed to know which spell each bomb casts when it goes
    /// off, looked for the join in world.db, did not find one -- the bombs' <c>spells</c> array is
    /// empty and no effect anywhere names spell 13455 -- and concluded there wasn't one. So I
    /// measured it off 96 detonations in the Wireshark captures and wrote the four rows down.
    ///
    /// The rows were right and the conclusion was wrong. The table is in the client, in a class
    /// called <c>SpellBombData</c>, dumped to <c>dofus3_data/bomb_spells.json</c>; I had only
    /// looked in the database. <c>tools/extract_bomb_spells.py</c> turns it into
    /// <c>datos/bombas.json</c> and this reads that.
    ///
    /// What the swap bought, beyond deleting eight literals: it carries TEN bombs and not the four
    /// the Rogue throws, and two fields the measurement never gave me -- the chain-reaction spell
    /// and a wall id.
    /// </remarks>
    public static class Bombs
    {
        private static readonly object _lock = new();
        private static Dictionary<int, BombData> _all;

        /// <summary>Every bomb the client knows, by monster template.</summary>
        public static IReadOnlyDictionary<int, BombData> All
        {
            get { Load(); return _all; }
        }

        public static bool Is(int template) => All.ContainsKey(template);

        public static BombData Of(int template)
            => All.TryGetValue(template, out var data) ? data : null;

        /// <summary>The spell this bomb casts when it is set off, or zero.</summary>
        public static int Explosion(int template) => Of(template)?.Explosion ?? 0;

        /// <summary>The spell thrown when the target cell is already taken, or zero.</summary>
        public static int OnTarget(int template) => Of(template)?.OnTarget ?? 0;

        internal static void Forget()
        {
            lock (_lock) { _all = null; }
        }

        private static void Load()
        {
            lock (_lock)
            {
                if (_all != null) return;
                var table = new Dictionary<int, BombData>();

                try
                {
                    string path = Paths.BombsJson;
                    if (System.IO.File.Exists(path))
                    {
                        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
                        foreach (var entry in doc.RootElement.EnumerateObject())
                        {
                            if (!int.TryParse(entry.Name, out int template)) continue;
                            table[template] = new BombData
                            {
                                Explosion = Number(entry.Value, "explosion"),
                                OnTarget = Number(entry.Value, "alObjetivo"),
                                ChainReaction = Number(entry.Value, "cadena"),
                                Wall = Number(entry.Value, "muro"),
                                ComboCoefficient = Number(entry.Value, "coeficienteDeCombo"),
                            };
                        }
                    }
                    else
                    {
                        Program.LogDebug($"[Bombas] No está {path}; ninguna bomba explotará.");
                    }
                }
                catch (System.Exception ex)
                {
                    Program.LogDebug($"[Bombas] Tabla ilegible: {ex.Message}");
                }

                _all = table;
                Program.LogDebug($"[Bombas] {table.Count} bomba(s) leídas del cliente.");
            }
        }

        private static int Number(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.TryGetInt32(out int n) ? n : 0;
    }
}
