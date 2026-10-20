using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The treasure hunts on the wire (TreasureHunts, TreasureHuntProtocol): the divine dimensions'
    /// legendary hunts launched from the Travelers' Tower's models, their flags, digs and give-ups,
    /// and the fight against the fissure at their end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model as the capture has it: the element in use (iwn), then the answer (hhk) -- 3 for a
    /// dimension whose portal one already has, 2 with a hunt under way -- and with 5 the hunt (hhf).
    /// A give-up within the first minute is refused with info 436, «termina tu misión actual o
    /// espera {0} minuto/s más».
    /// </para>
    /// <para>
    /// INFERRED, nothing captured: the flags' and digs' answers by the client's own texts for each
    /// code, the hunt sent again after each, its end (hgm) when lost, given up or won. The fight at
    /// the end is the hunt's fissure («Fisura de X», the legendary row's monster) and three
    /// monsters of the dimension's first zone at the hunt's level -- DofusPourLesNoobs speaks of
    /// «les monstres qui se cachent à la fin». Whoever wins it gets the dimension's portal for good,
    /// those who joined included, as the guide says; the hunter, the hunt's chest.
    /// </para>
    /// </remarks>
    public static class TreasureHuntHandler
    {
        /// <summary>«Imposible: termina tu misión actual o espera {0} minuto/s más.»</summary>
        public const int GiveUpTooSoon = 436;

        /// <summary>How many of the dimension's monsters come out with the fissure.</summary>
        public const int FissureCompany = 3;

        private static readonly Random _dice = new();

        private static Task WriteAsync(NetworkStream stream, byte[] frame)
            => Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, frame);

        private static Task SendHuntAsync(NetworkStream stream, TreasureHunts.Hunt hunt)
            => WriteAsync(stream, ConnectionProtocol.Push(Op.Hhf, TreasureHuntProtocol.BuildHunt(hunt)));

        private static Task SendFinishedAsync(NetworkStream stream, int type)
            => WriteAsync(stream, ConnectionProtocol.Push(Op.Hgm, TreasureHuntProtocol.BuildFinished(type)));

        /// <summary>What the model answers a character: the hunt to launch, or why not.</summary>
        public static int Answer(TravelersTower.Dimension dimension, long characterId, int level,
                                 Func<long, int, bool> has, Func<long, int, TreasureHunts.Hunt?> hunting)
        {
            if (has(characterId, dimension.Alteration)) return TreasureHunts.NotAvailable;
            if (level < TravelersTower.LocateLevel) return TreasureHunts.NotAvailable;
            if (hunting(characterId, TreasureHunts.Legendary) != null) return TreasureHunts.AlreadyHaveQuest;
            if (LegendaryHunts.Of(dimension.Legendary) == null) return TreasureHunts.NoQuestFound;
            return TreasureHunts.RequestOk;
        }

        /// <summary>A model's «Localizar una fisura»: its dimension's legendary hunt.</summary>
        public static async Task LocateFissureAsync(NetworkStream stream, RegisteredInteractive interactive, InteractiveAction action)
        {
            var state = SessionContext.State;
            var dimension = TravelersTower.ModelOf(interactive.Element.Id);
            if (dimension == null) return;

            await SessionRegistry.BroadcastToMapAsync(state.MapId, ConnectionProtocol.Push(Op.Iwn,
                ConnectionProtocol.BuildElementInUse(interactive.Element.Id, action.SkillId, state.CharacterId)));

            int answer = Answer(dimension, state.CharacterId, state.CharacterLevel, Alterations.Has, TreasureHunts.Get);
            TreasureHunts.Hunt? hunt = null;
            if (answer == TreasureHunts.RequestOk)
            {
                hunt = TreasureHunts.Begin(TreasureHunts.World, state.CharacterId, TreasureHunts.Legendary,
                                           dimension.Legendary, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _dice);
                if (hunt == null) answer = TreasureHunts.NoQuestFound;
            }

            await WriteAsync(stream, ConnectionProtocol.Push(Op.Hhk,
                TreasureHuntProtocol.BuildRequestAnswer(TreasureHunts.Legendary, answer)));
            if (hunt == null) return;

            TreasureHunts.Save(hunt);
            Console.WriteLine($"[Hunts] {state.CharacterName} sets out to find {dimension.Name}'s fissure from {hunt.StartMap}.");
            await SendHuntAsync(stream, hunt);
        }

        /// <summary>A flag put on a step (hhv), on the map one stands on.</summary>
        public static async Task FlagAsync(NetworkStream stream, byte[] frame)
        {
            byte[] body = ConnectionProtocol.ReadPayload(frame, Op.Hhv);
            if (body == null) return;
            var (type, index) = TreasureHuntProtocol.ReadFlagPut(body);
            await FlagChangedAsync(stream, type, index, hunt =>
                TreasureHunts.PutFlag(TreasureHunts.World, hunt, index, SessionContext.State.MapId));
        }

        /// <summary>A flag taken away (hhh).</summary>
        public static async Task RemoveFlagAsync(NetworkStream stream, byte[] frame)
        {
            byte[] body = ConnectionProtocol.ReadPayload(frame, Op.Hhh);
            if (body == null) return;
            var (type, index) = TreasureHuntProtocol.ReadFlagRemoved(body);
            await FlagChangedAsync(stream, type, index, hunt => TreasureHunts.RemoveFlag(hunt, index));
        }

        private static async Task FlagChangedAsync(NetworkStream stream, int type, int index, Func<TreasureHunts.Hunt, int> change)
        {
            var hunt = TreasureHunts.Get(SessionContext.State.CharacterId, type);
            int result = hunt == null || SessionContext.State.IsInFight ? TreasureHunts.FlagImpossible : change(hunt);
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Hhq, TreasureHuntProtocol.BuildFlagAnswer(type, index, result)));
            if (hunt == null || result != TreasureHunts.FlagOk) return;
            TreasureHunts.Save(hunt);
            await SendHuntAsync(stream, hunt);
        }

        /// <summary>
        /// A dig (hgj): the stage's flags judged. The next stage, a try spent, the hunt lost -- or, on
        /// the last stage, the fight against the fissure.
        /// </summary>
        public static async Task DigAsync(NetworkStream stream, byte[] frame)
        {
            byte[] body = ConnectionProtocol.ReadPayload(frame, Op.Hgj);
            if (body == null) return;
            int type = TreasureHuntProtocol.ReadType(body);
            var state = SessionContext.State;
            var hunt = TreasureHunts.Get(state.CharacterId, type);

            var (result, wrong) = hunt == null || state.IsInFight
                ? (TreasureHunts.DigImpossible, 0)
                : TreasureHunts.Dig(TreasureHunts.World, hunt, state.MapId, _dice);
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Hht, TreasureHuntProtocol.BuildDigAnswer(type, result, wrong)));
            if (hunt == null) return;

            switch (result)
            {
                case TreasureHunts.DigLost:
                    TreasureHunts.End(state.CharacterId, type);
                    Console.WriteLine($"[Hunts] {state.CharacterName} has lost the hunt.");
                    await SendFinishedAsync(stream, type);
                    return;
                case TreasureHunts.DigWrong:
                case TreasureHunts.DigNewStage:
                    TreasureHunts.Save(hunt);
                    await SendHuntAsync(stream, hunt);
                    return;
                case TreasureHunts.DigFinished:
                    TreasureHunts.Save(hunt);
                    await SendHuntAsync(stream, hunt);
                    await FightTheFissureAsync(stream, hunt);
                    return;
            }
        }

        /// <summary>The fissure and its company on the map one dug on, the fight opened at once.</summary>
        private static async Task FightTheFissureAsync(NetworkStream stream, TreasureHunts.Hunt hunt)
        {
            var legendary = LegendaryHunts.Of(hunt.Legendary);
            var dimension = TravelersTower.LegendaryOf(hunt.Legendary);
            if (legendary == null) return;

            var roster = dimension == null ? Array.Empty<int>() : MobSpawnManager.RosterOfMap(dimension.EntryMap);
            var members = FissureFight(legendary, roster, MobSpawnManager.GetMonsterData, _dice);
            var group = MobSpawnManager.ComposeOffMap(members);
            if (group == null)
            {
                Console.WriteLine($"[Hunts] The fissure {legendary.Monster} could not be put together.");
                return;
            }
            long mapId = SessionContext.State.MapId;
            Console.WriteLine($"[Hunts] {SessionContext.State.CharacterName} faces the fissure of hunt {hunt.Legendary} on {mapId}: " +
                              string.Join(", ", members.Select(m => $"{m.Monster}#{m.Grade}")) + ".");
            await FightHandler.InitiateFightFromMobCollision(stream, group, mapId);
        }

        /// <summary>
        /// The fight at a fissure hunt's end: the fissure, and up to <see cref="FissureCompany"/> of
        /// its dimension's monsters, each at the grade nearest the hunt's level.
        /// </summary>
        public static List<(int Monster, int Grade)> FissureFight(LegendaryHunts.Definition legendary, IReadOnlyList<int> roster,
                                                                  Func<int, MobSpawnManager.MonsterData?> data, Random dice)
        {
            int GradeNear(MobSpawnManager.MonsterData monster)
            {
                int best = 0;
                int grades = Math.Min(monster.Grades.Count, MobSpawnManager.MobMember.MaxWrittenGrades);
                for (int i = 1; i < grades; i++)
                    if (Math.Abs(monster.Grades[i].Level - legendary.Level) < Math.Abs(monster.Grades[best].Level - legendary.Level))
                        best = i;
                return best;
            }

            var members = new List<(int Monster, int Grade)>();
            if (data(legendary.Monster) is { Grades.Count: > 0 } fissure)
                members.Add((legendary.Monster, GradeNear(fissure)));

            var company = roster.Distinct().Select(id => data(id)).Where(m => m is { Grades.Count: > 0 }).ToList();
            for (int i = 0; i < FissureCompany && company.Count > 0; i++)
            {
                var monster = company[dice.Next(company.Count)]!;
                members.Add((monster.Id, GradeNear(monster)));
            }
            return members;
        }

        /// <summary>A give-up (hgn): refused within its first minute, otherwise the hunt over.</summary>
        public static async Task GiveUpAsync(NetworkStream stream, byte[] frame)
        {
            byte[] body = ConnectionProtocol.ReadPayload(frame, Op.Hgn);
            if (body == null) return;
            int type = TreasureHuntProtocol.ReadType(body);
            var state = SessionContext.State;
            var hunt = TreasureHunts.Get(state.CharacterId, type);
            if (hunt == null) return;

            int wait = TreasureHunts.MinutesBeforeGivingUp(hunt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (wait > 0)
            {
                await WriteAsync(stream, ConnectionProtocol.Push(Op.Lqn,
                    ConnectionProtocol.BuildInfoMessage(InfoMessages.Warning, GiveUpTooSoon, wait.ToString())));
                return;
            }
            TreasureHunts.End(state.CharacterId, type);
            Console.WriteLine($"[Hunts] {state.CharacterName} gives the hunt up.");
            await SendFinishedAsync(stream, type);
        }

        /// <summary>On entering the world: the hunts under way, as they stand.</summary>
        public static async Task OnWorldEntryAsync(NetworkStream stream, long characterId)
        {
            foreach (var hunt in TreasureHunts.All(characterId))
                await SendHuntAsync(stream, hunt);
        }

        /// <summary>
        /// A fight over, for one who fought it: a fissure beaten gives its dimension's portal, and
        /// ends the hunt of whoever was after it, with its chest.
        /// </summary>
        public static async Task FightEndedAsync(NetworkStream stream, FightInstance fight, bool won)
        {
            if (!won) return;
            long who = SessionContext.State.CharacterId;
            foreach (var monster in fight.Rojo)
            {
                if (monster.EsInvocado || monster.MonsterId <= 0) continue;
                var legendary = LegendaryHunts.EndingWith(monster.MonsterId);
                var dimension = legendary == null ? null : TravelersTower.LegendaryOf(legendary.Id);
                if (dimension == null) continue;

                if (!Alterations.Has(who, dimension.Alteration))
                {
                    await AlterationHandler.GainAsync(stream, dimension.Alteration);
                    Console.WriteLine($"[Hunts] {SessionContext.State.CharacterName} has found {dimension.Name}'s fissure: its portal is open to him.");
                }

                var hunt = TreasureHunts.Get(who, TreasureHunts.Legendary);
                if (hunt != null && hunt.Legendary == legendary!.Id && hunt.ReadyToFight)
                {
                    TreasureHunts.End(who, TreasureHunts.Legendary);
                    if (legendary.Chest > 0) await Equipment.GiveAsync(stream, legendary.Chest, 1);
                    await SendFinishedAsync(stream, TreasureHunts.Legendary);
                }
                return;
            }
        }
    }
}
