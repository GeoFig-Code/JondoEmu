using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The guidebook's «Sugerencias» tab (<see cref="ActivitySuggestions"/>): hmo asks for cards,
    /// hmn answers them, hmp locks or frees one.
    /// </summary>
    public static class ActivitySuggestionHandler
    {
        private static readonly Random _dice = new();

        /// <summary>hmo { 1 level to, 2 cards, 3 level from, 4 category, 5 area }: the cards, answered with hmn.</summary>
        public static async Task SuggestAsync(NetworkStream stream, byte[] payload)
        {
            byte[]? hmo = ConnectionProtocol.ReadPayload(payload, "hmo");
            if (hmo == null) return;
            long Field(int n) => ProtoMessage.Parse(hmo).Fields.FirstOrDefault(f => f.FieldNumber == n && f.WireType == 0)?.VarIntValue ?? 0;

            int levelTo = (int)Field(1), cards = (int)Field(2), levelFrom = (int)Field(3);
            int category = (int)Field(4), area = (int)Field(5);
            if (cards <= 0) cards = 50;

            var locked = ActivitySuggestions.LockedBy(SessionContext.State.CharacterId);
            var drawn = ActivitySuggestions.Draw(levelFrom, levelTo, category, area, cards, locked,
                                                 map => Interactives.AreaOfSubArea(DatabaseManager.SubAreaOfMap(map)),
                                                 DateTimeOffset.UtcNow, _dice);
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, ConnectionProtocol.Push("hmn", Build(locked.ToArray(), drawn.ToArray())));
            Console.WriteLine($"[Suggestions] {SessionContext.State.CharacterName}: levels {levelFrom}-{levelTo}, " +
                              $"category {category}, area {area}: {drawn.Count} card(s), {locked.Count} locked.");
        }

        /// <summary>hmn { 1 the locked cards, 2 the others }.</summary>
        internal static byte[] Build(int[] locked, int[] others)
        {
            var hmn = Pb.New();
            if (locked.Length > 0) hmn.Packed(1, locked.Select(i => (long)i).ToList());
            if (others.Length > 0) hmn.Packed(2, others.Select(i => (long)i).ToList());
            return hmn.Build();
        }

        /// <summary>
        /// hmp { 2 the activity }: a card's lock. The client's action carries whether it locks
        /// (cvu), but only the activity was seen going into hmp, so a 1 in f1 locks, and with
        /// nothing there the lock is turned over.
        /// </summary>
        public static Task LockAsync(NetworkStream stream, byte[] payload)
        {
            byte[]? hmp = ConnectionProtocol.ReadPayload(payload, "hmp");
            if (hmp == null) return Task.CompletedTask;
            var fields = ProtoMessage.Parse(hmp).Fields;
            int activity = (int)(fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 0)?.VarIntValue ?? 0);
            if (activity == 0) return Task.CompletedTask;

            long me = SessionContext.State.CharacterId;
            var flag = fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 0);
            bool lockIt = flag != null ? flag.VarIntValue != 0 : !ActivitySuggestions.LockedBy(me).Contains(activity);
            ActivitySuggestions.SetLocked(me, activity, lockIt);
            return Task.CompletedTask;
        }
    }
}
