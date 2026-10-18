using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Content;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Santuario's first enigma, the Obra monocromática: four pedestals in the Enclave show two
    /// objects each, and the Obra's paper flowers are activated in their order.
    /// </summary>
    /// <remarks>
    /// THE GUIDE (DPLN): "On repère quel type d'objet se trouvent sur des piédestaux numérotés de I à
    /// IV (2 par piédestal) dans le clos. On détermine l'ordre des fleurs en papier à cliquer dans
    /// l'ouvrage monochrome grâce à l'ordre des objets sur les piédestaux. On obtient une couleur".
    /// Each of the Obra's eight maps around its centre has a giant object; of each pedestal's two
    /// objects, one's map holds a flower; the four are activated pedestal by pedestal. A wrong one
    /// flashes red and costs the raid a life; the four done, the zone takes its colour and the raid
    /// gains its 2000 points and a life back.
    ///
    /// ALL OF IT IS THE CLIENT'S MAP DATA, hidden at alpha 0 and shown by its stagings
    /// (<see cref="GuildRaidStagings"/>):
    ///
    ///   the pedestals, on the Enclave's maps: "pop_&lt;object&gt;_pN" puts an object on pedestal N,
    ///   "pop_&lt;object&gt;_pNbis" the second one -- I and III on [11,21], II and IV on [12,20], and
    ///   IV seen from [11,20] too, which has its stagings: a pedestal shows on every map that has.
    ///   The objects are lanterne, kamas, bague, regle, crayon, arakne, fils and bougie.
    ///
    ///   the flowers, one possible on each object's map: "pop_fleur_&lt;object&gt;" shows it, and
    ///   names the map's object; "#SD fleur_true" turns it green, "#SD fleur_false" takes the
    ///   green away and flashes it red. The element under it is the client's type 459 "Flor de
    ///   papel", offered with "Activar la flor de papel" (SkillNamesDataRoot 383) only where a flower
    ///   stands and while it is not green.
    ///
    ///   the centre: "#SD Logo1_spawn" to "#SD Logo4_spawn" mark the flowers done so far on its
    ///   statue, "#SD Logo_despawn" takes them away.
    ///
    ///   every Obra map: "Couleur_rouge|verte|bleu|orange" and "#SD Colorisation", the black and
    ///   white giving way to the colour. The colour is the raid's (GuildRaidGuardians.ColourVariable),
    ///   numbered as the client's own spells number it: "color == 1 / feu", 2 air, 3 eau, 4 terre --
    ///   red, green, blue and orange, the Centinela's escarlata, viridina, azulado and ambarino.
    ///
    /// Which goal it meets: the raid's first, "Superar el enigma de la obra monocromática".
    /// </remarks>
    public static class GuildRaidMonochrome
    {
        /// <summary>The Obra is the Santuario's first zone; its pedestals stand in the second, the Enclave.</summary>
        public const int Zone = 1;
        public const int PedestalZone = 2;

        /// <summary>The goal it meets: "Superar el enigma de la obra monocromática".</summary>
        public const int Goal = 1;

        /// <summary>Four pedestals, two objects each.</summary>
        public const int Pedestals = 4;

        /// <summary>The paper flowers' graphics in the Obra's maps.</summary>
        public static readonly IReadOnlyList<int> FlowerGfx = new[] { 303016, 303021 };

        /// <summary>The client's type 459 "Flor de papel", "Utilizar" shown as "Activar la flor de papel".</summary>
        public const int FlowerType = 459;
        public const int FlowerSkill = 184;
        public const int ActivateFlower = 383;

        /// <summary>The stagings, by their names in the client's maps.</summary>
        public const string FlowerPrefix = "pop_fleur_";
        public const string FlowerGreen = "#SD fleur_true";
        public const string FlowerRed = "#SD fleur_false";
        public const string StepDone = "#SD Logo{0}_spawn";
        public const string StepsUndone = "#SD Logo_despawn";
        public const string Colouring = "#SD Colorisation";

        /// <summary>The colour each of the raid's colours, 1 to 4, gives the Obra.</summary>
        public static readonly IReadOnlyList<string> Colours = new[] { "Couleur_rouge", "Couleur_verte", "Couleur_bleu", "Couleur_orange" };

        /// <summary>Where the raid keeps the pedestals' objects, the flowers and how far they have gone.</summary>
        public const string PedestalsSequence = "Obra_Pedestals";
        public const string FlowersSequence = "Obra_Flowers";
        public const string ProgressVariable = "Obra_Progress";

        /// <summary>The pedestal object's staging: the first of pedestal N, or its second ("bis").</summary>
        public static string OnPedestal(string objectName, int pedestal, bool second)
            => $"pop_{objectName}_p{pedestal}{(second ? "bis" : "")}";

        /// <summary>The Obra as the client's maps lay it out.</summary>
        public sealed record Work(
            IReadOnlyList<string> Objects,
            IReadOnlyDictionary<string, (long MapId, Interactives.Element Flower)> Flowers,
            long Centre,
            IReadOnlyList<long> Maps,
            IReadOnlyDictionary<int, IReadOnlyList<long>> PedestalMaps);

        private static readonly ConcurrentDictionary<int, Work> _works = new();

        /// <summary>A raid's Obra, or null for one without it.</summary>
        public static Work WorkOf(RaidKind kind)
        {
            if (kind == null || kind.Floors.Count < PedestalZone) return null;
            if (_works.TryGetValue(kind.Id, out var known)) return known;
            var work = Read(kind);
            if (work != null) _works[kind.Id] = work;
            return work;
        }

        private static Work Read(RaidKind kind)
        {
            var maps = DatabaseManager.MapsOfSubArea(kind.Floors[Zone - 1]).OrderBy(m => m).ToList();
            var flowers = new Dictionary<string, (long, Interactives.Element)>();
            long centre = 0;
            foreach (long map in maps)
            {
                if (GuildRaidStagings.IdOf(map, string.Format(StepDone, 1)) != 0) centre = map;
                foreach (var (_, name) in GuildRaidStagings.Of(map))
                {
                    if (!name.StartsWith(FlowerPrefix)) continue;
                    var flower = Interactives.ElementsOf(map).FirstOrDefault(e => FlowerGfx.Contains(e.Gfx));
                    if (flower.Id != 0) flowers[name[FlowerPrefix.Length..]] = (map, flower);
                }
            }

            var objects = flowers.Keys.OrderBy(o => o, StringComparer.Ordinal).ToList();
            var pedestalMaps = new Dictionary<int, IReadOnlyList<long>>();
            var enclave = DatabaseManager.MapsOfSubArea(kind.Floors[PedestalZone - 1]).OrderBy(m => m).ToList();
            for (int pedestal = 1; pedestal <= Pedestals && objects.Count > 0; pedestal++)
            {
                var showing = enclave.Where(map => objects.All(o => GuildRaidStagings.IdOf(map, OnPedestal(o, pedestal, false)) != 0
                                                                 && GuildRaidStagings.IdOf(map, OnPedestal(o, pedestal, true)) != 0))
                                     .ToList();
                if (showing.Count > 0) pedestalMaps[pedestal] = showing;
            }

            if (objects.Count != Pedestals * 2 || centre == 0 || pedestalMaps.Count != Pedestals) return null;
            return new Work(objects, flowers, centre, maps, pedestalMaps);
        }

        // ─── The draw ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A raid starting: the eight objects on the four pedestals in a random order, the flower of
        /// each pedestal on the map of one of its two, and all of it standing for the raid to see.
        /// </summary>
        public static void Draw(RaidInstance raid, Random dice)
        {
            var work = raid == null ? null : WorkOf(Raids.Of(raid.RaidId));
            if (work == null) return;
            var order = Enumerable.Range(0, work.Objects.Count).OrderBy(_ => dice.Next()).ToList();
            var flowers = Enumerable.Range(0, Pedestals).Select(p => order[p * 2 + dice.Next(2)]).ToList();
            Set(raid, order, flowers);
        }

        /// <summary>The pedestals' objects and the flowers as given, standing for the raid (and for the tests).</summary>
        internal static void Set(RaidInstance raid, IReadOnlyList<int> pedestals, IReadOnlyList<int> flowers)
        {
            var work = WorkOf(Raids.Of(raid.RaidId));
            if (work == null) return;
            raid.SetSequence(PedestalsSequence, pedestals);
            raid.SetSequence(FlowersSequence, flowers);
            raid.Set(ProgressVariable, 0);

            for (int pedestal = 1; pedestal <= Pedestals; pedestal++)
            foreach (long map in work.PedestalMaps[pedestal])
            for (int slot = 0; slot < 2; slot++)
            {
                string objectName = work.Objects[pedestals[(pedestal - 1) * 2 + slot]];
                GuildRaidStagings.Keep(raid, map, GuildRaidStagings.IdOf(map, OnPedestal(objectName, pedestal, slot == 1)));
            }
            foreach (int flower in flowers)
            {
                var (map, _) = work.Flowers[work.Objects[flower]];
                GuildRaidStagings.Keep(raid, map, GuildRaidStagings.IdOf(map, FlowerPrefix + work.Objects[flower]));
            }
        }

        // ─── The flowers ────────────────────────────────────────────────────────────────────

        /// <summary>The pedestal, 0 to 3, whose flower an element is for a raid, or -1 when no flower of the raid's stands there.</summary>
        public static int PedestalOf(RaidInstance raid, long mapId, int elementId)
        {
            var work = raid == null ? null : WorkOf(Raids.Of(raid.RaidId));
            if (work == null) return -1;
            var flowers = raid.SequenceOf(FlowersSequence);
            for (int pedestal = 0; pedestal < flowers.Count; pedestal++)
            {
                if (flowers[pedestal] < 0 || flowers[pedestal] >= work.Objects.Count) continue;
                var (map, flower) = work.Flowers[work.Objects[flowers[pedestal]]];
                if (map == mapId && flower.Id == elementId) return pedestal;
            }
            return -1;
        }

        /// <summary>Whether the enigma is solved for a raid.</summary>
        public static bool Solved(RaidInstance raid) => raid != null && raid.GoalAt(Goal) >= 1;

        /// <summary>Whether a flower is offered to a raid: one of its four, standing there.</summary>
        public static bool Offers(RaidInstance raid, long mapId, int elementId) => PedestalOf(raid, mapId, elementId) >= 0;

        /// <summary>Whether a raid's flower can still be activated: not green yet, the enigma not solved.</summary>
        public static bool Usable(RaidInstance raid, long mapId, int elementId)
        {
            int pedestal = PedestalOf(raid, mapId, elementId);
            return pedestal >= 0 && !Solved(raid) && pedestal >= raid.Get(ProgressVariable);
        }

        public static bool Offers(long characterId, long mapId, int elementId)
            => Offers(GuildRaidManager.RaidOf(characterId), mapId, elementId);

        public static bool Usable(long characterId, long mapId, int elementId)
            => Usable(GuildRaidManager.RaidOf(characterId), mapId, elementId);

        public static Task<bool> PressAsync(NetworkStream stream, long characterId, long mapId, int elementId, int skillId)
            => PressAsync(stream, GuildRaidManager.RaidOf(characterId), mapId, elementId, skillId);

        /// <summary>
        /// A flower activated: the next pedestal's turns green and marks the centre's statue, and the
        /// fourth gives the zone its colour; any other flashes red, takes the green ones away and costs
        /// the raid a life. False when this is no flower of the raid's.
        /// </summary>
        public static async Task<bool> PressAsync(NetworkStream stream, RaidInstance raid, long mapId, int elementId, int skillId)
        {
            var work = raid == null ? null : WorkOf(Raids.Of(raid.RaidId));
            int pedestal = PedestalOf(raid, mapId, elementId);
            if (work == null || pedestal < 0) return false;

            if (stream != null)
            {
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                    ConnectionProtocol.Push(Op.Iwi, ConnectionProtocol.BuildInteractiveUseEnded(elementId, skillId)));
            }
            if (Solved(raid)) return true;

            int done = (int)raid.Get(ProgressVariable);
            if (pedestal < done) return true;           // green already

            if (pedestal == done)
            {
                raid.Set(ProgressVariable, ++done);
                await GuildRaidStagings.PlayAsync(raid, mapId, FlowerGreen, keep: true);
                await RedeclareAsync(raid, mapId, elementId, usable: false);
                await GuildRaidStagings.PlayAsync(raid, work.Centre, string.Format(StepDone, done), keep: true);
                Program.LogDebug($"[Raids] {raid.Uuid}: the Obra's flower of pedestal {pedestal + 1} is green.");
                if (done == Pedestals) await SolveAsync(raid, work, mapId);
                return true;
            }

            // Out of order: the green ones go, the wrong one flashes red, and a life is lost.
            raid.Set(ProgressVariable, 0);
            var flowers = raid.SequenceOf(FlowersSequence);
            for (int green = 0; green < done; green++)
            {
                var (map, flower) = work.Flowers[work.Objects[flowers[green]]];
                GuildRaidStagings.Drop(raid, map, GuildRaidStagings.IdOf(map, FlowerGreen));
                await GuildRaidStagings.PlayAsync(raid, map, FlowerRed, keep: false);
                await RedeclareAsync(raid, map, flower.Id, usable: true);
            }
            await GuildRaidStagings.PlayAsync(raid, mapId, FlowerRed, keep: false);
            GuildRaidStagings.Drop(raid, work.Centre, Enumerable.Range(1, Pedestals)
                .Select(step => GuildRaidStagings.IdOf(work.Centre, string.Format(StepDone, step))).ToArray());
            await GuildRaidStagings.PlayAsync(raid, work.Centre, StepsUndone, keep: false);
            Console.WriteLine($"[Raid] {raid.Uuid}: the Obra's flower of pedestal {pedestal + 1} out of order ({done} done); a life lost.");
            await GuildRaidManager.LoseHealthAsync(raid, 1);
            return true;
        }

        /// <summary>The four flowers done: the goal met, a life back, and the Obra in the raid's colour.</summary>
        private static async Task SolveAsync(RaidInstance raid, Work work, long mapId)
        {
            await GuildRaidManager.AdvanceGoalAsync(raid, Goal, 1);
            await GuildRaidManager.GainHealthAsync(raid, 1);

            int colour = (int)raid.Get(GuildRaidGuardians.ColourVariable);
            foreach (long map in work.Maps)
            {
                if (colour >= 1 && colour <= Colours.Count)
                    await GuildRaidStagings.PlayAsync(raid, map, Colours[colour - 1], keep: true);
                await GuildRaidStagings.PlayAsync(raid, map, Colouring, keep: true);
            }

            var goal = GuildRaidCatalogue.GoalsOf(raid.RaidId).FirstOrDefault(g => g.Id == Goal);
            if (goal != null)
            {
                foreach (long member in raid.Members.ToList())
                {
                    var session = SessionRegistry.FindByCharacter(member);
                    if (session == null || session.State.MapId != mapId) continue;
                    await session.SendAsync(ConnectionProtocol.Push(Op.Lqn, ConnectionProtocol.BuildNotice(
                        Handlers.CommandTexts.Get(session.State.Language, "raid.goal.met", (object)goal.Name))));
                }
            }
            Console.WriteLine($"[Raid] {raid.Uuid}: the Obra monocromática is solved, colour {colour}.");
        }

        /// <summary>A flower offered again, or no more, to the raid's members on its map.</summary>
        private static async Task RedeclareAsync(RaidInstance raid, long mapId, int elementId, bool usable)
        {
            var interactive = InteractiveRegistry.OnMap(mapId).FirstOrDefault(i => i.Element.Id == elementId);
            var action = interactive?.Actions.FirstOrDefault(a => a.Kind == InteractiveActionKind.RaidFlower);
            if (action == null) return;
            await GuildRaidStagings.TellRaidOnMapAsync(raid, mapId, ConnectionProtocol.Push(Op.Iwm,
                ConnectionProtocol.BuildElementRedeclared(action.SkillInstanceId, action.SkillId, elementId,
                                                          interactive.Type, usable, action.SkillNameId)));
        }
    }
}
