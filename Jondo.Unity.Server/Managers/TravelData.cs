using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// Where the travel items go, as the client's own data says (datos/travel_*.json,
    /// extract_travel.py): its map references and its choices.
    /// </summary>
    /// <remarks>
    /// A potion with effect 826 or 1155 carries a map reference, not a map: 189 is Bonta's square
    /// (217318404, cell 395), 282 Brakmar's. The captures land where the reference says -- Otomai's
    /// 431 on 28049408, Meriana's 193 on 134403. An ankla carries a choice instead (effect 3008):
    /// Anutropía's 28 offers three dungeons and «No teletransportarse», and its Palacio del Rey
    /// Nidas (670) is the 129499136 the capture goes to.
    /// </remarks>
    public static class TravelData
    {
        /// <summary>One option of a choice: what it reads as and what choosing it gives.</summary>
        public sealed record ChoiceOption(int Id, int DescriptionId, int GfxId, int RewardType, string RewardValue);

        /// <summary>A choice the client draws as a popup of options (kss).</summary>
        public sealed record Choice(int Id, int NameId, IReadOnlyList<ChoiceOption> Options);

        /// <summary>An option's reward: go to a map reference.</summary>
        public const int GoToMapReference = 6;

        /// <summary>An option's reward: the item itself, which is how «No teletransportarse» keeps it.</summary>
        public const int KeepTheItem = 4;

        private sealed record Document(Dictionary<int, (long Map, int Cell)> References, Dictionary<int, Choice> Choices);

        private static Document _document;
        private static readonly object _lock = new();

        private static Document Loaded
        {
            get
            {
                if (_document != null) return _document;
                lock (_lock)
                {
                    _document ??= Load(Paths.TravelJson);
                    return _document;
                }
            }
        }

        private static Document Load(string path)
        {
            var references = new Dictionary<int, (long, int)>();
            var choices = new Dictionary<int, Choice>();
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Travel] {path} not found: no travel item knows where it goes.");
                return new Document(references, choices);
            }

            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.TryGetProperty("mapReferences", out var refs))
            {
                foreach (var reference in refs.EnumerateObject())
                {
                    if (!int.TryParse(reference.Name, out int id)) continue;
                    references[id] = (reference.Value.GetProperty("map").GetInt64(),
                                      reference.Value.GetProperty("cell").GetInt32());
                }
            }
            if (json.RootElement.TryGetProperty("choices", out var list))
            {
                foreach (var entry in list.EnumerateObject())
                {
                    if (!int.TryParse(entry.Name, out int id)) continue;
                    var options = new List<ChoiceOption>();
                    foreach (var option in entry.Value.GetProperty("options").EnumerateArray())
                    {
                        options.Add(new ChoiceOption(option.GetProperty("id").GetInt32(),
                                                     option.GetProperty("descriptionId").GetInt32(),
                                                     option.GetProperty("gfxId").GetInt32(),
                                                     option.GetProperty("rewardType").GetInt32(),
                                                     option.GetProperty("rewardValue").GetString() ?? ""));
                    }
                    choices[id] = new Choice(id, entry.Value.GetProperty("nameId").GetInt32(), options);
                }
            }
            Console.WriteLine($"[Travel] {references.Count} map references and {choices.Count} choices.");
            return new Document(references, choices);
        }

        /// <summary>The map and cell a reference stands for.</summary>
        public static bool TryMapReference(int id, out long mapId, out int cellId)
        {
            if (Loaded.References.TryGetValue(id, out var where))
            {
                (mapId, cellId) = where;
                return true;
            }
            mapId = 0;
            cellId = 0;
            return false;
        }

        /// <summary>A choice by its id, or null.</summary>
        public static Choice ChoiceOf(int id) => Loaded.Choices.TryGetValue(id, out var choice) ? choice : null;
    }
}
