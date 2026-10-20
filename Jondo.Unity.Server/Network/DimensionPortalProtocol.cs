using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// The divine dimensions' world portals on the wire (DimensionPortals), as the 3.6.10.11 client
    /// reads them: no capture shows one.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   actor (ljx)  f1 { f1 cell }   f2 { f1 { f3 { f2 the portal, f3 the dimension's area } }, f3 look }   f3 actor id
    ///   ioc          f2 the portal's type, f4 map, f5 actor id
    ///   ios          the portal NPC's sentence, its reply, and as parameters the uses left and the
    ///                closing time in unix seconds
    ///   hhu (client) f3 the portal
    /// </code>
    /// </remarks>
    public static class DimensionPortalProtocol
    {
        /// <summary>The portal among its map's actors, with its NPC's look.</summary>
        public static Pb Actor(DimensionPortals.Portal portal, int area, byte[] look)
            => Pb.New()
                .Msg(1, Pb.New().Var(1, portal.Cell))
                .Msg(2, Pb.New()
                    .Msg(1, Pb.New().Msg(DimensionPortals.ActorKind, Pb.New().Var(2, portal.Type).VarIfNotZero(3, area)))
                    .Bytes(3, look))
                .Var(3, portal.ContextId);

        /// <summary>The portal drawn on a map already loaded (jsn): { f1 the actor }.</summary>
        public static byte[] BuildShown(DimensionPortals.Portal portal, int area, byte[] look)
            => Pb.New().Msg(1, Actor(portal, area, look)).Build();

        /// <summary>The dialogue with the portal opened (ioc): its type makes the client show its NPC.</summary>
        public static byte[] BuildDialog(DimensionPortals.Portal portal)
            => Pb.New().Var(2, portal.Type).Var(4, portal.MapId).Var(5, portal.ContextId).Build();

        /// <summary>The portal's sentence (ios): how many crossings are left and when it closes unused.</summary>
        public static byte[] BuildQuestion(DimensionPortals.Portal portal, long messageId, IEnumerable<long> replies)
            => BankProtocol.BuildQuestion(messageId, replies, null, new[]
            {
                portal.UsesLeft.ToString(CultureInfo.InvariantCulture),
                (portal.ClosesMs / 1000).ToString(CultureInfo.InvariantCulture),
            });

        /// <summary>The portal a crossing asks for (hhu f3).</summary>
        public static int ReadUse(byte[] hhu)
            => (int)(ProtoMessage.Parse(hhu).Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 0)?.VarIntValue ?? 0);
    }
}
