using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The client's alterations, as its own data has them (datos/alterations_*.json,
    /// extract_alterations.py): what each one is under, and what it does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An alteration is a line of the alterations window: the Trool fair's potion sets 624 «Mamá de
    /// almendra» (a creature follows, one fight) and 625 «Influencia de Jibejan» (+50 initiative, one
    /// fight); the guild's oracle 859 (+150% experience for two hours); a dragobone, a pill, an
    /// uchronic elixir theirs. The item that gives one says so with effect 2892, the alteration as
    /// its first number, and 2893 takes one away.
    /// </para>
    /// <para>
    /// How long it lasts is one of its effects: 811 «#3 combates restantes», 817 «#3 victorias
    /// restantes», 2833 «Expiración: #1» with the minutes. Without any of them it lasts until
    /// something takes it away.
    /// </para>
    /// </remarks>
    public static class AlterationCatalogue
    {
        /// <summary>«#3 Combate(s) restante(s)»: the fights it lasts.</summary>
        public const int FightsLeft = 811;

        /// <summary>«#3 Victoria(s) restante(s)»: the fights won it lasts.</summary>
        public const int VictoriesLeft = 817;

        /// <summary>«Expiración: #1»: the minutes it lasts, and on the wire the date it ends.</summary>
        public const int Expiry = 2833;

        /// <summary>The «Golosina» category: the candies' alterations, with «Perlequín» and «Shigekax» under it.</summary>
        public const int CandyCategory = 39;

        /// <summary>One alteration: its category, its name and its effects, with the item effects' four numbers.</summary>
        public sealed record Definition(int Id, int Category, int NameId, string Criterion,
                                        IReadOnlyList<Equipment.ItemEffect> Effects);

        private sealed record Document(Dictionary<int, Definition> Alterations, Dictionary<int, int> Parents);

        private static Document? _document;
        private static readonly object _lock = new();

        private static Document Loaded
        {
            get
            {
                if (_document != null) return _document;
                lock (_lock)
                {
                    _document ??= Load(Paths.AlterationsJson);
                    return _document;
                }
            }
        }

        /// <summary>The alteration with that id, or null when the client does not have it.</summary>
        public static Definition? Of(int id) => Loaded.Alterations.TryGetValue(id, out var found) ? found : null;

        /// <summary>Whether a category is the one given or hangs from it, however far down.</summary>
        public static bool IsUnder(int category, int ancestor)
        {
            var parents = Loaded.Parents;
            for (int step = 0; step < 16 && category > 0; step++)
            {
                if (category == ancestor) return true;
                if (!parents.TryGetValue(category, out category)) return false;
            }
            return false;
        }

        /// <summary>Whether an alteration is a candy's: the «Golosina» category or one under it.</summary>
        public static bool IsCandy(int alteration)
            => Of(alteration) is { } found && IsUnder(found.Category, CandyCategory);

        private static Document Load(string path)
        {
            var alterations = new Dictionary<int, Definition>();
            var parents = new Dictionary<int, int>();
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Alterations] {path} not found: no item can give an alteration.");
                return new Document(alterations, parents);
            }

            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                if (json.RootElement.TryGetProperty("categories", out var categories))
                {
                    foreach (var category in categories.EnumerateObject())
                    {
                        if (int.TryParse(category.Name, out int id)) parents[id] = category.Value.GetInt32();
                    }
                }
                if (json.RootElement.TryGetProperty("alterations", out var list))
                {
                    foreach (var entry in list.EnumerateObject())
                    {
                        if (!int.TryParse(entry.Name, out int id)) continue;
                        var effects = new List<Equipment.ItemEffect>();
                        foreach (var effect in entry.Value.GetProperty("effects").EnumerateArray())
                        {
                            // [effect, diceNum, diceSide, value], as the client's EffectInstance has them.
                            effects.Add(new Equipment.ItemEffect(effect[0].GetInt32(), effect[3].GetInt64(),
                                                                 effect[1].GetInt64(), effect[2].GetInt64()));
                        }
                        alterations[id] = new Definition(id,
                            entry.Value.GetProperty("category").GetInt32(),
                            entry.Value.GetProperty("nameId").GetInt32(),
                            entry.Value.TryGetProperty("criterion", out var criterion) ? criterion.GetString() ?? "" : "",
                            effects);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Alterations] Could not read {Path.GetFileName(path)}: {ex.Message}");
            }

            Console.WriteLine($"[Alterations] {alterations.Count} alterations in {parents.Count} categories.");
            return new Document(alterations, parents);
        }
    }
}
