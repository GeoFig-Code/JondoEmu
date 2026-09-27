using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.World.Fights;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Koliseo's JondoBots: characters of a random class, level 200 and geared far beyond any
    /// set, played by the server's tactics, for the Koliseo's own "1v1 against a JondoBot" card.
    /// </summary>
    /// <remarks>
    /// Asked for as such (2026-09-27): a random class, always level 200, 12 AP, 6 MP, 1500 in
    /// every element and 6666 life. The rest of an "optimised level 200 set" is INFERRED and in
    /// the constants below: +6 range, 30 % critical, 20 % resistance in every element, and the
    /// initiative of four 1500s.
    ///
    /// A JondoBot is a fighter like a character -- shown with the character identity and the look
    /// of its class, one variant of each spell pair picked at random, every spell at its level-200
    /// grade, its class's passive and initial spells -- with no session and no row in
    /// Characters; <see cref="Fighter.IsBot"/> is what tells the fight to play it with the
    /// monsters' tactics (<see cref="MonsterTactics"/>), which weigh every spell it can pay for
    /// against every target and cell. Its ids sit far above any character's so nothing mistakes
    /// one for the other.
    /// </remarks>
    public static class KoliseoBots
    {
        public const int Level = 200;
        public const int ActionPoints = 12;
        public const int MovementPoints = 6;
        public const int Elements = 1500;
        public const int Life = 6666;
        public const int Range = 6;
        public const int Critical = 30;
        public const int Resistance = 20;

        /// <summary>
        /// Summons on top of the one every character has: what a summoner's optimized set of
        /// level 200 gives. Without them an Osamodas JondoBot had one creature out at a time.
        /// </summary>
        public const int Summons = 3;

        /// <summary>The first id a JondoBot gets; characters are nowhere near it.</summary>
        public const long FirstId = 900_000_000_000_000;

        /// <summary>The classes' names as the Spanish client writes them, for the bot's own.</summary>
        private static readonly Dictionary<int, string> ClassNames = new()
        {
            [1] = "Feca", [2] = "Osamodas", [3] = "Anutrof", [4] = "Sram", [5] = "Xelor", [6] = "Zurcarák",
            [7] = "Aniripsa", [8] = "Yopuka", [9] = "Ocra", [10] = "Sadida", [11] = "Sacrógrito",
            [12] = "Pandawa", [13] = "Tymador", [14] = "Zobal", [15] = "Steamer", [16] = "Selatrop",
            [17] = "Hipermago", [18] = "Uginak", [20] = "Forjalanza",
        };

        /// <summary>
        /// The looks a JondoBot of each class can wear: the notable NPCs of that class -- placed in
        /// the world, with something to say, and dressed (three skins or more), the class read off
        /// the body skin every humanoid look starts with. Built once, when first asked for.
        /// </summary>
        private static Dictionary<int, List<Npcs.Spawn>>? _npcLooks;
        private static readonly object _looksGate = new();

        /// <summary>The notable NPCs a JondoBot of this class can look like; empty when there are none.</summary>
        public static IReadOnlyList<Npcs.Spawn> NpcLooksOf(int breed)
        {
            lock (_looksGate)
            {
                // Not kept empty: asked before the NPCs are read, it is built again once they are.
                if (_npcLooks == null || (_npcLooks.Count == 0 && Npcs.Count > 0))
                {
                    var bodies = new Dictionary<long, int>();
                    foreach (int b in SpellTable.ClassBreeds)
                        for (int sex = 0; sex < 2; sex++)
                        {
                            var look = BreedLookTable.Get(b, sex);
                            if (look != null && look.Skins.Count > 0) bodies[look.Skins[0]] = b;
                        }
                    _npcLooks = new Dictionary<int, List<Npcs.Spawn>>();
                    var seen = new HashSet<int>();
                    foreach (var spawn in Npcs.AllSpawns)
                    {
                        if (spawn.Bones != 1 || spawn.Skins.Length < 3 || !seen.Add(spawn.NpcId)) continue;
                        if (!bodies.TryGetValue(spawn.Skins[0], out int of)) continue;
                        if ((Npcs.TemplateOf(spawn.NpcId)?.DialogMessageId ?? 0) == 0) continue;
                        if (!_npcLooks.TryGetValue(of, out var list)) _npcLooks[of] = list = new List<Npcs.Spawn>();
                        list.Add(spawn);
                    }
                }
                return _npcLooks.TryGetValue(breed, out var found) ? found : new List<Npcs.Spawn>();
            }
        }

        /// <summary>One JondoBot: who it is before it fights.</summary>
        public sealed class Spec
        {
            public long Id { get; init; }
            public int Breed { get; init; }
            public int Sex { get; init; }
            public string Name { get; init; } = "";
            public IReadOnlyDictionary<int, int> Choices { get; init; } = new Dictionary<int, int>();

            /// <summary>The NPC it looks like, if its class has a notable one; else its class's look.</summary>
            public Npcs.Spawn? LooksLike { get; init; }
        }

        private static readonly ConcurrentDictionary<long, Spec> _alive = new();
        private static long _lastId = FirstId;
        private static readonly Random _dice = new();

        /// <summary>Whether an id is a JondoBot's.</summary>
        public static bool IsBot(long id) => id >= FirstId;

        /// <summary>A JondoBot waiting for, or in, a fight.</summary>
        public static Spec? Of(long id) => _alive.TryGetValue(id, out var spec) ? spec : null;

        /// <summary>A new JondoBot, of a class drawn at random (or the one given).</summary>
        public static Spec Create(int breed = 0)
        {
            var classes = SpellTable.ClassBreeds;
            if (breed == 0 || !classes.Contains(breed))
            {
                lock (_dice) breed = classes.Count > 0 ? classes[_dice.Next(classes.Count)] : 8;
            }
            var choices = new Dictionary<int, int>();
            int sex;
            lock (_dice)
            {
                foreach (var pair in SpellTable.PairsOf(breed))
                    choices[pair.Id] = _dice.Next(2) == 0 ? pair.Base : pair.Variant;
                sex = _dice.Next(2);
            }
            var looks = NpcLooksOf(breed);
            Npcs.Spawn? npc = null;
            if (looks.Count > 0) lock (_dice) npc = looks[_dice.Next(looks.Count)];
            var spec = new Spec
            {
                Id = System.Threading.Interlocked.Increment(ref _lastId),
                Breed = breed,
                Sex = npc != null ? (BreedLookTable.Get(breed, 1)?.Skins.FirstOrDefault() == npc.Skins[0] ? 1 : 0) : sex,
                Name = "JondoBot " + (ClassNames.TryGetValue(breed, out var name) ? name : breed.ToString()),
                Choices = choices,
                LooksLike = npc,
            };
            _alive[spec.Id] = spec;
            return spec;
        }

        /// <summary>A JondoBot gone: its fight is over, or never was.</summary>
        public static void Forget(long id) => _alive.TryRemove(id, out _);

        /// <summary>The spells a JondoBot knows: a level-200 character's of its class, with its picks.</summary>
        public static List<(int Spell, int Grade)> SpellsOf(Spec spec)
            => SpellTable.KnownFor(spec.Breed, Level, spec.Choices).Select(k => (k.SpellId, k.Grade)).ToList();

        /// <summary>The JondoBot as a fighter, full of life and points, ready from the start.</summary>
        public static Fighter BuildFighter(Spec spec)
        {
            var fighter = new Fighter
            {
                Id = spec.Id,
                Name = spec.Name,
                Breed = spec.Breed,
                Sex = spec.Sex,
                Level = Level,
                MaxHP = Life,
                CurrentHP = Life,
                MaxAP = ActionPoints,
                CurrentAP = ActionPoints,
                MaxMP = MovementPoints,
                CurrentMP = MovementPoints,
                Strength = Elements,
                Intelligence = Elements,
                Chance = Elements,
                Agility = Elements,
                Initiative = Elements * 4,
                Range = Range,
                CriticalBonus = Critical,
                EarthResPct = Resistance,
                FireResPct = Resistance,
                WaterResPct = Resistance,
                AirResPct = Resistance,
                NeutralResPct = Resistance,
                LookBoneId = 744,
                IsMonster = false,
                IsBot = true,
                IsReady = true,
                BotLook = spec.LooksLike != null
                    ? Network.ConnectionProtocol.BuildNpcLook(spec.LooksLike.Bones, spec.LooksLike.Skins,
                                                              spec.LooksLike.Colors, spec.LooksLike.Scales)
                    : BreedLookTable.BuildLook(spec.Breed, spec.Sex, 0, null, spec.Id),
            };

            // The rest of the sheet as a character's is filled: flee and tackle a tenth of agility,
            // and the erosion every fighter starts with.
            fighter.Otras[78] = Elements / 10;
            fighter.Otras[79] = Elements / 10;
            fighter.Otras[75] = Fighter.ErosionBase;
            fighter.Otras[26] = Summons;

            var spells = SpellsOf(spec);
            foreach (var (spell, grade) in spells)
            {
                fighter.SpellIds.Add(spell);
                fighter.SpellGrades[spell] = grade;
            }

            // Its class's own, as a character's: the initial spells of its picks, the passive last.
            int passive = ClassPassives.PassiveOf(spec.Breed);
            foreach (var (spell, grade) in ClassPassives.ForFight(spec.Breed, spells))
                if (spell != passive) fighter.Buffs.PonerActitud(spell, grade);
            if (passive != 0) fighter.Buffs.PonerActitud(passive);
            return fighter;
        }
    }
}
