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
    /// The divine dimensions' old portals in the Amakna village (DimensionPortals,
    /// DimensionPortalProtocol): among the square's actors, talked to and crossed.
    /// </summary>
    /// <remarks>
    /// Anybody can cross one, as often as they like: a crossing takes to the dimension's entry, as
    /// the Travelers' Tower's portal does.
    /// </remarks>
    public static class DimensionPortalHandler
    {
        /// <summary>The portal's NPC look and the dimension's area, as the actor carries them.</summary>
        private static (int Area, byte[] Look)? Drawing(DimensionPortals.Portal portal)
        {
            var template = Npcs.EnsureTemplate(portal.Dimension.PortalNpc);
            if (template == null) return null;
            var variant = Npcs.Variantes(template.Look).FirstOrDefault();
            byte[] look = variant == null
                ? ConnectionProtocol.BuildNpcLook(0, Array.Empty<long>(), Array.Empty<long>(), Array.Empty<long>())
                : ConnectionProtocol.BuildNpcLook(variant.Bones, variant.Skins, variant.Colors, variant.Scales);
            int area = Interactives.AreaOfSubArea(MapManager.GetMapInfo(portal.Dimension.EntryMap)?.SubAreaId ?? 0);
            return (Math.Max(0, area), look);
        }

        /// <summary>The map's portals among its actors.</summary>
        public static void AddToMap(Pb jss, long mapId)
        {
            foreach (var portal in DimensionPortals.OnMap(mapId))
            {
                if (Drawing(portal) is not { } drawing) continue;
                jss.Msg(5, DimensionPortalProtocol.Actor(portal, drawing.Area, drawing.Look));
            }
        }

        /// <summary>
        /// Talking to a portal (iov 3 on its actor): the dialogue as its NPC (ioc with the portal's
        /// type) and its sentence (ios). False when the actor is no portal.
        /// </summary>
        public static async Task<bool> TalkAsync(NetworkStream stream, long mapId, long contextId)
        {
            var portal = DimensionPortals.ByContext(contextId);
            if (portal == null || portal.MapId != mapId) return false;
            var template = Npcs.EnsureTemplate(portal.Dimension.PortalNpc);
            if (template == null) return false;

            var state = SessionContext.State;
            state.OpenActorDialogue = contextId;
            state.OpenDialogueNpcId = portal.Dimension.PortalNpc;
            state.OpenDialogueMapId = mapId;
            state.OpenDialogueMessage = template.DialogMessageId;
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Ioc, DimensionPortalProtocol.BuildDialog(portal)));
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Ios,
                DimensionPortalProtocol.BuildQuestion(template.DialogMessageId, template.Replies, DateTimeOffset.UtcNow)));
            return true;
        }

        /// <summary>The portal's reply chosen (ioy): the dialogue closed and the portal crossed. False when it is no portal's.</summary>
        public static async Task<bool> ReplyAsync(NetworkStream stream, long reply)
        {
            var state = SessionContext.State;
            var portal = DimensionPortals.ByContext(state.OpenActorDialogue);
            if (portal == null || state.OpenDialogueNpcId != portal.Dimension.PortalNpc) return false;
            var template = Npcs.EnsureTemplate(portal.Dimension.PortalNpc);
            if (template == null || !template.Replies.Contains(reply)) return false;

            state.OpenActorDialogue = 0;
            state.OpenDialogueNpcId = 0;
            state.OpenDialogueMapId = 0;
            state.OpenDialogueMessage = 0;
            await WriteAsync(stream, ConnectionProtocol.Push(Op.Kld, ConnectionProtocol.BuildDialogClosed(ConnectionProtocol.NpcDialogCloseReason)));
            await CrossAsync(stream, portal);
            return true;
        }

        /// <summary>«Usar» on a portal (hhu): crossed, when one stands on its map.</summary>
        public static async Task UseAsync(NetworkStream stream, byte[] frame)
        {
            byte[] body = ConnectionProtocol.ReadPayload(frame, Op.Hhu);
            if (body == null) return;
            var portal = DimensionPortals.OfType(DimensionPortalProtocol.ReadUse(body));
            if (portal == null) return;
            await CrossAsync(stream, portal);
        }

        private static async Task CrossAsync(NetworkStream stream, DimensionPortals.Portal portal)
        {
            var state = SessionContext.State;
            if (state.IsInFight || portal.MapId != state.MapId) return;
            Console.WriteLine($"[Portals] {state.CharacterName} crosses the village's portal to {portal.Dimension.Name}.");
            await TeleportHandler.ToMapAsync(stream, portal.Dimension.EntryMap, portal.Dimension.EntryCell);
        }

        private static Task WriteAsync(NetworkStream stream, byte[] frame)
            => Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, frame);
    }
}
