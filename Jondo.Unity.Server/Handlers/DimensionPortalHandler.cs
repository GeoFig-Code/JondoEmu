using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The divine dimensions' portals in the world (DimensionPortals, DimensionPortalProtocol):
    /// among their maps' actors, talked to, crossed, and moved when spent or left unused.
    /// </summary>
    /// <remarks>
    /// Anybody can cross one: the portal is a way in, not the tower's for-good access. A crossing
    /// takes to the dimension's entry, as the tower's portal does. The ones on the map are told
    /// when a portal leaves (kmu) or arrives (jsn); a clock looks every minute for the ones left a
    /// day unused.
    /// </remarks>
    public static class DimensionPortalHandler
    {
        private static readonly Random _dice = new();
        private static Timer? _clock;
        private static int _ticking;

        private static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>
        /// Where a portal can stand on a map: its inner walkable cells, with nothing clickable and no
        /// NPC on them. The monster groups are left to walk round it, as they do round each other.
        /// </summary>
        private static IReadOnlyList<int> CellsOf(long mapId)
        {
            var taken = Npcs.Of(mapId).Select(n => n.Cell).ToHashSet();
            return MobSpawnManager.GetInnerWalkableCells(mapId).Where(c => !taken.Contains(c)).ToList();
        }

        /// <summary>The portals placed, and their clock started. At the server's start, when the settings want them.</summary>
        public static void Start()
        {
            if (!ServerSettings.Current.WorldPortals)
            {
                Console.WriteLine("[Portals] No portals in the world: the settings leave only the tower's.");
                return;
            }
            try
            {
                DimensionPortals.Load(TreasureHunts.World, CellsOf, NowMs, _dice);
                foreach (var portal in DimensionPortals.All)
                    Console.WriteLine($"[Portals] The portal to {portal.Dimension.Name} is on {portal.MapId}, " +
                                      $"cell {portal.Cell}, {portal.UsesLeft} crossings left.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Portals] The portals could not be placed: {ex.Message}");
            }
            _clock ??= new Timer(_ =>
            {
                if (Interlocked.Exchange(ref _ticking, 1) != 0) return;
                try { TickAsync().GetAwaiter().GetResult(); }
                catch (Exception ex) { Console.WriteLine($"[Portals] The clock failed: {ex.Message}"); }
                finally { Interlocked.Exchange(ref _ticking, 0); }
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>The portals left a day unused, closed and opened elsewhere.</summary>
        public static async Task TickAsync()
        {
            foreach (var portal in DimensionPortals.All)
            {
                if (!DimensionPortals.Expired(portal, NowMs)) continue;
                Console.WriteLine($"[Portals] Nobody has crossed the portal to {portal.Dimension.Name} for a day.");
                await MoveAsync(portal);
            }
        }

        /// <summary>A portal closed where it was and opened somewhere else, both maps told.</summary>
        private static async Task MoveAsync(DimensionPortals.Portal portal)
        {
            long from = DimensionPortals.Move(portal, TreasureHunts.World, CellsOf, NowMs, _dice);
            if (from == 0) return;
            await SessionRegistry.BroadcastToMapAsync(from, ConnectionProtocol.BuildActorRemoved(portal.ContextId));
            var shown = Shown(portal);
            if (shown != null) await SessionRegistry.BroadcastToMapAsync(portal.MapId, ConnectionProtocol.Push(Op.Jsn, shown));
            Console.WriteLine($"[Portals] The portal to {portal.Dimension.Name} opens on {portal.MapId}, cell {portal.Cell}.");
        }

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

        private static byte[]? Shown(DimensionPortals.Portal portal)
            => Drawing(portal) is { } drawing ? DimensionPortalProtocol.BuildShown(portal, drawing.Area, drawing.Look) : null;

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
                DimensionPortalProtocol.BuildQuestion(portal, template.DialogMessageId, template.Replies)));
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
            if (portal == null || portal.MapId != SessionContext.State.MapId) return;
            await CrossAsync(stream, portal);
        }

        private static async Task CrossAsync(NetworkStream stream, DimensionPortals.Portal portal)
        {
            var state = SessionContext.State;
            if (state.IsInFight || portal.MapId != state.MapId) return;
            bool spent = DimensionPortals.Cross(portal, NowMs);
            DimensionPortals.Save(portal);
            Console.WriteLine($"[Portals] {state.CharacterName} crosses the portal to {portal.Dimension.Name}: {portal.UsesLeft} left.");
            await TeleportHandler.ToMapAsync(stream, portal.Dimension.EntryMap, portal.Dimension.EntryCell);
            if (spent)
            {
                Console.WriteLine($"[Portals] The portal to {portal.Dimension.Name} is spent.");
                await MoveAsync(portal);
            }
        }

        private static Task WriteAsync(NetworkStream stream, byte[] frame)
            => Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, frame);
    }
}
