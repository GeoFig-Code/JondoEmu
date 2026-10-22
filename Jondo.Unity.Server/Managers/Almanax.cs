using System;
using System.Collections.Generic;
using Jondo.Unity.World.Almanax;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Almanax: today's saint, today's offering, today's bonuses.
    /// </summary>
    /// <remarks>
    /// <b>INFERRED throughout.</b> No capture visits the sanctuary, so nothing here is checked
    /// against the real server's traffic. What is here is what the client's own data defines, run
    /// through the quest engine that already exists:
    ///
    /// <list type="bullet">
    ///   <item>Each of the 376 calendar entries leads to an ordinary quest, "Ofrenda para
    ///   &lt;saint&gt;", whose start condition is <c>PL&gt;19&amp;Ad=&lt;entry&gt;</c>. <c>Ad</c> is
    ///   answered here: the entry of today's date.</item>
    ///   <item>All 376 are handed out by Ontoral Zo (NPC 1625) in the sanctuary, map 101450251,
    ///   and none of their steps names a line of his to hand it over on. So the rule the quest
    ///   engine applies to an NPC with no written tree — the opening line hands over what can be
    ///   taken — is applied to him for today's offering.</item>
    ///   <item>The four objectives are the same for every day: bring the offering to Ontoral, pray
    ///   at the altar (free text, which the client reports), see the saint, go back to Ontoral.
    ///   The quest engine already closes each of those kinds.</item>
    ///   <item>An offering is made once a day: finishing it writes the day down, and the same
    ///   quest cannot be taken again that day.</item>
    /// </list>
    ///
    /// Every type of the days' bonuses is applied, to everybody: they hang on the date, not on
    /// having made the offering (see <see cref="BonusType"/>). Their conditions are read the way the
    /// days' own descriptions word them (<see cref="Fits"/>).
    ///
    /// And «El año del dragón» (<see cref="Dragon"/>): while it is in hand, each offering made gives
    /// a page of the calendar, and the 365 pages taken to Ontoral give the Dolmanax.
    /// </remarks>
    public static class Almanax
    {
        /// <summary>The operator the offering quests' start conditions use for the day.</summary>
        public const string DayOperator = "Ad";

        /// <summary>The tally kind the day of the last offering is written under.</summary>
        public const string OfferingKind = "Ax";

        /// <summary>
        /// Ontoral Zo, who hands out every one of the 376 offerings in the client's data. Not
        /// written here: whoever the offering quests themselves name as their giver, worked out
        /// the first time it is asked once both the calendar and the quests are loaded.
        /// </summary>
        public static int Giver
        {
            get
            {
                if (_giver == 0) _giver = FindGiver();
                return _giver;
            }
        }

        private static int _giver;

        private static int FindGiver()
        {
            var calendar = _calendar;
            var book = Quests.Book;
            if (calendar == null || !calendar.Ready || book == null) return 0;

            var givers = new Dictionary<int, int>();
            foreach (var quest in book.All())
            {
                if (!calendar.IsOffering(quest.Id)) continue;
                foreach (var giver in quest.Givers)
                {
                    givers.TryGetValue(giver.NpcId, out int n);
                    givers[giver.NpcId] = n + 1;
                }
            }

            int best = 0;
            foreach (var (npc, n) in givers)
            {
                if (best == 0 || n > givers[best]) best = npc;
            }

            return best;
        }

        /// <summary>
        /// «El año del dragón»: the quest, the calendar page and how many it takes. Read off the
        /// data rather than written here: the one quest Ontoral hands out that is not an offering and
        /// asks for an item brought back to him by the hundreds -- 954, 365 of item 13345.
        /// </summary>
        /// <remarks>
        /// The client's own words for it: «Cada vez que pases por el santuario del Almanax, podrás
        /// obtener una página del calendario si celebras el día del santo. Cuando tengas 365, Ontoral
        /// Zo te recompensará con un dolmanax» (its step's description), and the page's: «Ontoral Zo
        /// te dará una página de calendario cada día que pase». No reward of the data gives the page:
        /// it is handed over here when an offering is finished with the quest in hand.
        /// </remarks>
        public static (int Quest, int Page, int Pages) Dragon
        {
            get
            {
                if (_dragon.Quest == 0) _dragon = FindDragon();
                return _dragon;
            }
        }

        private static (int Quest, int Page, int Pages) _dragon;

        /// <summary>
        /// The Dolmanax, which «El año del dragón» pays. Its step's reward in the data carries only
        /// experience; the item is the one Ontoral names as he hands it over -- «Toma este dolmanax,
        /// este auténtico huevo de dragón» (his line 16041) -- and the only Dofus called so.
        /// </summary>
        public const int Dolmanax = 13344;

        private static (int, int, int) FindDragon()
        {
            var calendar = _calendar;
            var book = Quests.Book;
            int giver = Giver;
            if (calendar == null || !calendar.Ready || book == null || giver == 0) return (0, 0, 0);

            foreach (var quest in book.All())
            {
                if (calendar.IsOffering(quest.Id) || !quest.Givers.Any(g => g.NpcId == giver)) continue;
                foreach (var step in quest.Steps)
                    foreach (var objective in step.Objectives)
                        if (objective.ConsumesItems && objective.NpcId == giver && objective.ItemCount >= 300)
                            return (quest.Id, objective.ItemId, objective.ItemCount);
            }
            return (0, 0, 0);
        }

        private static volatile AlmanaxCalendar? _calendar;
        private static readonly object _loadLock = new object();

        /// <summary>The server's clock. Replaceable so the tests can pick a day.</summary>
        public static Func<DateTime> Clock { get; set; } = () => DateTime.Now;

        /// <summary>Reads the calendar. Once, at startup, after the quests.</summary>
        public static void Load()
        {
            if (_calendar != null) return;
            lock (_loadLock)
            {
                if (_calendar != null) return;
                var calendar = new AlmanaxCalendar(Console.WriteLine);
                _calendar = calendar;

                var today = Today();
                if (today != null)
                {
                    Console.WriteLine($"[Almanax] {calendar.Count} days in the calendar; today is {today.Id}, " +
                                      $"offering in quest {today.QuestId}, saint {today.Saint}, " +
                                      $"{today.Bonuses.Count} bonuses not applied. Ontoral hands it over (NPC {Giver}).");
                }
            }
        }

        /// <summary>
        /// Puts the Almanax back to not loaded, for the tests: a calendar left behind by one test
        /// would put a day's bonus on the job experience another test measures.
        /// </summary>
        internal static void Reset()
        {
            lock (_loadLock)
            {
                _calendar = null;
                _giver = 0;
            }
        }

        /// <summary>Today's calendar entry, or null without a calendar.</summary>
        public static AlmanaxDay? Today() => _calendar?.DayOf(Clock());

        /// <summary>Whether an NPC is the saint of some day: only today's stands (AlmanaxTemple).</summary>
        public static bool IsSaint(int npcId) => _calendar?.IsSaint(npcId) == true;

        /// <summary>
        /// What <c>Ad</c> answers: today's entry. Null — the term left unjudged — without a calendar.
        /// </summary>
        public static long? Scalar(string op)
        {
            if (op != DayOperator) return null;
            var today = Today();
            return today?.Id;
        }

        /// <summary>
        /// The bonus types applied here, each named by the one day whose text says what it does
        /// and has no condition attached. The client data has only the number; the meaning is read
        /// off that day's own description.
        /// </summary>
        public static class BonusType
        {
            /// <summary>Day 129: "la ganancia de experiencia aumenta un 25% para todos los oficios".</summary>
            public const int JobExperience = 2;

            /// <summary>Day 277: "La ganancia de experiencia aumenta un 100% para todas la misiones".</summary>
            public const int QuestExperience = 14;

            /// <summary>Day 250: "La ganancia de kamas aumentará en un 100% en todas las misiones".</summary>
            public const int QuestKamas = 15;

            /// <summary>"La ganancia de experiencia aumenta un 100% en la mazmorra de la Cámara de los Maleficios".</summary>
            public const int MonsterExperience = 1;

            /// <summary>"Las posibilidades de conseguir un botín aumentan en un 100% en la mazmorra ...".</summary>
            public const int MonsterDrops = 16;

            /// <summary>"Los bonus que se consiguen al superar un reto aumentan en un 75%".</summary>
            public const int ChallengeBonus = 17;

            /// <summary>"La cantidad de fichas que se gana en el koliseo aumentará en un 100%".</summary>
            public const int KoliseoTokens = 25;

            /// <summary>"La ganancia de experiencia aumenta en un 75% con los combates del koliseo".</summary>
            public const int KoliseoExperience = 28;

            /// <summary>"las curas con consumibles (vida y energía) aumenta un 100%": the life (21) and the energy (32).</summary>
            public const int ConsumableLife = 21, ConsumableEnergy = 32;

            /// <summary>"La cantidad de peces que consiguen los pescadores ... aumenta un 100%".</summary>
            public const int HarvestQuantity = 7;

            /// <summary>"Los campesinos ahorran un 15% de ingredientes cuando fabrican objetos".</summary>
            public const int IngredientsSaved = 10;

            /// <summary>"Por cada objeto que fabriquen, los campesinos tendrán un 25% de posibilidades de crear un segundo objeto".</summary>
            public const int SecondItem = 19;

            /// <summary>"Los aventureros tienen un 20% de probabilidad de generar un segundo botín con todos los monstruos de las mazmorras".</summary>
            public const int SecondLoot = 35;

            /// <summary>"El botín que generan los recaudadores aumenta un 200% en las mazmorras de nivel 160 a 190".</summary>
            public const int TaxCollectorLoot = 6;

            /// <summary>"Los escultomagos tienen un 15% de probabilidad de no consumir ninguna runa".</summary>
            public const int RuneKept = 31;

            /// <summary>"La velocidad de aparición de madera de todo tipo aumenta en un 100%" (by area, subarea or job).</summary>
            public const int ResourceRespawn = 8;

            /// <summary>"La regeneración de puntos de vida aumenta un 300% (fuera de combate)".</summary>
            public const int LifeRegeneration = 20;

            /// <summary>"Se propondrá un reto extra contra los monstruos presentes en la zona ...": its amount, 1, is the count.</summary>
            public const int ExtraChallenge = 5;

            /// <summary>"El efecto de los acariciadores se duplica en los cercados": its 16 is the paddock gauge (2 the patter, 4 the trough).</summary>
            public const int PaddockItems = 36;

            /// <summary>"Las monturas que nazcan este día tendrán la capacidad Resistente": its 17 names the capacity.</summary>
            public const int NewbornCapacity = 37;

            /// <summary>
            /// Day 292: «Los protectores de recursos tienen 5 veces más de posibilidades de aparecer
            /// durante la actividad de recolección» (400) -- and «la velocidad de aparición de los
            /// archimonstruos aumenta un 200%», type 18 (200).
            /// </summary>
            public const int ResourceProtectors = 9, Archmonsters = 18;

            /// <summary>
            /// Day 140: «La calidad de los objetos fabricados en la ciudad de Bonta será mayor» (20),
            /// and six other cities by their area or subarea. INFERRED: each characteristic rolled
            /// comes that percent of the way from its roll to its best (Forgemagic.Roll).
            /// </summary>
            public const int CraftQuality = 11;

            /// <summary>Day 341: «La ganancia de experiencia de las mascotas aumenta un 50%».</summary>
            public const int PetExperience = 30;

            /// <summary>Day 51: «La velocidad de aparición de los monstruos de Se busca aumenta un 200%», by race.</summary>
            public const int WantedSpawn = 3;

            /// <summary>"En los apareamientos de monturas, se añade un bonus del 20% a la probabilidad de obtener la generación objetivo".</summary>
            public const int TargetGeneration = 38;

            /// <summary>
            /// Thirteen days: «La cantidad de carne tierna que consiguen los cazadores aumenta un
            /// 125%», the meats named one by one (kind 9, an item obtained); 08/10 names all forty.
            /// </summary>
            public const int HunterMeat = 26;

            /// <summary>04/07: «se añade un bonus del 75% a las posibilidades de conseguir botín en los Sueños».</summary>
            public const int DreamDrops = 34;
        }

        /// <summary>
        /// What a bonus's conditions ask about, as <see cref="Fits"/> reads them: where it is (area,
        /// subarea, a dungeon), whom the fight is against (monsters and their races), and what the
        /// character wears.
        /// </summary>
        public sealed record Where(int Area, int Subarea, IReadOnlyCollection<int> Monsters, IReadOnlyCollection<int> Races,
                                   IReadOnlyCollection<int> Worn, bool InDungeon, int Job = 0, int Skill = 0,
                                   int PaddockGauge = 0, int Item = 0)
        {
            public static readonly Where Anywhere = new(-1, 0, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), false);

            /// <summary>An item obtained: the hunters' meats.</summary>
            public static Where Obtaining(int item) => Anywhere with { Item = item };

            /// <summary>A job's work: its experience, a harvest by one of its skills.</summary>
            public static Where AtWork(int job, int skill = 0) => Anywhere with { Job = job, Skill = skill };

            /// <summary>On a map: its subarea and that subarea's area.</summary>
            public static Where At(long mapId)
            {
                int subarea = DatabaseManager.SubAreaOfMap(mapId);
                return Anywhere with { Subarea = subarea, Area = subarea > 0 ? Interactives.AreaOfSubArea(subarea) : -1 };
            }
        }

        /// <summary>
        /// Today's bonus of that type that fits here, in percent: the ones with no condition and
        /// the ones whose every condition is met.
        /// </summary>
        public static int BonusPercent(int type, Where where)
        {
            var today = Today();
            if (today == null) return 0;
            int percent = 0;
            foreach (var bonus in today.Bonuses)
                if (bonus.Type == type && Fits(bonus, where)) percent += bonus.Amount;
            return percent;
        }

        /// <summary>
        /// Whether a bonus's conditions are met: each kind is one of its values, and every kind is
        /// met. The kinds, read off the days whose descriptions name them: 1 an area (the «dimensiones
        /// divinas» are 53, 54 and 55), 2 a subarea («la Cámara de los Maleficios», 1030), 3 a monster
        /// and 4 a race -- one condition, the race and its archmonsters: «{{monsterRace,51::cuerboks}}»
        /// comes with 2437 --, 5 a job («los pescadores»: 36), 7 an item worn («para todos los
        /// aventureros que lleven equipado»), 10 a job's skill («centeno» and «arroz»: skills 52 and
        /// 159, «Segar» rye and rice), 15 the dungeons, 13 «las zonas influenciadas por una anomalía temporal»
        /// (Anomalies' zones), 9 an item obtained (the hunters' meats). Every other kind is never met here.
        /// </summary>
        public static bool Fits(AlmanaxBonus bonus, Where where)
        {
            foreach (var kind in bonus.Criteria.GroupBy(c => c.Type == 4 ? 3 : c.Type))
            {
                bool met = kind.Any(c => c.Type switch
                {
                    1 => where.Area == c.Value,
                    2 => where.Subarea == c.Value,
                    3 => where.Monsters.Contains(c.Value),
                    4 => where.Races.Contains(c.Value),
                    5 => where.Job == c.Value,
                    7 => where.Worn.Contains(c.Value),
                    9 => where.Item == c.Value,
                    10 => where.Skill == c.Value,
                    16 => where.PaddockGauge == c.Value,
                    15 => where.InDungeon,
                    13 => Anomalies.TryGet(where.Subarea, out _) == (c.Value != 0),
                    _ => false,
                });
                if (!met) return false;
            }
            return true;
        }

        /// <summary>
        /// The capacity today's Almanax gives a mount born now, as its effect (Rides.Chameleon 3823 to
        /// Rides.Grower 3828), or 0: the bonus's 17 counts them from the chameleon, «Sabia» being 3 and
        /// «Resistente» 4 (3825, 3826), drawn at the bonus's percent.
        /// </summary>
        public static int NewbornCapacity(Random dice)
        {
            var today = Today();
            if (today == null) return 0;
            foreach (var bonus in today.Bonuses)
            {
                if (bonus.Type != BonusType.NewbornCapacity || dice.Next(100) >= bonus.Amount) continue;
                foreach (var (kind, value) in bonus.Criteria)
                    if (kind == 17 && value >= 1 && value <= 6) return Rides.Chameleon - 1 + value;
            }
            return 0;
        }

        /// <summary>
        /// How many of <paramref name="units"/> a percent chance each comes out on: the Almanax's
        /// «ahorran un 15% de ingredientes», «un 25% de posibilidades de crear un segundo objeto».
        /// </summary>
        public static int ChancesMet(int units, int percent, Random dice)
        {
            if (units <= 0 || percent <= 0) return 0;
            int met = 0;
            for (int i = 0; i < units; i++) if (dice.Next(100) < percent) met++;
            return met;
        }

        /// <summary>
        /// A quantity with a percent bonus on top, the part of a unit it does not reach drawn:
        /// one meat at +125% is two, and a third one time in four.
        /// </summary>
        public static int Raised(int quantity, int percent, Random dice)
        {
            if (quantity <= 0 || percent <= 0) return quantity;
            long extra = (long)quantity * percent;
            return quantity + (int)(extra / 100) + ChancesMet(1, (int)(extra % 100), dice);
        }

        /// <summary>An amount with today's bonus of that type that fits here on top.</summary>
        public static long WithBonus(int type, long amount, Where where)
        {
            int percent = BonusPercent(type, where);
            return percent <= 0 || amount <= 0 ? amount : amount + amount * percent / 100;
        }

        /// <summary>
        /// A fight's monsters and place, for the bonuses: its map's subarea and area, whether it is a
        /// dungeon's room, the monsters and their races, and what the character in session wears.
        /// </summary>
        public static Where ForFight(long mapId, IEnumerable<int> monsters, IEnumerable<int> worn, bool inDungeon)
        {
            int subarea = MapManager.GetMapInfo(mapId)?.SubAreaId ?? 0;
            var ids = monsters.Where(m => m > 0).Distinct().ToList();
            return new Where(subarea > 0 ? Interactives.AreaOfSubArea(subarea) : -1, subarea, ids,
                             ids.Select(RaceOf).Where(r => r != 0).Distinct().ToList(), worn.ToList(), inDungeon);
        }

        private static Dictionary<int, int>? _races;
        private static readonly object _racesGate = new();

        /// <summary>A monster's race, from its template (MonsterTemplates.Data "race"); 0 when unknown.</summary>
        public static int RaceOf(int monsterId)
        {
            lock (_racesGate)
            {
                if (_races == null)
                {
                    _races = new Dictionary<int, int>();
                    try
                    {
                        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(DatabaseManager.WorldConnectionString);
                        connection.Open();
                        using var command = connection.CreateCommand();
                        command.CommandText = "SELECT Id, COALESCE(json_extract(Data, '$.race'), 0) FROM MonsterTemplates;";
                        using var reader = command.ExecuteReader();
                        while (reader.Read()) _races[reader.GetInt32(0)] = reader.GetInt32(1);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Almanax] The monsters' races could not be read: {ex.Message}");
                    }
                }
                return _races.TryGetValue(monsterId, out int race) ? race : 0;
            }
        }

        /// <summary>
        /// Today's bonus of that type, in percent, counting only the ones with no condition.
        /// </summary>
        /// <remarks>
        /// A bonus with a condition — a monster race, a dungeon, a zone — is left out: what each
        /// condition type means is not in the client data either, and applying one on a guess
        /// would give the bonus where the game does not.
        /// </remarks>
        public static int BonusPercent(int type)
        {
            var today = Today();
            if (today == null) return 0;

            int percent = 0;
            foreach (var bonus in today.Bonuses)
            {
                if (bonus.Type == type && bonus.Criteria.Count == 0) percent += bonus.Amount;
            }

            return percent;
        }

        /// <summary>An amount with today's unconditional bonus of that type on top.</summary>
        public static long WithBonus(int type, long amount)
        {
            int percent = BonusPercent(type);
            return percent <= 0 || amount <= 0 ? amount : amount + amount * percent / 100;
        }

        /// <summary>Whether a quest is one of the Almanax offerings.</summary>
        public static bool IsOffering(int questId) => _calendar?.IsOffering(questId) ?? false;

        /// <summary>Today's offering quest, or zero.</summary>
        public static int TodaysOffering() => Today()?.QuestId ?? 0;

        /// <summary>The date as the tally keeps it, 20260926.</summary>
        public static long DayKey(DateTime date) => date.Year * 10000L + date.Month * 100 + date.Day;

        /// <summary>
        /// Whether this character has already made an offering today. The quests are repeatable,
        /// so without this the same one could be taken again straight after finishing it.
        /// </summary>
        public static bool OfferedToday(IReadOnlyDictionary<(string Kind, long Key), long> tallies, int questId)
            => tallies.TryGetValue((OfferingKind, questId), out long day) && day == DayKey(Clock());
    }
}
