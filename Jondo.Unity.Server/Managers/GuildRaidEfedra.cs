using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Content;
using Jondo.Unity.World.Fights;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Santuario's fourth enigma, the Patio de Efedra's chess: four pieces found on the Reserva's
    /// chessboards, then a game against Belladona where four players, turned into those pieces, stand
    /// where they stood.
    /// </summary>
    /// <remarks>
    /// THE GUIDE (DPLN): Belladona, at [12,14] of the Patio, "S'informer sur les règles à suivre":
    /// "des pièces sont placées aléatoirement sur 4 échiquiers dans la zone Réserve de Belladone. Il
    /// faudra alors parmi toutes ces pièces trouver les 4 qui sont à la bonne position (2 Tours et 2
    /// Fous)" -- red if not ("ne fait pas perdre de PV"), green if so. Then, four of them, "Défier
    /// Belladone": "Vous serez transformé en pièces d'échecs", "le but du combat est de se placer sur
    /// la même position que chaque pièce avait dans l'autre zone", rooks in line, bishops in
    /// diagonal, a piece in place may give an ally action points; "au bout du quatrième tour de
    /// Belladone, le combat se termine et si vous êtes bien placé c'est gagné sinon c'est perdu".
    /// 2000 points.
    ///
    /// THE CLIENT'S DATA:
    ///
    ///   the boards, four of the Reserva's maps, each a variant b (0 to 3) by its stagings' names:
    ///   "#SD spawn_k" and "#SD spawn_leurre_n" bring its pieces (the elements named "3,cp_pc{k}")
    ///   and decoys ("leurre_{n}"); "#SD 3,cp_pc{k}_vert_{b}" turns piece k green, "_rouge_" red,
    ///   "#SD leurre_{n}" a decoy red. Piece k stands where it should on the board of its variant,
    ///   drawn by the server -- the fight's "Check des solutions cp_pc{k} == {b}" list them all
    ///   and the data picks none.
    ///
    ///   the fight, against Belladona 8246: "Aplicación de roles y hechizos" (31839), grade k, turns
    ///   a player into piece k -- its look, its move (3405: "Desplazamiento Táctico" of the rooks,
    ///   31840, or of the bishops, 31841, in line or in diagonal), its state 6537+k-1. "Pose des
    ///   glyphes" of piece k (31845, 31853, 31854, 31855) lays, at grade b+1, the glyph on the cell
    ///   piece k must reach, and its grade 5 puts the piece's "placed" state (6541+k-1), which
    ///   "Bendición" (31847) asks for. Belladona's own script (31842) looks, three turns on, for the
    ///   four placed states -- which the client's "Pieza de ajedrez" (8245) gave her and no fighter of
    ///   the guide's fight carries --: so the server decides it, at the start of her fourth turn.
    ///   The arena's "chiffre_1".."chiffre_3" count down the turns left.
    ///
    ///   Belladona the NPC (7845): "¿Te atreves a jugar contra mí?" with "Desafiar a Belladona al
    ///   ajedrez", "Informarte sobre las reglas que hay que seguir" ("explora mi despensa, puede que
    ///   haya dejado pistas") and "Adiós"; "Contra todo pronóstico, has ganado la partida" once won.
    ///
    /// INFERRED: the counter counts down; the pieces go to the players in the order they joined.
    /// The goal, "Superar el enigma del patio de Efedra", is the game won.
    /// </remarks>
    public static class GuildRaidEfedra
    {
        /// <summary>The Patio de Efedra is the Santuario's fourth zone; its pieces are on the third's boards.</summary>
        public const int Zone = 4;
        public const int BoardZone = 3;

        /// <summary>The goal it meets: "Superar el enigma del patio de Efedra".</summary>
        public const int Goal = 4;

        /// <summary>Four pieces, four variants of where each stands.</summary>
        public const int Pieces = 4;
        public const int Variants = 4;

        /// <summary>Belladona: the NPC at [12,14] (the guide), and the chess's Belladona, fought.</summary>
        public const int Npc = 7845;
        public const int NpcX = 12, NpcY = 14;
        public const int Queen = 8246;

        /// <summary>The NPC's dialogue, its template's own messages and replies.</summary>
        public const long Question = 59649, Rules = 60268, Won = 59658;
        public const long Challenge = 81577, AskRules = 82819, Bye = 82820, Shrug = 82821, Mock = 81586;

        /// <summary>The fight's spells: the turning into pieces, each piece's glyph, the blessing, the queen's script.</summary>
        public const int Transformation = 31839;
        public static readonly IReadOnlyList<int> Glyphs = new[] { 31845, 31853, 31854, 31855 };
        public const int GlyphPlaced = 5;
        public const int Blessing = 31847;
        public const int QueenScript = 31842;

        /// <summary>The boards' clickables: unnamed, "Utilizar".</summary>
        public const int PieceType = -1;
        public const int PieceSkill = 184;

        /// <summary>The stagings, by their names in the client's maps.</summary>
        public static string Spawn(int piece) => $"#SD spawn_{piece}";
        public static string SpawnDecoy(int decoy) => $"#SD spawn_leurre_{decoy}";
        public static string Green(int piece, int variant) => $"#SD 3,cp_pc{piece}_vert_{variant}";
        public static string Red(int piece, int variant) => $"#SD 3,cp_pc{piece}_rouge_{variant}";
        public static string DecoyRed(int decoy) => $"#SD leurre_{decoy}";
        public static string Counter(int left) => $"chiffre_{left}";
        public const string CounterReset = "chiffre_reset";

        /// <summary>Where the raid keeps the variants, whether the pieces are out, and the ones found.</summary>
        public const string VariantsSequence = "Efedra_Variants";
        public const string LaidVariable = "Efedra_Laid";
        public const string FoundVariable = "Efedra_Found";

        /// <summary>A chessboard of the Reserva: its variant, its map, its pieces and decoys by element.</summary>
        public sealed record Board(int Variant, long MapId, IReadOnlyDictionary<int, int> Pieces, IReadOnlyDictionary<int, int> Decoys);

        /// <summary>The enigma as the client's maps lay it out.</summary>
        public sealed record Work(IReadOnlyList<Board> Boards, long NpcMap, long Centre);

        private static readonly ConcurrentDictionary<int, Work> _works = new();

        /// <summary>A raid's Patio, or null for one without it.</summary>
        public static Work WorkOf(RaidKind kind)
        {
            if (kind == null || kind.Floors.Count < Zone) return null;
            if (_works.TryGetValue(kind.Id, out var known)) return known;
            var work = Read(kind);
            if (work != null) _works[kind.Id] = work;
            return work;
        }

        private static Work Read(RaidKind kind)
        {
            var boards = new List<Board>();
            foreach (long map in DatabaseManager.MapsOfSubArea(kind.Floors[BoardZone - 1]).OrderBy(m => m))
            {
                if (!GuildRaidStagings.Has(map, Spawn(1))) continue;
                int variant = GuildRaidStagings.Of(map).Select(s => Regex.Match(s.Name, @"^#SD 3,cp_pc1_vert_(\d+)$"))
                                               .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).First();
                var pieces = new Dictionary<int, int>();
                var decoys = new Dictionary<int, int>();
                foreach (var (name, _, element) in GuildRaidStagings.ElementsOf(map))
                {
                    if (element == 0) continue;
                    var piece = Regex.Match(name, @"^3,cp_pc(\d)$");
                    var decoy = Regex.Match(name, @"^leurre_(\d)$");
                    if (piece.Success) pieces[element] = int.Parse(piece.Groups[1].Value);
                    else if (decoy.Success) decoys[element] = int.Parse(decoy.Groups[1].Value);
                }
                if (variant >= 0 && pieces.Count == Pieces) boards.Add(new Board(variant, map, pieces, decoys));
            }
            var patio = DatabaseManager.MapsOfSubArea(kind.Floors[Zone - 1]).OrderBy(m => m).ToList();
            long npcMap = GuildRaidStagings.MapAt(patio.Where(m => !GuildRaidManager.IsArena(m)), NpcX, NpcY);
            long centre = patio.FirstOrDefault(m => GuildRaidStagings.Has(m, string.Format(GuildRaidMonochrome.StepDone, 1)));
            if (boards.Count != Variants || npcMap == 0 || centre == 0) return null;
            return new Work(boards.OrderBy(b => b.Variant).ToList(), npcMap, centre);
        }

        // ─── The draw and the NPC ───────────────────────────────────────────────────────────

        /// <summary>A raid starting: where each piece stands right, one board of the four.</summary>
        public static void Draw(RaidInstance raid, Random dice)
        {
            if (raid == null || WorkOf(Raids.Of(raid.RaidId)) == null) return;
            Set(raid, Enumerable.Range(0, Pieces).Select(_ => dice.Next(Variants)).ToList());
        }

        /// <summary>The variants as given (and for the tests).</summary>
        internal static void Set(RaidInstance raid, IReadOnlyList<int> variants)
        {
            raid.SetSequence(VariantsSequence, variants);
            raid.Set(LaidVariable, 0);
            raid.Set(FoundVariable, 0);
        }

        /// <summary>Whether the game is won for a raid.</summary>
        public static bool Solved(RaidInstance raid) => raid != null && raid.GoalAt(Goal) >= 1;

        /// <summary>Where Belladona the NPC stands: the Patio's [12,14], on its walkable cell nearest the middle.</summary>
        public static (long MapId, int Cell)? NpcPlace()
        {
            var work = WorkOf(Raids.Of(Raids.EternalGardens));
            if (work == null) return null;
            int cell = MapManager.GetNearestWalkableCell(work.NpcMap, Handlers.TeleportHandler.MapCentre);
            return cell < 0 ? null : (work.NpcMap, cell);
        }

        /// <summary>The NPC's opening: the game won, her spite; otherwise her challenge, the rules and goodbye.</summary>
        public static (long Question, long[] Replies) DialogueFor(long characterId)
        {
            var raid = GuildRaidManager.RaidOf(characterId);
            if (Solved(raid)) return (Won, new[] { Mock });
            return raid == null ? (Question, new[] { Bye }) : (Question, new[] { Challenge, AskRules, Bye });
        }

        /// <summary>Whether a reply is the NPC's.</summary>
        public static bool Owns(long reply) => reply is Challenge or AskRules or Bye or Shrug or Mock;

        /// <summary>
        /// The rules asked: the pieces and decoys come out on the Reserva's four boards, for the raid
        /// to find. Her answer is "explora mi despensa".
        /// </summary>
        public static Task<(long Question, long[] Replies)> RulesAsync(long characterId) => RulesAsync(GuildRaidManager.RaidOf(characterId));

        internal static async Task<(long Question, long[] Replies)> RulesAsync(RaidInstance raid)
        {
            var work = raid == null ? null : WorkOf(Raids.Of(raid.RaidId));
            if (work != null && raid.Get(LaidVariable) == 0)
            {
                raid.Set(LaidVariable, 1);
                foreach (var board in work.Boards)
                {
                    for (int n = 1; n <= Pieces; n++)
                    {
                        await GuildRaidStagings.PlayAsync(raid, board.MapId, Spawn(n), keep: true);
                        await GuildRaidStagings.PlayAsync(raid, board.MapId, SpawnDecoy(n), keep: true);
                    }
                    foreach (int element in board.Pieces.Keys.Concat(board.Decoys.Keys))
                        await GuildRaidStagings.RedeclareAsync(raid, board.MapId, element, InteractiveActionKind.RaidChessPiece, usable: true);
                }
                Program.LogDebug($"[Raids] {raid.Uuid}: Belladona's pieces come out on the Reserva's boards.");
            }
            return (Rules, new[] { Shrug });
        }

        // ─── The Reserva's boards ───────────────────────────────────────────────────────────

        /// <summary>The board an element is a piece or a decoy of, or null.</summary>
        public static Board BoardAt(RaidKind kind, long mapId, int elementId)
            => WorkOf(kind)?.Boards.FirstOrDefault(b => b.MapId == mapId && (b.Pieces.ContainsKey(elementId) || b.Decoys.ContainsKey(elementId)));

        public static bool Offers(long characterId, long mapId, int elementId) => Offers(GuildRaidManager.RaidOf(characterId), mapId, elementId);

        /// <summary>Whether a raid's pieces are out to be clicked: once its players asked for the rules.</summary>
        internal static bool Offers(RaidInstance raid, long mapId, int elementId)
            => raid != null && raid.Get(LaidVariable) != 0 && BoardAt(Raids.Of(raid.RaidId), mapId, elementId) != null;

        public static bool Usable(long characterId, long mapId, int elementId) => Offers(characterId, mapId, elementId);

        public static Task<bool> PressAsync(NetworkStream stream, long characterId, long mapId, int elementId, int skillId)
            => PressAsync(stream, GuildRaidManager.RaidOf(characterId), mapId, elementId, skillId);

        /// <summary>A piece or a decoy clicked: green where the piece stands right, red otherwise -- no life lost.</summary>
        public static async Task<bool> PressAsync(NetworkStream stream, RaidInstance raid, long mapId, int elementId, int skillId)
        {
            var work = raid == null ? null : WorkOf(Raids.Of(raid.RaidId));
            var board = work == null ? null : BoardAt(Raids.Of(raid.RaidId), mapId, elementId);
            if (board == null || raid.Get(LaidVariable) == 0) return false;
            if (stream != null)
            {
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                    ConnectionProtocol.Push(Op.Iwi, ConnectionProtocol.BuildInteractiveUseEnded(elementId, skillId)));
            }

            if (board.Decoys.TryGetValue(elementId, out int decoy))
            {
                await GuildRaidStagings.PlayAsync(raid, mapId, DecoyRed(decoy), keep: false);
                return true;
            }
            int piece = board.Pieces[elementId];
            var variants = raid.SequenceOf(VariantsSequence);
            if (piece - 1 >= variants.Count || variants[piece - 1] != board.Variant)
            {
                await GuildRaidStagings.PlayAsync(raid, mapId, Red(piece, board.Variant), keep: false);
                return true;
            }
            await GuildRaidStagings.PlayAsync(raid, mapId, Green(piece, board.Variant), keep: true);
            long found = raid.Get(FoundVariable);
            if ((found & (1L << (piece - 1))) == 0)
            {
                raid.Set(FoundVariable, found | (1L << (piece - 1)));
                int count = Enumerable.Range(0, Pieces).Count(k => (raid.Get(FoundVariable) & (1L << k)) != 0);
                await GuildRaidStagings.PlayAsync(raid, work.Centre, string.Format(GuildRaidMonochrome.StepDone, count), keep: true);
                Program.LogDebug($"[Raids] {raid.Uuid}: Belladona's piece {piece} found on board {board.Variant}.");
            }
            return true;
        }

        // ─── The game ───────────────────────────────────────────────────────────────────────

        /// <summary>A chess game being fought: its raid, its players' pieces, Belladona's turns, the pieces in place.</summary>
        private sealed class Game
        {
            public RaidInstance Raid;
            public Dictionary<long, int> PieceOf;
            public int QueenTurns;
            public bool Started;
            public readonly HashSet<int> Placed = new();
        }

        private static readonly ConcurrentDictionary<long, Game> _games = new();

        /// <summary>
        /// The challenge accepted: Belladona's chess group, beside her or already out there, fought by
        /// whoever challenged; the others of the raid join it, four at most.
        /// </summary>
        public static async Task ChallengeAsync(NetworkStream stream, long characterId)
        {
            var raid = GuildRaidManager.RaidOf(characterId);
            var work = raid == null ? null : WorkOf(Raids.Of(raid.RaidId));
            if (work == null || Solved(raid) || stream == null) return;
            var group = MobSpawnManager.GetMobsForMap(work.NpcMap)
                                       .FirstOrDefault(g => g.Members.Any(m => m.Monster.Id == Queen) && !Handlers.FightHandler.IsGroupFighting(g.MobId))
                        ?? MobSpawnManager.SpawnComposed(work.NpcMap, new[] { (Queen, 0) });
            if (group == null) return;
            await Handlers.FightHandler.InitiateFightFromMobCollision(stream, group, work.NpcMap);
        }

        /// <summary>A fight just built: one against the chess's Belladona is the raid's game, for four, decided by the server.</summary>
        public static void OnFightCreated(FightInstance fight)
        {
            var queen = fight?.Rojo.FirstOrDefault(m => m.IsMonster && m.MonsterId == Queen);
            if (queen == null) return;
            var player = fight.Azul.Concat(fight.Rojo).FirstOrDefault(f => !f.IsMonster && !f.EsInvocado);
            var raid = player == null ? null : GuildRaidManager.RaidOf(player.Id);
            if (raid == null || raid.RaidId != Raids.EternalGardens) return;
            fight.PeopleCap = Pieces;
            queen.Conducta = (0, 0);      // her script's end is the server's here (QueenTurnAsync)
            _games[fight.FightId] = new Game { Raid = raid };
        }

        private static Game GameOf(FightInstance fight) => fight != null && _games.TryGetValue(fight.FightId, out var game) ? game : null;

        public static bool IsGame(FightInstance fight) => GameOf(fight) != null;

        /// <summary>The players' pieces, given once the fight has begun: in the order they joined it.</summary>
        private static Dictionary<long, int> PiecesOf(FightInstance fight, Game game)
        {
            if (game.PieceOf != null) return game.PieceOf;
            if (fight.State != FightState.Ongoing) return null;
            var players = fight.Azul.Concat(fight.Rojo).Where(f => !f.IsMonster && !f.EsInvocado).Take(Pieces).ToList();
            game.PieceOf = players.Select((p, i) => (p.Id, Piece: i + 1)).ToDictionary(x => x.Id, x => x.Piece);
            return game.PieceOf;
        }

        /// <summary>The piece a character plays in his fight, or 0.</summary>
        public static int PieceOf(long characterId)
        {
            var fight = Handlers.FightHandler.FightOf(characterId);
            var game = GameOf(fight);
            return game == null ? 0 : PiecesOf(fight, game)?.GetValueOrDefault(characterId) ?? 0;
        }

        /// <summary>A piece's move: the spell its turning gives it (3405 of 31839, grade = the piece).</summary>
        public static int MoveOf(int piece)
        {
            int level = SpellEffects.De(Transformation, piece).FirstOrDefault(e => e.EffectId == GiveSpell)?.Value ?? 0;
            return level == 0 ? 0 : SpellOfLevel(level);
        }

        /// <summary>The effect of the turning that gives the piece's spell, by spell level.</summary>
        public const int GiveSpell = 3405;

        /// <summary>The cell a piece must reach for a raid: its glyph's, at the grade of its variant.</summary>
        public static int TargetOf(RaidInstance raid, int piece)
        {
            var variants = raid?.SequenceOf(VariantsSequence);
            if (variants == null || piece < 1 || piece > Glyphs.Count || piece > variants.Count) return -1;
            var glyph = SpellEffects.De(Glyphs[piece - 1], variants[piece - 1] + 1).FirstOrDefault(e => e.CeldasFijas.Count > 0);
            return glyph == null ? -1 : glyph.CeldasFijas[0];
        }

        /// <summary>The fight's spells of a character playing a piece: its move and the blessing. Null when he plays none.</summary>
        public static List<(int Spell, int Grade)> SpellsFor(long characterId)
        {
            int piece = PieceOf(characterId);
            int move = piece == 0 ? 0 : MoveOf(piece);
            return move == 0 ? null : new List<(int, int)> { (move, 1), (Blessing, 1) };
        }

        /// <summary>
        /// Whether a piece may cast: only its move -- in line for a rook, in diagonal for a bishop, as
        /// its spell says -- and the blessing.
        /// </summary>
        public static bool MayCast(FightInstance fight, Fighter caster, int spell, int cell)
        {
            var game = GameOf(fight);
            if (game == null || caster.IsMonster) return true;
            int piece = PiecesOf(fight, game)?.GetValueOrDefault(caster.Id) ?? 0;
            if (spell == Blessing) return piece != 0;
            return piece != 0 && spell == MoveOf(piece) && Reaches(spell, caster.CellId, cell);
        }

        /// <summary>Whether a move goes from a cell to another: in line, in diagonal, as its spell is cast.</summary>
        public static bool Reaches(int spell, int from, int to)
        {
            var (inLine, inDiagonal) = WaysOf(spell);
            var (x0, y0) = Jondo.Unity.World.Maps.MapGeometry.CellToPoint(from);
            var (x1, y1) = Jondo.Unity.World.Maps.MapGeometry.CellToPoint(to);
            bool line = x0 == x1 || y0 == y1, diagonal = Math.Abs(x1 - x0) == Math.Abs(y1 - y0);
            return from != to && ((inLine && line) || (inDiagonal && diagonal));
        }

        /// <summary>A piece does not walk: it moves by its spell.</summary>
        public static bool MayWalk(FightInstance fight, Fighter walker) => GameOf(fight) == null || walker.IsMonster;

        /// <summary>
        /// The game begins, once: each player turned into his piece, and the four glyphs laid where the
        /// pieces must go.
        /// </summary>
        public static async Task OnFightStartedAsync(NetworkStream stream, FightInstance fight)
        {
            var game = GameOf(fight);
            var queen = game == null ? null : fight.Rojo.FirstOrDefault(m => m.IsAlive && m.MonsterId == Queen);
            if (queen == null || game.Started) return;
            game.Started = true;
            var pieces = PiecesOf(fight, game) ?? new Dictionary<long, int>();
            foreach (var (playerId, piece) in pieces)
            {
                var player = fight.Buscar(playerId);
                if (player != null)
                    await Handlers.FightHandler.CastAtAsync(stream, fight, queen, Transformation, piece, player.CellId);
            }
            var variants = game.Raid.SequenceOf(VariantsSequence);
            for (int piece = 1; piece <= Glyphs.Count && piece <= variants.Count; piece++)
            {
                int target = TargetOf(game.Raid, piece);
                if (target >= 0)
                    await Handlers.FightHandler.CastAtAsync(stream, fight, queen, Glyphs[piece - 1], variants[piece - 1] + 1, target);
            }
            await Handlers.FightHandler.PlayStagingAsync(fight, queen, CounterReset);
            Program.LogDebug($"[Raids] {game.Raid.Uuid}: Belladona's game begins with {pieces.Count} piece(s).");
        }

        /// <summary>
        /// A piece's move done: on its cell, it is placed -- its glyph's grade 5 gives it its state,
        /// unless the glyph's aura already did as the piece landed on it.
        /// </summary>
        public static async Task AfterCastAsync(NetworkStream stream, FightInstance fight, Fighter caster)
        {
            var game = GameOf(fight);
            var queen = game == null ? null : fight.Rojo.FirstOrDefault(m => m.IsAlive && m.MonsterId == Queen);
            if (queen == null || caster.IsMonster) return;
            int piece = PiecesOf(fight, game)?.GetValueOrDefault(caster.Id) ?? 0;
            if (piece == 0 || game.Placed.Contains(piece) || caster.CellId != TargetOf(game.Raid, piece)) return;
            game.Placed.Add(piece);
            int placed = PlacedStateOf(piece);
            if (placed == 0 || !caster.Buffs.TieneEstado(placed))
                await Handlers.FightHandler.CastAtAsync(stream, fight, queen, Glyphs[piece - 1], GlyphPlaced, caster.CellId);
            Program.LogDebug($"[Raids] {game.Raid.Uuid}: piece {piece} stands where it should.");
        }

        /// <summary>The state of a piece in place: the one its glyph's grade 5 adds.</summary>
        public static int PlacedStateOf(int piece)
            => piece < 1 || piece > Glyphs.Count ? 0
             : SpellEffects.De(Glyphs[piece - 1], GlyphPlaced).FirstOrDefault(e => e.EffectId == Jondo.Unity.World.Combat.EffectSupport.AddState)?.Value ?? 0;

        /// <summary>
        /// Belladona's turn begins: her fourth, the game is over -- won with the four pieces on their
        /// cells, lost otherwise --; before it, the counter shows the turns left. True when it ended.
        /// </summary>
        public static async Task<bool> QueenTurnAsync(NetworkStream stream, FightInstance fight, Fighter fighter)
        {
            var game = GameOf(fight);
            if (game == null || !fighter.IsMonster || fighter.MonsterId != Queen) return false;
            int last = TurnsOf();
            game.QueenTurns++;
            if (game.QueenTurns < last)
            {
                await Handlers.FightHandler.PlayStagingAsync(fight, fighter, Counter(last - game.QueenTurns));
                return false;
            }

            var pieces = PiecesOf(fight, game) ?? new Dictionary<long, int>();
            bool won = pieces.Count == Pieces && pieces.All(p => fight.Buscar(p.Key) is { IsAlive: true } player
                                                                && player.CellId == TargetOf(game.Raid, p.Value));
            Console.WriteLine($"[Raid] {game.Raid.Uuid}: Belladona's game {(won ? "won" : "lost")} at her turn {game.QueenTurns}.");
            await Handlers.FightHandler.SideFallsAsync(stream, fight, players: !won);
            return true;
        }

        /// <summary>The turn of Belladona's that ends the game: her script's check, three turns on.</summary>
        public static int TurnsOf()
        {
            var check = SpellEffects.De(QueenScript, 1).FirstOrDefault(e => e.DiceNum == QueenScript && e.Delay > 0);
            return check == null || check.Delay <= 0 ? 4 : check.Delay + 1;
        }

        /// <summary>A fight won: the chess's Belladona beaten is the game won -- 2000 points and a life.</summary>
        public static async Task BeatenAsync(RaidInstance raid, IReadOnlyDictionary<int, int> beaten)
        {
            if (raid == null || Solved(raid) || !beaten.ContainsKey(Queen)) return;
            await GuildRaidManager.AdvanceGoalAsync(raid, Goal, 1);
            await GuildRaidManager.GainHealthAsync(raid, 1);
            Console.WriteLine($"[Raid] {raid.Uuid}: the Patio de Efedra's chess game is won.");
        }

        /// <summary>A fight over: its game goes.</summary>
        public static void Forget(long fightId) => _games.TryRemove(fightId, out _);

        // ─── The spells' own data ───────────────────────────────────────────────────────────

        private static readonly ConcurrentDictionary<int, int> _spellOfLevel = new();
        private static readonly ConcurrentDictionary<int, (bool, bool)> _ways = new();

        /// <summary>The spell a spell level is of.</summary>
        private static int SpellOfLevel(int level) => _spellOfLevel.GetOrAdd(level, l => Convert.ToInt32(Scalar("SELECT SpellId FROM SpellLevels WHERE Id = $v;", l) ?? 0L));

        /// <summary>Whether a spell is cast in line, in diagonal (its first grade's).</summary>
        private static (bool InLine, bool InDiagonal) WaysOf(int spell) => _ways.GetOrAdd(spell, s =>
            (Convert.ToInt64(Scalar("SELECT CastInLine FROM SpellLevels WHERE SpellId = $v ORDER BY Grade LIMIT 1;", s) ?? 0L) != 0,
             Convert.ToInt64(Scalar("SELECT CastInDiagonal FROM SpellLevels WHERE SpellId = $v ORDER BY Grade LIMIT 1;", s) ?? 0L) != 0));

        private static object Scalar(string sql, int value)
        {
            try
            {
                using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$v", value);
                var result = command.ExecuteScalar();
                return result is DBNull ? null : result;
            }
            catch
            {
                return null;
            }
        }
    }
}
