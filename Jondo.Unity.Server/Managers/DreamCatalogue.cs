using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// What a Dream Fountain sells and a Dream Favour gives: the rewards of
    /// <c>content/dreams/fountain.json</c>, drawn the way the guide tells.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fountain: "Dans chaque Fontaine Onirique, 5 choix vous seront proposés", and "la
    /// fontaine se réapprovisionne immédiatement après un achat (un nouveau choix prend la place
    /// de celui que vous venez d'acheter)". The first one, the fountain that closes band I --
    /// "profondeur 4" -- offers nothing dearer than fifteen points and no passive but Vent
    /// Arrière. The favour: two of its list and the small purse last.
    /// </para>
    /// <para>
    /// What may come out again is INFERRED where the guide is silent: a reward marked unique is
    /// never offered twice, nor a passive or a spell the dream already holds -- a second copy
    /// would do nothing -- but for the spells spent by charges, Oblivion and Domi Reversi, of
    /// which "si vous en récupérez plusieurs dans votre songe, vous pouvez en utiliser autant que
    /// vous en récupérerez". Every reward is as likely as any other: the guide gives no odds,
    /// and the one fountain captured -- two commons, three epics -- cannot tell.
    /// </para>
    /// </remarks>
    public static class DreamCatalogue
    {
        public const string AuthoredFile = "dreams/fountain.json";

        /// <summary>What a passive does beyond its spell: the side plays first, or is placed anywhere.</summary>
        public const string AlliesPlayFirst = "alliesPlayFirst";
        public const string PlaceAnywhere = "placeAnywhere";

        /// <summary>A reward of the file, before it is priced and its spell resolved.</summary>
        public sealed record Entry(int Reward, string Name, string Kind, int Rarity, int Spell, int Icon,
                                   IReadOnlyList<Dreams.Bono> Bonuses, int Sand, int Storms, int Levels,
                                   int Points, bool Unique, string Special);

        public const string Active = "active";
        public const string Passive = "passive";

        private static readonly object _lock = new();
        private static readonly Random _dice = new();
        private static Dictionary<int, Entry>? _rewards;
        private static List<(int Reward, int Price)> _fountain = new();
        private static List<int> _favour = new();
        private static HashSet<int> _firstPassives = new();
        private static int _firstMaxPrice = int.MaxValue;
        private static int _offers = 5;
        private static int _purse;

        /// <summary>A reward of the file, or null.</summary>
        public static Entry? EntryOf(int reward)
        {
            Load();
            return _rewards!.TryGetValue(reward, out var entry) ? entry : null;
        }

        /// <summary>The fountain's rewards and their prices, in the guide's order.</summary>
        public static IReadOnlyList<(int Reward, int Price)> Fountain
        {
            get { Load(); return _fountain; }
        }

        /// <summary>The favour's list, the purse left out.</summary>
        public static IReadOnlyList<int> Favour
        {
            get { Load(); return _favour; }
        }

        /// <summary>How many offers a fountain shows.</summary>
        public static int OffersPerFountain
        {
            get { Load(); return _offers; }
        }

        /// <summary>The favour's third choice, always: the small purse of ten dream points.</summary>
        public static Dreams.Reward Purse => RewardOf(PurseId)!;

        /// <summary>The purse's reward id: 143, "Bolsa de puntos de sueño".</summary>
        public static int PurseId
        {
            get { Load(); return _purse; }
        }

        /// <summary>
        /// A reward as it goes on the wire: its icon -- a spell's own for a spell or a passive --,
        /// its rarity, the price given, and its bonuses, the spell's last: the effect 3405 of the
        /// spell's grade 1 for an active, the 792 of the spell for a passive.
        /// </summary>
        public static Dreams.Reward? RewardOf(int reward, int price = 0)
        {
            var entry = EntryOf(reward);
            if (entry == null) return null;

            var bonuses = new List<Dreams.Bono>(entry.Bonuses);
            int icon = entry.Icon;
            if (entry.Spell != 0)
            {
                icon = DreamSpells.IconOf(entry.Spell);
                if (entry.Kind == Active) bonuses.Add(new Dreams.Bono(DreamSpells.LearnEffect, DreamSpells.FirstLevelOf(entry.Spell)));
                else if (entry.Kind == Passive) bonuses.Add(new Dreams.Bono(Dreams.PassiveEffect, entry.Spell, true));
            }

            return new Dreams.Reward
            {
                Id = icon,
                Tag = entry.Reward,
                Rarity = entry.Rarity,
                Price = price,
                Sand = entry.Sand,
                Storms = entry.Storms,
                Levels = entry.Levels,
                Points = entry.Points,
                Bonuses = bonuses,
            };
        }

        /// <summary>What a dream passive does beyond its spell, or "" -- see the file's special.</summary>
        public static string SpecialOf(int passiveSpell)
        {
            Load();
            foreach (var entry in _rewards!.Values)
                if (entry.Kind == Passive && entry.Spell == passiveSpell) return entry.Special;
            return "";
        }

        /// <summary>Whether a dream holds a passive that does <paramref name="special"/>.</summary>
        public static bool Holds(Dreams.Sueno? dream, string special)
            => dream != null && dream.Ganados.Any(b => b.Efecto == Dreams.PassiveEffect && SpecialOf(b.Valor) == special);

        /// <summary>A fountain's offers: as many as it shows, each one that may be offered there.</summary>
        public static List<Dreams.Reward> DrawFountain(Dreams.Sueno dream, Dreams.Sala room)
        {
            var drawn = new List<Dreams.Reward>();
            while (drawn.Count < OffersPerFountain)
            {
                var next = DrawOneForFountain(dream, room, drawn);
                if (next == null) break;
                drawn.Add(next);
            }
            return drawn;
        }

        /// <summary>
        /// The offer that takes the place of one bought: another of the fountain's, none of those
        /// still on show. Null when nothing is left to offer.
        /// </summary>
        public static Dreams.Reward? DrawOneForFountain(Dreams.Sueno dream, Dreams.Sala room,
                                                        IReadOnlyCollection<Dreams.Reward> onShow)
        {
            var candidates = Fountain.Where(f => onShow.All(o => o.Tag != f.Reward)
                                                 && MayBeOffered(dream, room, f.Reward, f.Price))
                                     .ToList();
            if (candidates.Count == 0) return null;
            (int Reward, int Price) pick;
            lock (_dice) pick = candidates[_dice.Next(candidates.Count)];
            return RewardOf(pick.Reward, pick.Price);
        }

        /// <summary>
        /// A favour's three choices: two of its list, different ones, that the dream may still
        /// take, and the purse last.
        /// </summary>
        public static List<Dreams.Reward> DrawFavour(Dreams.Sueno dream, int count)
        {
            var pool = Favour.Where(r => MayBeOffered(dream, null, r, 0)).ToList();
            var drawn = new List<Dreams.Reward>();
            lock (_dice)
            {
                while (drawn.Count < count && pool.Count > 0)
                {
                    int pick = pool[_dice.Next(pool.Count)];
                    pool.Remove(pick);
                    if (RewardOf(pick) is { } reward) drawn.Add(reward);
                }
            }
            drawn.Add(Purse);
            return drawn;
        }

        /// <summary>
        /// Whether a reward may be offered to this dream: never a unique one taken already, a
        /// passive held or a spell held -- unless it is spent by charges --, and at the first
        /// fountain nothing above its price nor a passive it does not list.
        /// </summary>
        public static bool MayBeOffered(Dreams.Sueno dream, Dreams.Sala? room, int reward, int price)
        {
            var entry = EntryOf(reward);
            if (entry == null) return false;
            if (entry.Unique && dream.RewardsTaken.Contains(reward)) return false;
            if (entry.Kind == Passive && dream.Ganados.Any(b => b.Efecto == Dreams.PassiveEffect && b.Valor == entry.Spell))
                return false;
            if (entry.Kind == Active && !DreamSpells.HasCharges(entry.Spell) && DreamSpells.Held(dream, entry.Spell) != null)
                return false;

            if (room != null && room.EsFuente && room.Franja == 1)
            {
                if (price > _firstMaxPrice) return false;
                if (entry.Kind == Passive && !_firstPassives.Contains(reward)) return false;
            }
            return true;
        }

        private static void Load()
        {
            if (_rewards != null) return;
            lock (_lock)
            {
                if (_rewards != null) return;
                var rewards = new Dictionary<int, Entry>();
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(Paths.ContentFile(AuthoredFile)));
                    var root = doc.RootElement;
                    foreach (var property in root.GetProperty("rewards").EnumerateObject())
                    {
                        int id = int.Parse(property.Name);
                        var row = property.Value;
                        var bonuses = new List<Dreams.Bono>();
                        if (row.TryGetProperty("bonuses", out var list))
                        {
                            foreach (var bonus in list.EnumerateArray())
                            {
                                bool nested = bonus.GetArrayLength() > 2 && bonus[2].ValueKind == JsonValueKind.True;
                                bonuses.Add(new Dreams.Bono(bonus[0].GetInt32(), bonus[1].GetInt32(), nested));
                            }
                        }
                        rewards[id] = new Entry(
                            id,
                            Text(row, "name"),
                            Text(row, "kind"),
                            Number(row, "rarity"),
                            Number(row, "spell"),
                            Number(row, "icon"),
                            bonuses,
                            Number(row, "sand"),
                            Number(row, "storms"),
                            Number(row, "levels"),
                            Number(row, "points"),
                            row.TryGetProperty("unique", out var unique) && unique.ValueKind == JsonValueKind.True,
                            Text(row, "special"));
                    }

                    _fountain = root.GetProperty("fountain").EnumerateObject()
                                    .Select(p => (int.Parse(p.Name), p.Value.GetInt32()))
                                    .ToList();
                    _favour = root.GetProperty("favour").EnumerateArray().Select(e => e.GetInt32()).ToList();
                    _firstPassives = root.GetProperty("firstFountainPassives").EnumerateArray()
                                         .Select(e => e.GetInt32()).ToHashSet();
                    _firstMaxPrice = root.GetProperty("firstFountainMaxPrice").GetInt32();
                    _offers = root.GetProperty("offers").GetInt32();
                    _purse = root.GetProperty("favourPurse").GetInt32();
                    Console.WriteLine($"[Dreams] {rewards.Count} rewards, {_fountain.Count} for the fountain and " +
                                      $"{_favour.Count} for the favour, from {AuthoredFile}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Dreams] Could not read {AuthoredFile}: {ex.Message}");
                }
                _rewards = rewards;
            }
        }

        private static string Text(JsonElement row, string name)
            => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

        private static int Number(JsonElement row, string name)
            => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
    }
}
