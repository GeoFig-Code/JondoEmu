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
    /// The Santuario's second enigma, the Enclave de los protectores: the statues of its four gardens
    /// set again on the Patio de Efedra's four miniature boards, then the protector the statue that
    /// rises names, in the raid's colour, beaten.
    /// </summary>
    /// <remarks>
    /// THE GUIDE (DPLN): "On copie la disposition de 3 statues sur les 4 maps d'une zone pour la
    /// reproduire sur les 4 maps d'une autre zone" -- the garden to the right of the Enclave's portal
    /// on the board to the right of the Patio's, and so on --; "Certaines cases des jardins
    /// miniatures peuvent être cliquées pour faire apparaître une statue"; with the three placed "il
    /// faut cliquer sur le bouton lumineux qui apparaît pour valider la map": red and a life lost if
    /// wrong, green if right. The four done, "une statue va apparaître au centre de la zone du clos",
    /// with the Obra's colour under it if that enigma is solved, and "il faut donc affronter le
    /// monstre qui correspond à la statue affichée et à la couleur": the wrong one, "le combat est
    /// directement perdu et votre raid perd 1PV". The enigma is worth 2000 points and a life.
    ///
    /// THE CLIENT'S MAP DATA, shown by its stagings (<see cref="GuildRaidStagings"/>):
    ///
    ///   the gardens: "S_st{a}_mp_{m}_{b}" stands statue a (1-3) of garden m (1-4) on its spot b
    ///   (1-3). Each garden is its own map -- 1 the left of the portal's, 2 above, 3 the right, 4
    ///   below -- and its neighbours see parts of it, so a garden is shown on every map that has.
    ///
    ///   the boards: "#SD S_statue{a}_{b}_ON|OFF" put statuette a on spot b of a board or take it
    ///   away; "#SD Bouton ON|OFF" light its button; "#SD S_statueTRUE|FALSE" and "St{a}_{b}TRUE" are
    ///   the green and the red. Board k is the map whose button is the element named "T_bouton{k}",
    ///   the garden it copies; its nine squares are click boxes beside the statuette they toggle
    ///   ("T_statue{a}_{b}"), named on one board and matched by nearness on the others.
    ///
    ///   the end: "Pop statue N" and "Halo_rouge|vert|bleu|orange" on the Enclave's centre, "#SD
    ///   S_statueFinale_N" on the Patio's -- the statue N among the four kinds, numbered as the
    ///   Vigilante's "Check Mob" numbers them (GuildRaidGuardians.CompanionByMob); "#SD
    ///   Logo{n}_spawn" marks the gardens done on the Enclave's centre.
    ///
    /// THE PROTECTORS are the four kinds in the four colours, one of each on each garden's map as the
    /// guide lists them (<see cref="Placements"/>). In each kind the client's ids go escarlata,
    /// azulado, ambarino, viridina; the raid's colours are 1 fire, 2 air, 3 water, 4 earth ("color
    /// == 1 / feu"...): red, green, blue and amber. The world's own groups on these maps are not the
    /// raid's and go as it starts.
    ///
    /// The goal, "Superar el enigma del enclave de los protectores", is worth 2: one for the gardens,
    /// one for the protector.
    /// </remarks>
    public static class GuildRaidEnclave
    {
        /// <summary>The Enclave is the Santuario's second zone; its boards are in the fourth, the Patio de Efedra.</summary>
        public const int Zone = 2;
        public const int BoardZone = 4;

        /// <summary>The goal it meets: "Superar el enigma del enclave de los protectores", worth 2.</summary>
        public const int Goal = 2;

        /// <summary>Four gardens, three statues each, three spots for each.</summary>
        public const int Gardens = 4;
        public const int Statues = 3;
        public const int Spots = 3;

        /// <summary>A board's squares, in the client's maps; its button is the element named "T_bouton{k}".</summary>
        public const int SquareGfx = 122030;

        /// <summary>The squares: unnamed, "Utilizar". The button: the client's type 461 "Botón", "Pulsar".</summary>
        public const int SquareType = -1;
        public const int SquareSkill = 184;
        public const int ButtonType = 461;
        public const int ButtonSkill = 184;
        public const int Press = 392;

        /// <summary>A protector is fought by four at most: "Le combat se fait à 4 maximum mais solotable".</summary>
        public const int PeopleCap = 4;

        /// <summary>How long the board's statuettes take to fall when it is wrong, before they go.</summary>
        public static readonly TimeSpan FallTime = TimeSpan.FromSeconds(2);

        /// <summary>The stagings, by their names in the client's maps.</summary>
        public static string GardenStatue(int statue, int garden, int spot) => $"S_st{statue}_mp_{garden}_{spot}";
        public static string SquareOn(int statue, int spot) => $"#SD S_statue{statue}_{spot}_ON";
        public static string SquareOff(int statue, int spot) => $"#SD S_statue{statue}_{spot}_OFF";
        public static string StatueRight(int statue, int spot) => $"St{statue}_{spot}TRUE";
        public static string FinalStatue(int kind) => $"Pop statue {kind}";
        public static string PatioStatue(int kind) => $"#SD S_statueFinale_{kind}";
        public const string BoardRight = "#SD S_statueTRUE";
        public const string BoardWrong = "#SD S_statueFALSE";
        public const string ButtonOn = "#SD Bouton ON";
        public const string ButtonOff = "#SD Bouton OFF";

        /// <summary>The halo each of the raid's colours, 1 to 4, gives the statue.</summary>
        public static readonly IReadOnlyList<string> Halos = new[] { "Halo_rouge", "Halo_vert", "Halo_bleu", "Halo_orange" };

        /// <summary>A kind's four colours in the order of the client's ids: escarlata, azulado, ambarino, viridina -- 1, 3, 4, 2.</summary>
        public static readonly IReadOnlyList<int> ColourOfIdOrder = new[] { 1, 3, 4, 2 };

        /// <summary>
        /// Where each protector stands, from the guide: by the kind's number (1 Machacamelia, 2
        /// Trilipán, 3 Daliana, 4 Muguetégida), the colour, and the garden map's position.
        /// </summary>
        public static readonly IReadOnlyList<(int Kind, int Colour, int X, int Y)> Placements = new[]
        {
            (3, 4, 11, 21), (1, 4, 11, 19), (4, 4, 12, 20), (2, 4, 10, 20),     // ambré
            (3, 3, 11, 19), (1, 3, 11, 21), (4, 3, 10, 20), (2, 3, 12, 20),     // azuré
            (3, 1, 10, 20), (1, 1, 12, 20), (4, 1, 11, 21), (2, 1, 11, 19),     // écarlate
            (3, 2, 12, 20), (1, 2, 10, 20), (4, 2, 11, 19), (2, 2, 11, 21),     // viridine
        };

        /// <summary>Where the raid keeps the gardens, the statue, the boards and what is done.</summary>
        public const string SpotsSequence = "Enclave_Spots";
        public const string StatueVariable = "Enclave_Statue";
        public const string ValidatedVariable = "Enclave_Validated";
        public const string FoughtVariable = "Enclave_Fought";
        public static string BoardVariable(int board) => "Enclave_Board" + board;

        /// <summary>A miniature board: its garden's number, its map, its button and its squares.</summary>
        public sealed record Board(int Index, long MapId, Interactives.Element Button,
                                   IReadOnlyDictionary<int, (int Statue, int Spot)> Squares);

        /// <summary>The enigma as the client's maps lay it out.</summary>
        public sealed record Work(IReadOnlyList<long> Maps, long Centre, IReadOnlyDictionary<int, Board> Boards,
                                  IReadOnlyList<long> PatioMaps);

        private static readonly ConcurrentDictionary<int, Work> _works = new();

        /// <summary>A raid's Enclave, or null for one without it.</summary>
        public static Work WorkOf(RaidKind kind)
        {
            if (kind == null || kind.Floors.Count < BoardZone) return null;
            if (_works.TryGetValue(kind.Id, out var known)) return known;
            var work = Read(kind);
            if (work != null) _works[kind.Id] = work;
            return work;
        }

        private static Work Read(RaidKind kind)
        {
            var maps = DatabaseManager.MapsOfSubArea(kind.Floors[Zone - 1]).OrderBy(m => m)
                                      .Where(m => GuildRaidStagings.Has(m, GardenStatue(1, 1, 1))).ToList();
            long centre = maps.FirstOrDefault(m => GuildRaidStagings.Has(m, FinalStatue(1)));
            var patio = DatabaseManager.MapsOfSubArea(kind.Floors[BoardZone - 1]).OrderBy(m => m).ToList();
            var boards = new Dictionary<int, Board>();
            foreach (long map in patio.Where(m => GuildRaidStagings.Has(m, SquareOn(1, 1))))
            {
                var board = ReadBoard(map);
                if (board != null) boards[board.Index] = board;
            }
            if (maps.Count == 0 || centre == 0 || boards.Count != Gardens) return null;
            return new Work(maps, centre, boards, patio);
        }

        /// <summary>
        /// A board: its number from its button's name, and its squares -- those named after their
        /// statuette first, then each of the others by the statuette nearest to it.
        /// </summary>
        public static Board ReadBoard(long map)
        {
            var named = GuildRaidStagings.ElementsOf(map);
            var elements = Interactives.ElementsOf(map);
            Interactives.Element button = default;
            int index = 0;
            foreach (var (name, _, element) in named)
            {
                var match = Regex.Match(name, @"^T_bouton(\d+)$");
                if (!match.Success || element == 0) continue;
                index = int.Parse(match.Groups[1].Value);
                button = elements.FirstOrDefault(e => e.Id == element);
            }
            if (index == 0 || button.Id == 0) return null;

            var statuettes = new List<(int Statue, int Spot, int Cell, int Element)>();
            foreach (var (name, cell, element) in named)
            {
                var match = Regex.Match(name, @"^T_statue(\d)_(\d)$");
                if (match.Success) statuettes.Add((int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), cell, element));
            }
            var squares = new Dictionary<int, (int, int)>();
            var placed = new HashSet<(int, int)>();
            var boxes = elements.Where(e => e.Gfx == SquareGfx).ToList();
            foreach (var box in boxes)
            {
                var own = statuettes.FirstOrDefault(s => s.Element == box.Id);
                if (own.Statue == 0 || !placed.Add((own.Statue, own.Spot))) continue;
                squares[box.Id] = (own.Statue, own.Spot);
            }
            var pairs = from box in boxes
                        where !squares.ContainsKey(box.Id)
                        from s in statuettes
                        where s.Element == 0
                        orderby Jondo.Unity.World.Maps.MapGeometry.Distance(box.Cell, s.Cell), box.Id
                        select (Box: box.Id, s.Statue, s.Spot);
            foreach (var (box, statue, spot) in pairs.ToList())
            {
                if (squares.ContainsKey(box) || placed.Contains((statue, spot))) continue;
                squares[box] = (statue, spot);
                placed.Add((statue, spot));
            }
            return squares.Count == Statues * Spots ? new Board(index, map, button, squares) : null;
        }

        // ─── The draw ───────────────────────────────────────────────────────────────────────

        /// <summary>A raid starting: each garden's statues on spots at random, and the statue that will rise.</summary>
        public static void Draw(RaidInstance raid, Random dice)
        {
            if (raid == null || WorkOf(Raids.Of(raid.RaidId)) == null) return;
            var spots = Enumerable.Range(0, Gardens * Statues).Select(_ => dice.Next(1, Spots + 1)).ToList();
            Set(raid, spots, dice.Next(1, 5));
        }

        /// <summary>The gardens and the statue as given, standing for the raid (and for the tests).</summary>
        internal static void Set(RaidInstance raid, IReadOnlyList<int> spots, int statue)
        {
            var work = WorkOf(Raids.Of(raid.RaidId));
            if (work == null) return;
            raid.SetSequence(SpotsSequence, spots);
            raid.Set(StatueVariable, statue);
            raid.Set(ValidatedVariable, 0);
            raid.Set(FoughtVariable, 0);
            for (int board = 1; board <= Gardens; board++) raid.Set(BoardVariable(board), 0);

            for (int garden = 1; garden <= Gardens; garden++)
            for (int a = 1; a <= Statues; a++)
            foreach (long map in work.Maps)
                GuildRaidStagings.Keep(raid, map, GuildRaidStagings.IdOf(map, GardenStatue(a, garden, SpotOf(raid, garden, a))));
        }

        /// <summary>The spot, 1 to 3, of a garden's statue for a raid.</summary>
        public static int SpotOf(RaidInstance raid, int garden, int statue)
        {
            var spots = raid.SequenceOf(SpotsSequence);
            int i = (garden - 1) * Statues + (statue - 1);
            return i < spots.Count ? spots[i] : 0;
        }

        // ─── The protectors ─────────────────────────────────────────────────────────────────

        /// <summary>The protector of a kind (1 to 4) in a colour (1 to 4), or 0.</summary>
        public static int ProtectorOf(int kind, int colour)
        {
            if (!GuildRaidGuardians.CompanionByMob().TryGetValue(kind, out int companion)) return 0;
            var variants = ColouredOf(companion);
            int order = ColourOfIdOrder.ToList().IndexOf(colour);
            return order >= 0 && order < variants.Count ? variants[order] : 0;
        }

        /// <summary>The protector a raid has to beat: its statue's kind in its colour.</summary>
        public static int RightProtector(RaidInstance raid)
            => raid == null ? 0 : ProtectorOf((int)raid.Get(StatueVariable), (int)raid.Get(GuildRaidGuardians.ColourVariable));

        /// <summary>Whether a monster is one of the sixteen protectors.</summary>
        public static bool IsProtector(int monster)
            => Enumerable.Range(1, 4).Any(kind => Enumerable.Range(1, 4).Any(colour => ProtectorOf(kind, colour) == monster));

        private static readonly ConcurrentDictionary<int, IReadOnlyList<int>> _coloured = new();

        /// <summary>A companion kind's four coloured monsters ("Trilipán escarlata"...), by id.</summary>
        private static IReadOnlyList<int> ColouredOf(int companion) => _coloured.GetOrAdd(companion, ReadColoured);

        private static IReadOnlyList<int> ReadColoured(int companion)
        {
            var ids = new List<int>();
            try
            {
                using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT m.Id FROM MonsterTemplates m JOIN Translations t ON t.Key = CAST(m.NameId AS TEXT) " +
                    "WHERE t.Text LIKE (SELECT t2.Text FROM MonsterTemplates m2 JOIN Translations t2 " +
                    "ON t2.Key = CAST(m2.NameId AS TEXT) WHERE m2.Id = $id) || ' %' ORDER BY m.Id;";
                command.Parameters.AddWithValue("$id", companion);
                using var reader = command.ExecuteReader();
                while (reader.Read()) ids.Add((int)reader.GetInt64(0));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Raids] The protectors of kind {companion} could not be read: {ex.Message}");
            }
            return ids;
        }

        /// <summary>
        /// The Enclave as a raid starts: the world's groups gone from its maps, and the sixteen
        /// protectors alone, each on its garden's map. How many were placed.
        /// </summary>
        public static int Populate(RaidKind kind, Random dice)
        {
            var work = WorkOf(kind);
            if (work == null) return 0;
            foreach (long map in DatabaseManager.MapsOfSubArea(kind.Floors[Zone - 1]))
                if (!GuildRaidManager.IsArena(map)) MobSpawnManager.ClearMap(map);

            int placed = 0;
            foreach (var group in Placements.GroupBy(p => (p.X, p.Y)))
            {
                long map = GuildRaidStagings.MapAt(work.Maps, group.Key.X, group.Key.Y);
                if (map == 0) continue;
                var cells = GuildRaidSanctuary.SpreadCells(GuildRaidSanctuary.CellsFor(map, group.Count()), group.Count(), dice);
                int i = 0;
                foreach (var (protectorKind, colour, _, _) in group)
                {
                    int monster = ProtectorOf(protectorKind, colour);
                    if (monster == 0 || i >= cells.Count) continue;
                    if (MobSpawnManager.SpawnComposed(map, new[] { (monster, 0) }, cells[i++]) != null) placed++;
                }
            }
            Program.LogDebug($"[Raids] The Enclave's {placed} protectors placed.");
            return placed;
        }

        // ─── The boards ─────────────────────────────────────────────────────────────────────

        /// <summary>The board an element of a map belongs to -- one of its squares or its button --, or null.</summary>
        public static Board BoardAt(RaidKind kind, long mapId, int elementId)
            => WorkOf(kind)?.Boards.Values.FirstOrDefault(b => b.MapId == mapId
                                                           && (b.Button.Id == elementId || b.Squares.ContainsKey(elementId)));

        public static bool Validated(RaidInstance raid, int board) => (raid.Get(ValidatedVariable) & (1L << (board - 1))) != 0;

        private static int Bit(int statue, int spot) => (statue - 1) * Spots + (spot - 1);

        /// <summary>The squares a raid has lit on a board.</summary>
        public static List<(int Statue, int Spot)> Lit(RaidInstance raid, int board)
        {
            long mask = raid.Get(BoardVariable(board));
            var lit = new List<(int, int)>();
            for (int a = 1; a <= Statues; a++)
            for (int b = 1; b <= Spots; b++)
                if ((mask & (1L << Bit(a, b))) != 0) lit.Add((a, b));
            return lit;
        }

        /// <summary>Whether a raid member may use a board's element: a square while the board is not done, its button with three lit.</summary>
        public static bool Usable(RaidInstance raid, long mapId, int elementId)
        {
            var board = raid == null ? null : BoardAt(Raids.Of(raid.RaidId), mapId, elementId);
            if (board == null || Validated(raid, board.Index)) return false;
            return board.Button.Id != elementId || Lit(raid, board.Index).Count == Statues;
        }

        public static bool Offers(long characterId, long mapId, int elementId)
        {
            var raid = GuildRaidManager.RaidOf(characterId);
            return raid != null && BoardAt(Raids.Of(raid.RaidId), mapId, elementId) != null;
        }

        public static bool Usable(long characterId, long mapId, int elementId)
            => Usable(GuildRaidManager.RaidOf(characterId), mapId, elementId);

        public static Task<bool> PressAsync(NetworkStream stream, long characterId, long mapId, int elementId, int skillId)
            => PressAsync(stream, GuildRaidManager.RaidOf(characterId), mapId, elementId, skillId);

        /// <summary>A square or a button of a board worked by a raid member. False when it is none of the raid's.</summary>
        public static async Task<bool> PressAsync(NetworkStream stream, RaidInstance raid, long mapId, int elementId, int skillId)
        {
            var board = raid == null ? null : BoardAt(Raids.Of(raid.RaidId), mapId, elementId);
            if (board == null) return false;
            if (stream != null)
            {
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                    ConnectionProtocol.Push(Op.Iwi, ConnectionProtocol.BuildInteractiveUseEnded(elementId, skillId)));
            }
            if (Validated(raid, board.Index)) return true;

            if (board.Button.Id == elementId) await ValidateAsync(raid, board);
            else await ToggleAsync(raid, board, board.Squares[elementId]);
            return true;
        }

        /// <summary>A square clicked: its statuette stands or goes, and the button lights with three standing.</summary>
        private static async Task ToggleAsync(RaidInstance raid, Board board, (int Statue, int Spot) square)
        {
            int bit = Bit(square.Statue, square.Spot);
            long mask = raid.Get(BoardVariable(board.Index)) ^ (1L << bit);
            raid.Set(BoardVariable(board.Index), mask);
            if ((mask & (1L << bit)) != 0)
            {
                await GuildRaidStagings.PlayAsync(raid, board.MapId, SquareOn(square.Statue, square.Spot), keep: true);
            }
            else
            {
                GuildRaidStagings.Drop(raid, board.MapId, GuildRaidStagings.IdOf(board.MapId, SquareOn(square.Statue, square.Spot)));
                await GuildRaidStagings.PlayAsync(raid, board.MapId, SquareOff(square.Statue, square.Spot), keep: false);
            }
            await LightAsync(raid, board, Lit(raid, board.Index).Count == Statues);
        }

        /// <summary>The board's button lit, or put out, and offered or not.</summary>
        private static async Task LightAsync(RaidInstance raid, Board board, bool lit)
        {
            int on = GuildRaidStagings.IdOf(board.MapId, ButtonOn);
            bool was = GuildRaidStagings.Standing(raid, board.MapId).Contains(on);
            if (lit == was) return;
            if (lit)
            {
                await GuildRaidStagings.PlayAsync(raid, board.MapId, ButtonOn, keep: true);
            }
            else
            {
                GuildRaidStagings.Drop(raid, board.MapId, on);
                await GuildRaidStagings.PlayAsync(raid, board.MapId, ButtonOff, keep: false);
            }
            await GuildRaidStagings.RedeclareAsync(raid, board.MapId, board.Button.Id, InteractiveActionKind.RaidGardenButton, lit);
        }

        /// <summary>
        /// The button pressed with three statuettes standing: the garden copied turns the board
        /// green and counts on the Enclave's centre; a wrong one makes them fall and costs a life.
        /// </summary>
        private static async Task ValidateAsync(RaidInstance raid, Board board)
        {
            var lit = Lit(raid, board.Index);
            if (lit.Count != Statues) return;
            var work = WorkOf(Raids.Of(raid.RaidId));
            bool right = Enumerable.Range(1, Statues).All(a => lit.Contains((a, SpotOf(raid, board.Index, a))));

            if (right)
            {
                raid.Set(ValidatedVariable, raid.Get(ValidatedVariable) | (1L << (board.Index - 1)));
                GuildRaidStagings.Drop(raid, board.MapId, GuildRaidStagings.IdOf(board.MapId, ButtonOn));
                await GuildRaidStagings.PlayAsync(raid, board.MapId, BoardRight, keep: true);
                foreach (var (a, b) in lit)
                    await GuildRaidStagings.PlayAsync(raid, board.MapId, StatueRight(a, b), keep: true);
                foreach (int square in board.Squares.Keys)
                    await GuildRaidStagings.RedeclareAsync(raid, board.MapId, square, InteractiveActionKind.RaidGardenSquare, usable: false);
                await GuildRaidStagings.RedeclareAsync(raid, board.MapId, board.Button.Id, InteractiveActionKind.RaidGardenButton, usable: false);

                int done = Enumerable.Range(1, Gardens).Count(g => Validated(raid, g));
                await GuildRaidStagings.PlayAsync(raid, work.Centre, string.Format(GuildRaidMonochrome.StepDone, done), keep: true);
                Program.LogDebug($"[Raids] {raid.Uuid}: the Enclave's garden {board.Index} is set right on its board ({done} done).");
                if (done == Gardens) await GardensDoneAsync(raid, work);
                return;
            }

            // Wrong: the statuettes fall, the board is bare again, and a life is lost.
            raid.Set(BoardVariable(board.Index), 0);
            foreach (var (a, b) in lit)
                GuildRaidStagings.Drop(raid, board.MapId, GuildRaidStagings.IdOf(board.MapId, SquareOn(a, b)));
            GuildRaidStagings.Drop(raid, board.MapId, GuildRaidStagings.IdOf(board.MapId, ButtonOn));
            await GuildRaidStagings.PlayAsync(raid, board.MapId, BoardWrong, keep: false);
            await GuildRaidStagings.RedeclareAsync(raid, board.MapId, board.Button.Id, InteractiveActionKind.RaidGardenButton, usable: false);
            Console.WriteLine($"[Raid] {raid.Uuid}: the Enclave's garden {board.Index} set wrong on its board; a life lost.");
            await GuildRaidManager.LoseHealthAsync(raid, 1);

            // Once they have fallen, their squares are offered again -- those not clicked since.
            _ = Task.Run(async () =>
            {
                await Task.Delay(FallTime);
                try
                {
                    long now = raid.Get(BoardVariable(board.Index));
                    foreach (var (a, b) in lit.Where(s => (now & (1L << Bit(s.Statue, s.Spot))) == 0))
                        await GuildRaidStagings.PlayAsync(raid, board.MapId, SquareOff(a, b), keep: false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Raid] The Enclave's board {board.Index} could not be cleared: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// The four gardens copied: the statue rises on the Enclave's centre -- in the raid's colour
        /// once the Obra has given it -- and lights on the Patio's, and the goal moves on.
        /// </summary>
        private static async Task GardensDoneAsync(RaidInstance raid, Work work)
        {
            int statue = (int)raid.Get(StatueVariable);
            await GuildRaidStagings.PlayAsync(raid, work.Centre, FinalStatue(statue), keep: true);
            await ShowColourAsync(raid, work);
            foreach (long map in work.PatioMaps)
                await GuildRaidStagings.PlayAsync(raid, map, PatioStatue(statue), keep: true);
            Console.WriteLine($"[Raid] {raid.Uuid}: the Enclave's gardens are copied; statue {statue} rises.");
            await StepAsync(raid);
        }

        /// <summary>The statue's halo in the raid's colour, once both the statue stands and the Obra is solved.</summary>
        private static async Task ShowColourAsync(RaidInstance raid, Work work)
        {
            int colour = (int)raid.Get(GuildRaidGuardians.ColourVariable);
            if (!GuildRaidMonochrome.Solved(raid) || colour < 1 || colour > Halos.Count) return;
            if (Enumerable.Range(1, Gardens).Any(g => !Validated(raid, g))) return;
            await GuildRaidStagings.PlayAsync(raid, work.Centre, Halos[colour - 1], keep: true);
        }

        /// <summary>The Obra solved: the statue, if it stands, takes its colour.</summary>
        public static async Task ObraSolvedAsync(RaidInstance raid)
        {
            var work = raid == null ? null : WorkOf(Raids.Of(raid.RaidId));
            if (work != null) await ShowColourAsync(raid, work);
        }

        /// <summary>One of the goal's two steps done; with both, the enigma is solved and gives a life back.</summary>
        private static async Task StepAsync(RaidInstance raid)
        {
            int before = raid.GoalAt(Goal);
            await GuildRaidManager.AdvanceGoalAsync(raid, Goal, before + 1);
            if (before + 1 < 2) return;
            await GuildRaidManager.GainHealthAsync(raid, 1);
            Console.WriteLine($"[Raid] {raid.Uuid}: the Enclave de los protectores is solved.");
        }

        // ─── The protector's fight ──────────────────────────────────────────────────────────

        /// <summary>The fights against a wrong protector, lost from their start.</summary>
        private static readonly ConcurrentDictionary<long, byte> _wrongFights = new();

        /// <summary>A fight just built: one against a protector the raid's statue and colour do not name is marked lost.</summary>
        public static void OnFightCreated(FightInstance fight)
        {
            var protector = fight?.Rojo.FirstOrDefault(m => m.IsMonster && !m.EsInvocado && IsProtector(m.MonsterId));
            if (protector == null) return;
            var player = fight.Azul.Concat(fight.Rojo).FirstOrDefault(f => !f.IsMonster && !f.EsInvocado);
            var raid = player == null ? null : GuildRaidManager.RaidOf(player.Id);
            if (raid == null || raid.RaidId != Raids.EternalGardens) return;
            fight.PeopleCap = PeopleCap;
            if (protector.MonsterId != RightProtector(raid)) _wrongFights[fight.FightId] = 0;
        }

        /// <summary>Whether a fight starting is lost at once (and it is no longer marked).</summary>
        public static bool LostFromTheStart(FightInstance fight) => fight != null && _wrongFights.TryRemove(fight.FightId, out _);

        /// <summary>A fight won: the right protector beaten is the goal's other step.</summary>
        public static async Task BeatenAsync(RaidInstance raid, IReadOnlyDictionary<int, int> beaten)
        {
            if (raid == null || raid.Get(FoughtVariable) != 0) return;
            int right = RightProtector(raid);
            if (right == 0 || !beaten.ContainsKey(right)) return;
            raid.Set(FoughtVariable, 1);
            Console.WriteLine($"[Raid] {raid.Uuid}: the Enclave's protector {right} is beaten.");
            await StepAsync(raid);
        }
    }
}
