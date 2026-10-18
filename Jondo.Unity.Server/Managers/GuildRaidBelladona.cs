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

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Santuario's third enigma, the Reserva de Belladona: six paper boats found in the Obra and
    /// sunk in a battleship game between the Reserva's two boards.
    /// </summary>
    /// <remarks>
    /// THE GUIDE (DPLN): "retrouver un total de 6 bateaux en papier dispersés aléatoirement sur les maps
    /// de la zone"; clicking one, "Dévoiler la position", "retire le bateau de la map et place un
    /// bateau sur l'une des maps de la Réserve de Belladone en [21,17] ou en [19,15]" -- three a board.
    /// Then each board fires at the other's boats in turn, "toujours la map en [21,17] qui commence en
    /// premier", a big ball green or red saying whose turn it is: "il faut cliquer sur deux boules de
    /// votre map pour former une coordonnée" -- a letter A to D and a number 1 to 3. "Toute erreur
    /// réinitialise tous les bateaux coulés ... et votre raid perd 1PV"; the six sunk, 2000 points.
    ///
    /// THE CLIENT'S MAP DATA, shown by its stagings (<see cref="GuildRaidStagings"/>):
    ///
    ///   the Obra: each of its maps has two or three boats, the elements named "bateau_NN", shown by
    ///   "spawn_bateau_NN" and dissolved by "#SD despawn_bateau_NN". They are the client's type 460
    ///   "Barco de papel", offered with "Revelar la posición" (SkillNamesDataRoot 384).
    ///
    ///   the boards: "#SD Spawn_{X}{n}" floats a boat on square Xn, "#SD destruction_{X}{n}" sinks
    ///   it, "bateaux_OFF_ALL" takes them all; "#SD {X}_switch" and "#SD {n}_switch" light the balls,
    ///   which stand where the elements named so are -- the seven clickables of each board, offered
    ///   with "Disparar la bola de cañón" (393); "#SD CANON_SHOOT", "#SD failure"; "#SD Greenlight"
    ///   and "#SD redlight" the turn's ball; "croix_{X}{n}" and "croix_OFF" a square's cross.
    ///
    ///   the centre: "#SD Logo{n}_spawn" for the boats sunk so far, "#SD Logo_despawn".
    ///
    /// INFERRED: a shot marks its square with a cross on the shooting board; a shot at a boat not yet
    /// revealed misses; an error gives the first turn back to [21,17], as the game starts there.
    ///
    /// The goal, "Superar el enigma de la reserva de Belladona", is worth 6: one a boat sunk.
    /// </remarks>
    public static class GuildRaidBelladona
    {
        /// <summary>The Reserva is the Santuario's third zone; its boats are found in the first, the Obra.</summary>
        public const int Zone = 3;
        public const int BoatZone = 1;

        /// <summary>The goal it meets, one a boat: "Superar el enigma de la reserva de Belladona".</summary>
        public const int Goal = 3;

        /// <summary>Six boats, three a board; boards of four letters by three numbers.</summary>
        public const int Boats = 6;
        public const int BoatsPerBoard = 3;
        public const int Letters = 4;
        public const int Numbers = 3;

        /// <summary>The board that fires first: [21,17] (the guide).</summary>
        public const int FirstX = 21, FirstY = 17;

        /// <summary>The Obra's boats: type 460 "Barco de papel", "Revelar la posición". The balls: "Disparar la bola de cañón".</summary>
        public const int BoatType = 460;
        public const int BoatSkill = 184;
        public const int Reveal = 384;
        public const int BallType = -1;
        public const int BallSkill = 184;
        public const int Fire = 393;

        /// <summary>The stagings, by their names in the client's maps.</summary>
        public static string BoatShown(int number) => $"spawn_bateau_{number:00}";
        public static string BoatGone(int number) => $"#SD despawn_bateau_{number:00}";
        public static string Square(int letter, int number) => $"{(char)('A' + letter - 1)}{number}";
        public static string Floating(int letter, int number) => "#SD Spawn_" + Square(letter, number);
        public static string Sunk(int letter, int number) => "#SD destruction_" + Square(letter, number);
        public static string Cross(int letter, int number) => "croix_" + Square(letter, number);
        public static string Ball(string name) => $"#SD {name}_switch";
        public const string NoBoats = "bateaux_OFF_ALL";
        public const string NoCrosses = "croix_OFF";
        public const string Shot = "#SD CANON_SHOOT";
        public const string Failure = "#SD failure";
        public const string YourTurn = "#SD Greenlight";
        public const string NotYourTurn = "#SD redlight";

        /// <summary>Where the raid keeps the boats, the squares, what is revealed and sunk, the turn and the aim.</summary>
        public const string SlotsSequence = "Belladona_Slots";
        public const string PlacesSequence = "Belladona_Places";
        public const string RevealedVariable = "Belladona_Revealed";
        public const string SunkVariable = "Belladona_Sunk";
        public const string TurnVariable = "Belladona_Turn";
        public static string LetterVariable(int board) => "Belladona_Letter" + board;
        public static string NumberVariable(int board) => "Belladona_Number" + board;

        /// <summary>A boat of the Obra: its map, its element, its number on that map.</summary>
        public sealed record Slot(long MapId, Interactives.Element Element, int Number);

        /// <summary>A board: its map, and its seven balls by element -- a letter 1-4 or a number 1-3.</summary>
        public sealed record Board(long MapId, IReadOnlyDictionary<int, (bool IsLetter, int Value)> Balls);

        /// <summary>The enigma as the client's maps lay it out; board 0 fires first.</summary>
        public sealed record Work(IReadOnlyList<Slot> Slots, IReadOnlyList<Board> Boards, long Centre);

        private static readonly ConcurrentDictionary<int, Work> _works = new();

        /// <summary>A raid's Reserva, or null for one without it.</summary>
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
            var slots = new List<Slot>();
            foreach (long map in DatabaseManager.MapsOfSubArea(kind.Floors[BoatZone - 1]).OrderBy(m => m))
            {
                var elements = Interactives.ElementsOf(map);
                foreach (var (name, _, element) in GuildRaidStagings.ElementsOf(map))
                {
                    var match = Regex.Match(name, @"^bateau_(\d+)$");
                    if (!match.Success || element == 0) continue;
                    int number = int.Parse(match.Groups[1].Value);
                    var boat = elements.FirstOrDefault(e => e.Id == element);
                    if (boat.Id != 0 && GuildRaidStagings.Has(map, BoatShown(number)) && slots.All(s => s.Element.Id != boat.Id))
                        slots.Add(new Slot(map, boat, number));
                }
            }

            var reserve = DatabaseManager.MapsOfSubArea(kind.Floors[Zone - 1]).OrderBy(m => m).ToList();
            var boards = reserve.Where(m => GuildRaidStagings.Has(m, Floating(1, 1))).Select(ReadBoard).Where(b => b != null).ToList();
            long first = GuildRaidStagings.MapAt(boards.Select(b => b.MapId), FirstX, FirstY);
            boards = boards.OrderBy(b => b.MapId == first ? 0 : 1).ThenBy(b => b.MapId).ToList();
            long centre = reserve.FirstOrDefault(m => GuildRaidStagings.Has(m, string.Format(GuildRaidMonochrome.StepDone, Boats)));

            if (slots.Count < Boats || boards.Count != 2 || centre == 0) return null;
            return new Work(slots, boards, centre);
        }

        /// <summary>A board's seven balls: the clickables standing where the elements named "A_switch"... "3_switch" are.</summary>
        private static Board ReadBoard(long map)
        {
            var clickable = Interactives.ElementsOf(map);
            var balls = new Dictionary<int, (bool, int)>();
            foreach (var (name, cell, _) in GuildRaidStagings.ElementsOf(map))
            {
                var match = Regex.Match(name, @"^([A-D1-3])_switch$");
                if (!match.Success) continue;
                char c = match.Groups[1].Value[0];
                var ball = clickable.FirstOrDefault(e => e.Cell == cell && !balls.ContainsKey(e.Id)
                                                         && GuildRaidStagings.ElementsOf(map).All(n => n.Element != e.Id));
                if (ball.Id == 0) continue;
                balls[ball.Id] = char.IsLetter(c) ? (true, c - 'A' + 1) : (false, c - '0');
            }
            return balls.Count == Letters + Numbers ? new Board(map, balls) : null;
        }

        // ─── The draw ───────────────────────────────────────────────────────────────────────

        /// <summary>A raid starting: six of the Obra's boats, and the square each will float on, three a board.</summary>
        public static void Draw(RaidInstance raid, Random dice)
        {
            var work = raid == null ? null : WorkOf(Raids.Of(raid.RaidId));
            if (work == null) return;
            var slots = Enumerable.Range(0, work.Slots.Count).OrderBy(_ => dice.Next()).Take(Boats).ToList();
            var places = new List<int>();
            for (int board = 0; board < 2; board++)
                places.AddRange(Enumerable.Range(0, Letters * Numbers).OrderBy(_ => dice.Next()).Take(BoatsPerBoard)
                                          .Select(square => board * Letters * Numbers + square));
            Set(raid, slots, places);
        }

        /// <summary>The boats and their squares as given, standing for the raid (and for the tests).</summary>
        internal static void Set(RaidInstance raid, IReadOnlyList<int> slots, IReadOnlyList<int> places)
        {
            var work = WorkOf(Raids.Of(raid.RaidId));
            if (work == null) return;
            raid.SetSequence(SlotsSequence, slots);
            raid.SetSequence(PlacesSequence, places);
            raid.Set(RevealedVariable, 0);
            raid.Set(SunkVariable, 0);
            raid.Set(TurnVariable, 0);
            for (int board = 0; board < 2; board++)
            {
                raid.Set(LetterVariable(board), 0);
                raid.Set(NumberVariable(board), 0);
            }
            foreach (int slot in slots)
            {
                var boat = work.Slots[slot];
                GuildRaidStagings.Keep(raid, boat.MapId, GuildRaidStagings.IdOf(boat.MapId, BoatShown(boat.Number)));
            }
            KeepTurn(raid, work, 0);
        }

        /// <summary>The boat i (0 to 5) of a raid: its slot in the Obra, its board and square (letter, number).</summary>
        public static (Slot Slot, int Board, int Letter, int Number) BoatOf(RaidInstance raid, Work work, int i)
        {
            int place = raid.SequenceOf(PlacesSequence)[i];
            int board = place / (Letters * Numbers), square = place % (Letters * Numbers);
            return (work.Slots[raid.SequenceOf(SlotsSequence)[i]], board, square / Numbers + 1, square % Numbers + 1);
        }

        private static bool Has(RaidInstance raid, string variable, int i) => (raid.Get(variable) & (1L << i)) != 0;

        // ─── The Obra's boats ───────────────────────────────────────────────────────────────

        /// <summary>Which of a raid's six boats an Obra element is, or -1.</summary>
        public static int BoatAt(RaidInstance raid, long mapId, int elementId)
        {
            var work = raid == null ? null : WorkOf(Raids.Of(raid.RaidId));
            if (work == null) return -1;
            var slots = raid.SequenceOf(SlotsSequence);
            for (int i = 0; i < slots.Count; i++)
                if (work.Slots[slots[i]].MapId == mapId && work.Slots[slots[i]].Element.Id == elementId) return i;
            return -1;
        }

        /// <summary>The board an element is a ball of, 0 or 1, or -1.</summary>
        public static int BoardAt(RaidKind kind, long mapId, int elementId)
        {
            var work = WorkOf(kind);
            if (work == null) return -1;
            for (int b = 0; b < work.Boards.Count; b++)
                if (work.Boards[b].MapId == mapId && work.Boards[b].Balls.ContainsKey(elementId)) return b;
            return -1;
        }

        /// <summary>Whether a raid member is offered an element: one of the raid's boats still in the Obra, or a ball.</summary>
        public static bool Offers(long characterId, long mapId, int elementId)
        {
            var raid = GuildRaidManager.RaidOf(characterId);
            if (raid == null) return false;
            int boat = BoatAt(raid, mapId, elementId);
            return boat >= 0 ? !Has(raid, RevealedVariable, boat) : BoardAt(Raids.Of(raid.RaidId), mapId, elementId) >= 0;
        }

        /// <summary>Whether a raid's element can be used: a boat not revealed; a ball of the board whose turn it is, the enigma not solved.</summary>
        public static bool Usable(RaidInstance raid, long mapId, int elementId)
        {
            if (raid == null) return false;
            int boat = BoatAt(raid, mapId, elementId);
            if (boat >= 0) return !Has(raid, RevealedVariable, boat);
            int board = BoardAt(Raids.Of(raid.RaidId), mapId, elementId);
            return board >= 0 && !Solved(raid) && raid.Get(TurnVariable) == board;
        }

        public static bool Usable(long characterId, long mapId, int elementId) => Usable(GuildRaidManager.RaidOf(characterId), mapId, elementId);

        public static bool Solved(RaidInstance raid) => raid != null && raid.GoalAt(Goal) >= Boats;

        public static Task<bool> PressAsync(NetworkStream stream, long characterId, long mapId, int elementId, int skillId)
            => PressAsync(stream, GuildRaidManager.RaidOf(characterId), mapId, elementId, skillId);

        /// <summary>A boat revealed or a ball clicked by a raid member. False when it is none of the raid's.</summary>
        public static async Task<bool> PressAsync(NetworkStream stream, RaidInstance raid, long mapId, int elementId, int skillId)
        {
            var work = raid == null ? null : WorkOf(Raids.Of(raid.RaidId));
            int boat = work == null ? -1 : BoatAt(raid, mapId, elementId);
            int board = work == null ? -1 : BoardAt(Raids.Of(raid.RaidId), mapId, elementId);
            if (boat < 0 && board < 0) return false;
            if (stream != null)
            {
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                    ConnectionProtocol.Push(Op.Iwi, ConnectionProtocol.BuildInteractiveUseEnded(elementId, skillId)));
            }
            if (boat >= 0) await RevealAsync(raid, work, boat);
            else await AimAsync(raid, work, board, work.Boards[board].Balls[elementId]);
            return true;
        }

        /// <summary>A boat revealed: gone from the Obra, floating on its board's square.</summary>
        private static async Task RevealAsync(RaidInstance raid, Work work, int i)
        {
            if (Has(raid, RevealedVariable, i)) return;
            raid.Set(RevealedVariable, raid.Get(RevealedVariable) | (1L << i));
            var (slot, board, letter, number) = BoatOf(raid, work, i);
            GuildRaidStagings.Drop(raid, slot.MapId, GuildRaidStagings.IdOf(slot.MapId, BoatShown(slot.Number)));
            await GuildRaidStagings.PlayAsync(raid, slot.MapId, BoatGone(slot.Number), keep: false);
            await GuildRaidStagings.RedeclareAsync(raid, slot.MapId, slot.Element.Id, InteractiveActionKind.RaidPaperBoat, usable: false);
            await GuildRaidStagings.PlayAsync(raid, work.Boards[board].MapId, Floating(letter, number), keep: true);
            Program.LogDebug($"[Raids] {raid.Uuid}: a paper boat revealed, on board {board} at {Square(letter, number)}.");
        }

        // ─── The battle ─────────────────────────────────────────────────────────────────────

        /// <summary>A ball clicked on the board whose turn it is: a letter and a number aim, and the cannon fires.</summary>
        private static async Task AimAsync(RaidInstance raid, Work work, int board, (bool IsLetter, int Value) ball)
        {
            if (Solved(raid) || raid.Get(TurnVariable) != board) return;
            long map = work.Boards[board].MapId;
            raid.Set(ball.IsLetter ? LetterVariable(board) : NumberVariable(board), ball.Value);
            string name = ball.IsLetter ? ((char)('A' + ball.Value - 1)).ToString() : ball.Value.ToString();
            await GuildRaidStagings.PlayAsync(raid, map, Ball(name), keep: false);

            int letter = (int)raid.Get(LetterVariable(board)), number = (int)raid.Get(NumberVariable(board));
            if (letter == 0 || number == 0) return;
            raid.Set(LetterVariable(board), 0);
            raid.Set(NumberVariable(board), 0);
            await FireAsync(raid, work, board, letter, number);
        }

        /// <summary>
        /// A board fires at the other's square: a boat there sinks, and the turn passes; nothing there
        /// is an error -- every boat sunk floats again, a life is lost and [21,17] fires first again.
        /// </summary>
        private static async Task FireAsync(RaidInstance raid, Work work, int board, int letter, int number)
        {
            int target = 1 - board;
            long map = work.Boards[board].MapId, targetMap = work.Boards[target].MapId;
            await GuildRaidStagings.PlayAsync(raid, map, Shot, keep: false);
            await GuildRaidStagings.PlayAsync(raid, map, Cross(letter, number), keep: true);

            int hit = Enumerable.Range(0, Boats).FirstOrDefault(i =>
            {
                var (_, b, l, n) = BoatOf(raid, work, i);
                return b == target && l == letter && n == number && Has(raid, RevealedVariable, i) && !Has(raid, SunkVariable, i);
            }, -1);

            if (hit >= 0)
            {
                raid.Set(SunkVariable, raid.Get(SunkVariable) | (1L << hit));
                await GuildRaidStagings.PlayAsync(raid, targetMap, Sunk(letter, number), keep: true);
                int sunk = Enumerable.Range(0, Boats).Count(i => Has(raid, SunkVariable, i));
                await GuildRaidStagings.PlayAsync(raid, work.Centre, string.Format(GuildRaidMonochrome.StepDone, sunk), keep: true);
                await GuildRaidManager.AdvanceGoalAsync(raid, Goal, sunk);
                Program.LogDebug($"[Raids] {raid.Uuid}: board {board} sinks the boat at {Square(letter, number)} ({sunk} sunk).");
                if (sunk == Boats)
                {
                    await GuildRaidManager.GainHealthAsync(raid, 1);
                    Console.WriteLine($"[Raid] {raid.Uuid}: the Reserva de Belladona's boats are all sunk.");
                    return;
                }
                await SetTurnAsync(raid, work, target);
                return;
            }

            // A miss: everything sunk floats again.
            await GuildRaidStagings.PlayAsync(raid, map, Failure, keep: false);
            for (int i = 0; i < Boats; i++)
            {
                if (!Has(raid, SunkVariable, i)) continue;
                var (_, b, l, n) = BoatOf(raid, work, i);
                GuildRaidStagings.Drop(raid, work.Boards[b].MapId, GuildRaidStagings.IdOf(work.Boards[b].MapId, Sunk(l, n)));
            }
            raid.Set(SunkVariable, 0);
            raid.SetGoal(Goal, 0);
            foreach (var each in work.Boards)
            {
                for (int l = 1; l <= Letters; l++)
                for (int n = 1; n <= Numbers; n++)
                    GuildRaidStagings.Drop(raid, each.MapId, GuildRaidStagings.IdOf(each.MapId, Cross(l, n)));
                await GuildRaidStagings.PlayAsync(raid, each.MapId, NoCrosses, keep: false);
                await GuildRaidStagings.PlayAsync(raid, each.MapId, NoBoats, keep: false);
            }
            for (int i = 0; i < Boats; i++)
            {
                if (!Has(raid, RevealedVariable, i)) continue;
                var (_, b, l, n) = BoatOf(raid, work, i);
                await GuildRaidStagings.PlayAsync(raid, work.Boards[b].MapId, Floating(l, n), keep: true);
            }
            GuildRaidStagings.Drop(raid, work.Centre, Enumerable.Range(1, Boats)
                .Select(step => GuildRaidStagings.IdOf(work.Centre, string.Format(GuildRaidMonochrome.StepDone, step))).ToArray());
            await GuildRaidStagings.PlayAsync(raid, work.Centre, GuildRaidMonochrome.StepsUndone, keep: false);
            Console.WriteLine($"[Raid] {raid.Uuid}: board {board} misses {Square(letter, number)}; the boats float again, a life lost.");
            await Handlers.GuildRaidHandler.TellRunningStateAsync(raid);
            await GuildRaidManager.LoseHealthAsync(raid, 1);
            await SetTurnAsync(raid, work, 0);
        }

        /// <summary>The turn given to a board: its ball green, the other's red, and its balls offered.</summary>
        private static async Task SetTurnAsync(RaidInstance raid, Work work, int board)
        {
            int before = (int)raid.Get(TurnVariable);
            raid.Set(TurnVariable, board);
            for (int b = 0; b < work.Boards.Count; b++)
            {
                long map = work.Boards[b].MapId;
                string light = b == board ? YourTurn : NotYourTurn;
                GuildRaidStagings.Drop(raid, map, GuildRaidStagings.IdOf(map, b == board ? NotYourTurn : YourTurn));
                await GuildRaidStagings.PlayAsync(raid, map, light, keep: true);
                if (before == board) continue;
                foreach (int ball in work.Boards[b].Balls.Keys)
                    await GuildRaidStagings.RedeclareAsync(raid, map, ball, InteractiveActionKind.RaidCannonBall, usable: b == board);
            }
        }

        /// <summary>The turn's lights as they stand: green on the board whose turn it is, red on the other.</summary>
        private static void KeepTurn(RaidInstance raid, Work work, int board)
        {
            for (int b = 0; b < work.Boards.Count; b++)
            {
                long map = work.Boards[b].MapId;
                GuildRaidStagings.Drop(raid, map, GuildRaidStagings.IdOf(map, b == board ? NotYourTurn : YourTurn));
                GuildRaidStagings.Keep(raid, map, GuildRaidStagings.IdOf(map, b == board ? YourTurn : NotYourTurn));
            }
        }
    }
}
