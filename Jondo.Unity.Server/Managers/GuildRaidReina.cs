using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jondo.Unity.World.Fights;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Reina Escarlata's fight in the Santuario's dungeons: four jails round her board, the
    /// players starting in them, a Floráculo before each.
    /// </summary>
    /// <remarks>
    /// THE GUIDE (DPLN): "La map est constituée de 4 cachots et d'une zone de combat (où se trouve la
    /// Reine Écarlate) au milieu. Au début du combat, tous les personnages commencent dans un cachot.
    /// Il y a 2 plots de placement par cachot". "Quand le combat commence, 2 monstres apparaissent dans
    /// chaque cachot : 2 x Tritulipe en haut à gauche, 2 x Fracamélia en haut à droite, 2 x Muguégide en
    /// bas à gauche, 2 x Dahliane en bas à droite" -- none in an empty jail at six players or fewer --,
    /// and "les monstres de chaque cachot partagent les dommages entre eux". "Pendant ce temps, la Reine
    /// Écarlate ne fait rien". "Au moment où vous éliminez le dernier des 8 monstres, tous vos
    /// personnages sortiront des cachots et seront téléporté à l'entrée de leur cachot. Ils seront
    /// également intégralement soignés." "Le Floracle est une entité statique qui n'a pas de tour de jeu.
    /// Il fait office de porte (il y en a 4, un pour chaque cachot)."
    ///
    /// THE CLIENT'S DATA: the arena (238295313) flags two red cells in each jail and nine blue ones in
    /// the middle (GuildRaidStagings.PlacementOf). Each jail is a "prisonGlyph" (4040) of the cells
    /// it holds; the Floráculo's threshold (31952) names, per Floráculo, its jail's glyph and exit
    /// cell (4068), its jail's marker state (7110 to 7113) and the "Staging Manager" grades
    /// (32709) that lower its cage ("#SD_cage1_down"...) -- and the grade before each raises it.
    /// Where each Floráculo stands is its hitbox's cell (31936). The Reina's own "Passtour" (32169,
    /// effect 140 for good) is what has her do nothing; nothing in the data takes it off.
    ///
    /// INFERRED: the jails' monsters and the Floráculos come in as the Reina's summons at the fight's
    /// start, the cages closed behind the players; the pairs share damage through 1061 rows; the jail
    /// monsters stand on the jail's free cells farthest from the players; the Reina's pass goes when the
    /// last of them falls. Not done: the jails as fights of their own, the player's summons dying as
    /// he leaves one.
    /// </remarks>
    public static class GuildRaidReina
    {
        public const int Reina = GuildRaidSanctuary.Reina;

        /// <summary>The Floráculo's threshold and its "tpOut" grades, its hitbox, and the stagings' manager.</summary>
        public const int FloracleThreshold = 31952;
        public const int FloracleTpOut = 31953;
        public const int FloracleHitbox = 31936;
        public const int StagingManager = 32709;

        /// <summary>The Reina's "Passtour": she passes her turns while the jails hold their monsters.</summary>
        public const int PassTurns = 32169;

        /// <summary>A jail's cells (4040, "prisonGlyph") and the way out of it (4068).</summary>
        public const int PrisonGlyph = 4040;
        public const int JailExit = 4068;

        /// <summary>The kinds in each jail, by the guide, in the jails' order: Tritulipe, Fracamélia, Muguégide, Dahliane.</summary>
        public static readonly IReadOnlyList<int> JailKinds = new[] { 8285, 8286, 8287, 8288 };

        /// <summary>Two monsters a jail; at six players or fewer, none in a jail nobody is in.</summary>
        public const int PerJail = 2;
        public const int FewPlayers = 6;

        /// <summary>
        /// A jail: its number (I top left to IV bottom right, by its marker state), its glyph spell and
        /// cells, its exit, its marker, its Floráculo and that one's cell, the stagings raising and
        /// lowering its cage, and the kind of its monsters.
        /// </summary>
        public sealed record Jail(int Number, int Glyph, IReadOnlyList<int> Cells, int Exit, int Marker,
                                  int Floracle, int FloracleCell, int CageUp, int CageDown, int Kind);

        private static IReadOnlyList<Jail> _jails;

        /// <summary>The four jails as the client's spells lay them out; empty without the data.</summary>
        public static IReadOnlyList<Jail> Jails()
        {
            if (_jails != null) return _jails;
            var jails = new List<Jail>();
            foreach (var toTpOut in SpellEffects.De(FloracleThreshold, 1).Where(e => e.DiceNum == FloracleTpOut))
            {
                var floracle = Regex.Match(toTpOut.TargetMask ?? "", @"\*F(\d+)");
                if (!floracle.Success) continue;
                int floracleId = int.Parse(floracle.Groups[1].Value);
                var next = SpellEffects.De(FloracleTpOut, toTpOut.DiceSide).FirstOrDefault(e => e.DiceNum == FloracleTpOut);
                if (next == null) continue;
                var tpOut = SpellEffects.De(FloracleTpOut, next.DiceSide);
                var exit = tpOut.FirstOrDefault(e => e.EffectId == JailExit);
                var cage = tpOut.FirstOrDefault(e => e.DiceNum == StagingManager);
                var marker = exit == null ? null : Regex.Match(exit.TargetMask ?? "", @"\bE(\d+)");
                if (exit == null || cage == null || marker == null || !marker.Success) continue;
                var cells = SpellEffects.De(exit.DiceNum, 1).FirstOrDefault(e => e.EffectId == PrisonGlyph)?.CeldasFijas;
                var hitbox = SpellEffects.De(FloracleHitbox, 1).FirstOrDefault(e => (e.TargetMask ?? "").Contains("*F" + floracleId));
                int down = CageOf(cage.DiceSide), up = CageOf(cage.DiceSide - 1);
                if (cells == null || cells.Count == 0 || hitbox == null || hitbox.CeldasFijas.Count == 0 || down < 0 || up < 0) continue;
                jails.Add(new Jail(0, exit.DiceNum, cells, exit.DiceSide, int.Parse(marker.Groups[1].Value),
                                   floracleId, hitbox.CeldasFijas[0], up, down, 0));
            }
            if (jails.Count != JailKinds.Count) return Array.Empty<Jail>();
            _jails = jails.OrderBy(j => j.Marker).Select((j, i) => j with { Number = i + 1, Kind = JailKinds[i] }).ToList();
            return _jails;
        }

        /// <summary>The staging a grade of the stagings' manager plays (2192), by its uniqueId; -1 if none.</summary>
        private static int CageOf(int grade)
            => grade < 1 ? -1 : SpellEffects.De(StagingManager, grade).FirstOrDefault(e => e.EffectId == 2192)?.DiceNum ?? -1;

        /// <summary>The jail a cell is in, or null.</summary>
        public static Jail JailAt(int cell) => Jails().FirstOrDefault(j => j.Cells.Contains(cell));

        // ─── The fight ──────────────────────────────────────────────────────────────────────

        /// <summary>The fights whose jails still hold their monsters.</summary>
        private static readonly ConcurrentDictionary<long, byte> _jailPhase = new();

        private static Fighter ReinaOf(FightInstance fight) => fight?.Rojo.FirstOrDefault(m => m.IsAlive && m.IsMonster && !m.EsInvocado && m.MonsterId == Reina);

        public static bool InJailPhase(FightInstance fight) => fight != null && _jailPhase.ContainsKey(fight.FightId);

        /// <summary>
        /// The board starts: each jail's cage closed and its Floráculo before it, and two monsters in
        /// each jail -- in those with somebody, at six players or fewer -- sharing their damage.
        /// </summary>
        public static async Task OnFightStartedAsync(NetworkStream stream, FightInstance fight)
        {
            var reina = ReinaOf(fight);
            var jails = Jails();
            if (reina == null || jails.Count == 0 || !_jailPhase.TryAdd(fight.FightId, 0)) return;
            int people = fight.Azul.Count(f => f.IsAlive && !f.IsMonster && !f.EsInvocado);
            int placed = 0;
            foreach (var jail in jails)
            {
                await Handlers.FightHandler.PlayStagingAsync(fight, reina, jail.CageUp);
                await Handlers.FightHandler.SummonAsync(stream, fight, reina, jail.Floracle, 1, jail.FloracleCell);

                var inside = fight.Azul.Where(f => f.IsAlive && jail.Cells.Contains(f.CellId)).Select(f => f.CellId).ToList();
                if (people <= FewPlayers && inside.Count == 0) continue;
                var pair = new List<Fighter>();
                foreach (int cell in MonsterCells(fight, jail, inside, PerJail))
                {
                    var monster = await Handlers.FightHandler.SummonAsync(stream, fight, reina, jail.Kind, 1, cell);
                    if (monster != null) pair.Add(monster);
                }
                foreach (var monster in pair) Share(fight, reina, jail, monster);
                placed += pair.Count;
            }
            Program.LogDebug($"[Raids] The Reina's jails closed on {people} player(s), {placed} monster(s) in them.");
            if (placed == 0) await OpenTheJailsAsync(stream, fight, reina);
        }

        /// <summary>A jail's free cells farthest from whoever is in it, as many as asked.</summary>
        public static List<int> MonsterCells(FightInstance fight, Jail jail, IReadOnlyCollection<int> players, int count)
        {
            var taken = new HashSet<int>(fight.Todos.Where(f => f.IsAlive).Select(f => f.CellId));
            var walkable = MapManager.GetFightWalkable(fight.MapId);
            return jail.Cells.Where(c => !taken.Contains(c) && (walkable == null || walkable.Count == 0 || walkable.Contains(c)))
                       .OrderByDescending(c => players.Count == 0 ? 0 : players.Min(p => Jondo.Unity.World.Maps.MapGeometry.Distance(c, p)))
                       .ThenBy(c => c)
                       .Take(count).ToList();
        }

        /// <summary>A jail's monster shares the damage it takes with the other of its jail (1061, by the Reina and the jail's glyph).</summary>
        private static void Share(FightInstance fight, Fighter reina, Jail jail, Fighter monster)
        {
            monster.Buffs.Poner(new Buff
            {
                EffectId = EffectEngine.ComparteLosDanos, HechizoOrigen = jail.Glyph, NivelOrigen = 1,
                Quien = reina.Id, Disparador = "D", CaducaEnRonda = -1, EmpiezaEnRonda = fight.RoundNumber,
            }, fight.SiguienteEmbrujo);
        }

        /// <summary>A fighter fell: the last of the jails' monsters opens them.</summary>
        public static async Task OnDeathAsync(NetworkStream stream, FightInstance fight, Fighter fallen)
        {
            if (fallen == null || !InJailPhase(fight) || !JailKinds.Contains(fallen.MonsterId)) return;
            var reina = ReinaOf(fight);
            if (reina == null) return;
            if (fight.Rojo.Any(m => m.IsAlive && m.EsInvocado && m.Invocador == reina.Id && JailKinds.Contains(m.MonsterId))) return;
            await OpenTheJailsAsync(stream, fight, reina);
        }

        /// <summary>
        /// The jails emptied of their monsters: everyone in one to its exit, healed in full, the cages
        /// lowered, and the Reina playing from now on.
        /// </summary>
        private static async Task OpenTheJailsAsync(NetworkStream stream, FightInstance fight, Fighter reina)
        {
            if (!_jailPhase.TryRemove(fight.FightId, out _)) return;
            foreach (var player in fight.Azul.Where(f => f.IsAlive && !f.EsInvocado).ToList())
            {
                var jail = JailAt(player.CellId);
                if (jail == null) continue;
                await Handlers.FightHandler.TeleportNearAsync(fight, player, jail.Exit);
                await Handlers.FightHandler.HealFullyAsync(stream, fight, player, reina);
            }
            foreach (var jail in Jails())
                await Handlers.FightHandler.PlayStagingAsync(fight, reina, jail.CageDown);
            await Handlers.FightHandler.RemoveSpellRowsAsync(fight, reina, PassTurns);
            Program.LogDebug($"[Raids] Fight #{fight.FightId}: the Reina's jails open, her fight begins.");
        }

        /// <summary>A fight over: its jails go.</summary>
        public static void Forget(long fightId) => _jailPhase.TryRemove(fightId, out _);
    }
}
