using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Protocol;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// Entering the haven bag, changing the decor and placing the furniture.
    ///
    /// From the captures:
    ///
    ///   client   jbn { f2: whose }        the button and the H key
    ///   client   jbl { f1: theme }        changing the decor
    ///   server   jru { f2: the map } + the whole map + jbu (furniture) + jaz (permissions)
    ///
    ///   client   jbv          server jbm        opening placement mode
    ///   client   jbg { f2 (rep): {f1: cell, f2: piece, f3: rotation} }   accept
    ///   client   jbk / jav / jaw   server jba                            closing the mode
    ///
    /// The jbg arrives in pieces -- in the capture there are three in a row -- and carries the WHOLE
    /// room, not the differences. That is why the pieces are put together and written at once on
    /// closing: saving each piece separately, the first would delete what the other two carry.
    /// </summary>
    public static class MerkasakoHandler
    {
        /// <summary>
        /// The button and the H key, which is a different message from changing the decor:
        ///
        ///   client   jbn { f2: whose haven bag it is }
        ///
        /// It carries a character because somebody else's can be visited. Here there is only one, so one
        /// goes into one's own, with the decor left on the last time.
        ///
        /// And IT IS A TOGGLE: the same message goes in and out, and the server decides by where the
        /// player is. Measured in «Movimiento/ir al merkasako y volver.pcapng»: the player's requests #1
        /// and #8 are the same jbn with the body 10a28280c8e708 byte for byte; to the first it answers
        /// with map 162795538 -- subarea 851, the bags' one -- and to the second with 217056262, a normal
        /// world map. There is no third opcode: a sweep of the captures finds jbn in five files and jbl in
        /// one, and the empty jbl is decor 0, not the exit.
        ///
        /// Before, this called GoToThemeAsync without looking at anything, so the second H put the player
        /// back in the same room he wanted to leave.
        /// </summary>
        public static async Task EnterFromOutsideAsync(NetworkStream stream, byte[] payload)
        {
            if (ConnectionProtocol.ReadPayload(payload, Op.Jbn) == null) return;
            if (await Managers.Jail.KeepsInAsync(stream)) return;

            var state = Jondo.Unity.Server.Network.SessionContext.State;

            if (Merkasako.IsHavenBag(state.MapId))
            {
                await LeaveAsync(stream);
                return;
            }
            if (await ConquestHandler.HoldsInAsync(stream, ConquestHandler.NoHavenBagInAWar)) return;

            state.HavenBagEntryMapId = state.MapId;
            state.HavenBagEntryCell = state.CellId;
            HavenBagStore.SetEntry(state.CharacterId, state.MapId, state.CellId);

            await GoToRoomAsync(stream, HavenBagStore.CurrentRoom(state.CharacterId), entering: true);
        }

        /// <summary>
        /// «Salir del refugio», the map panel's option inside the haven bag (jaq, empty:
        /// MapInfoUI.ExitHavenBag): the same way out as the button's second press.
        /// </summary>
        public static async Task ExitAsync(NetworkStream stream)
        {
            if (Merkasako.IsHavenBag(SessionContext.State.MapId)) await LeaveAsync(stream);
        }

        /// <summary>
        /// Back to the world, the way he came in.
        /// </summary>
        /// <remarks>
        /// The same frames as leaving a house and in the same order, which is what the capture has: after
        /// the exit jru neither jbf, nor jbu, nor jaz appear -- the three that do go on ENTERING --, only
        /// lqu, lva, lva, iom and the jss.
        ///
        /// Where he came from is kept in the database too (<see cref="HavenBagStore.EntryOf"/>): one
        /// who logged in inside was taken to the starting point, Incarnam, since the session that
        /// knew it was gone. If it is not known at all he is still sent there rather than locked in.
        ///
        /// And the roleplay context goes and comes back (kml, kmp), as on leaving a fight. The client
        /// keeps the bag's menu while its haven bag state stands, and only the roleplay entities'
        /// reset clears it (RoleplayEntitiesService.Reset); without it the menu stayed on in Incarnam.
        /// </remarks>
        private static async Task LeaveAsync(NetworkStream stream)
        {
            var state = Jondo.Unity.Server.Network.SessionContext.State;
            long dentro = state.MapId;

            long salidaMapa = state.HavenBagEntryMapId;
            int salidaCasilla = state.HavenBagEntryCell;
            if (salidaMapa == 0) (salidaMapa, salidaCasilla) = HavenBagStore.EntryOf(state.CharacterId);

            if (salidaMapa == 0 || Merkasako.IsHavenBag(salidaMapa))
            {
                salidaMapa = DatabaseManager.StartingMap;
                salidaCasilla = DatabaseManager.StartingCell;
                Console.WriteLine("[Merkasako] Where it came in from is not known: it is taken out to the starting point.");
            }

            state.MapId = salidaMapa;
            state.CellId = MapManager.GetNearestWalkableCell(salidaMapa, salidaCasilla);
            state.HavenBagEntryMapId = 0;
            state.HavenBagEntryCell = 0;
            DatabaseManager.SaveCurrentCharacter();

            await SessionRegistry.AnunciarMudanzaAsync(SessionContext.Current, dentro);

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.BuildActorLeft(state.CharacterId));
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, ConnectionProtocol.Push(Op.Kml));
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, ConnectionProtocol.Push(Op.Kmp));
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.BuildLoadMap(salidaMapa));
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.BuildMapClock());
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.BuildMapDiscovered(salidaMapa));

            Console.WriteLine($"[Merkasako] Out of {dentro} to map {salidaMapa}, " +
                              $"cell {state.CellId}.");
        }

        /// <summary>
        /// Going to another room of one's bag (jbl { f1 room }): the room's theme is loaded, with its
        /// furniture. «Cambiar de salas» sends 1, 2, 3 and an empty one for room 0.
        /// </summary>
        /// <remarks>
        /// It was read as a theme: room 1 opened theme 1 whatever the room had.
        /// </remarks>
        public static async Task ChangeRoomAsync(NetworkStream stream, byte[] payload)
        {
            byte[]? jbl = ConnectionProtocol.ReadPayload(payload, Op.Jbl);
            if (jbl == null || !Merkasako.IsHavenBag(SessionContext.State.MapId)) return;

            int room = 0;
            foreach (var field in ProtoMessage.Parse(jbl).Fields)
                if (field.FieldNumber == 1 && field.WireType == 0) room = (int)field.VarIntValue;
            if (room < 0 || room >= Merkasako.Rooms) return;

            HavenBagStore.SetCurrentRoom(SessionContext.State.CharacterId, room);
            await GoToRoomAsync(stream, room, entering: false);
        }

        /// <summary>
        /// The room's theme changed (jbt { f1 theme }), once its popup is accepted: the room takes
        /// it, is loaded again in it, and the rooms' list (jbf) follows. Every theme is everybody's
        /// here (<see cref="Merkasako.Themes"/>).
        /// </summary>
        public static async Task ChangeThemeAsync(NetworkStream stream, byte[] payload)
        {
            byte[]? jbt = ConnectionProtocol.ReadPayload(payload, Op.Jbt);
            if (jbt == null || !Merkasako.IsHavenBag(SessionContext.State.MapId)) return;

            int theme = 0;
            foreach (var field in ProtoMessage.Parse(jbt).Fields)
                if (field.FieldNumber == 1 && field.WireType == 0) theme = (int)field.VarIntValue;
            if (!Merkasako.IsTheme(theme)) return;

            long who = SessionContext.State.CharacterId;
            int room = HavenBagStore.CurrentRoom(who);
            HavenBagStore.SetRoomTheme(who, room, theme);
            await GoToRoomAsync(stream, room, entering: false);
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Jbf, ConnectionProtocol.BuildHavenBagRooms(HavenBagStore.RoomsOf(who))));
        }

        /// <summary>
        /// Into a room of one's bag: its theme's map, by the zaap. Entering, the rooms' list (jbf)
        /// goes behind the map's own frames, as in «ir al merkasako y volver» (frame 11).
        /// </summary>
        private static async Task GoToRoomAsync(NetworkStream stream, int room, bool entering)
        {
            long who = SessionContext.State.CharacterId;
            int theme = HavenBagStore.ThemeOfRoom(who, room);
            long target = Merkasako.MapOfTheme(theme);
            if (target == 0)
            {
                Console.WriteLine("[Merkasako] There is no theme at all in the client's data.");
                return;
            }

            if (MapManager.GetMapInfo(target) == null)
            {
                Console.WriteLine($"[Merkasako] Map {target} is not in the world data.");
                return;
            }

            HavenBagStore.SaveTheme(who, Merkasako.ThemeOfMap(target));

            long left = SessionContext.State.MapId;
            SessionContext.State.MapId = target;

            // Next to the zaap, which is where the game leaves one on entering.
            var zaap = Merkasako.ZaapOf(target);
            SessionContext.State.CellId = MapManager.GetNearestWalkableCell(target, zaap.Cell);
            DatabaseManager.SaveCurrentCharacter();

            // Whoever stays on the map he left stops seeing him there. This was missing: going
            // into the haven bag left him standing on the street, drawn, for everybody on it.
            // Only the leaving is told -- a haven bag is nobody else's to be told about.
            if (left != target)
                await SessionRegistry.RemoveFromMapAsync(left, SessionContext.Current.CharacterId, SessionContext.Current.Id);

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.BuildActorLeft(SessionContext.State.CharacterId));
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.BuildLoadMap(target));
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.BuildMapClock());
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.BuildMapDiscovered(target));
            if (entering)
            {
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                    ConnectionProtocol.Push(Op.Jbf, ConnectionProtocol.BuildHavenBagRooms(HavenBagStore.RoomsOf(who))));
            }

            Console.WriteLine($"[Merkasako] Room {room}, theme {Merkasako.ThemeOfMap(target)} -> map {target}, " +
                              $"cell {SessionContext.State.CellId} (zaap on {zaap.Cell}).");
        }

        /// <summary>
        /// Whose bag the map is, the jss's f8 (jqd), for the one standing in it: here always his
        /// own, since nobody visits another's. The HavenbagManagerUI shows its menu -- rooms,
        /// themes, furniture -- only in one's own bag (m_inMyOwnHavenbag), which it learns here.
        /// </summary>
        public static void AddToMap(Pb jss, long mapId, DatabaseManager.DbCharacter owner)
        {
            if (owner == null || !Merkasako.IsHavenBag(mapId)) return;
            jss.Msg(8, ConnectionProtocol.HavenBagInfo(HavenBagStore.CurrentRoom(owner.Id), Merkasako.ThemeOfMap(mapId),
                                                       Merkasako.Rooms, owner.Id, owner.Name, owner.Level));
        }

        // ─── Furniture placement mode ───────────────────────────────────────────

        /// <summary>The client opens the management menu. An empty jbm is answered and it lets him place.</summary>
        public static async Task OpenEditorAsync(NetworkStream stream)
        {
            SessionContext.State.IsHavenBagEditing = true;
            SessionContext.State.PendingHavenBagFurniture.Clear();

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Jbm));

            Console.WriteLine("[Merkasako] Furniture placing mode open.");
        }

        /// <summary>
        /// Accepting the room starts with a jbk: a new upload of the whole room begins, and what
        /// was gathered before is forgotten. Nothing is answered to it.
        /// </summary>
        public static void UploadStarts()
        {
            SessionContext.State.IsHavenBagEditing = true;
            SessionContext.State.PendingHavenBagFurniture.Clear();
        }

        /// <summary>
        /// A piece of the room (jbg): added to the upload, and the upload written at once, in place
        /// of the room. On accepting, the client sends jbk, the pieces, jav and jaw -- the capture's
        /// order -- but the pieces may also come behind the jav (01:16:03 of the 16/10 test). It
        /// was written on the first close, before any piece had come: the room was stored empty and
        /// the pieces that followed were dropped.
        /// </summary>
        public static void CollectFurniture(byte[] payload)
        {
            byte[]? jbg = ConnectionProtocol.ReadPayload(payload, Op.Jbg);
            if (jbg == null || !Merkasako.IsHavenBag(SessionContext.State.MapId)) return;

            foreach (var field in ProtoMessage.Parse(jbg).Fields)
            {
                if (field.FieldNumber != 2 || field.WireType != 2) continue;

                int cell = 0, orientation = 0;
                long typeId = 0;
                foreach (var inner in ProtoMessage.Parse(field.BytesValue).Fields)
                {
                    if (inner.WireType != 0) continue;
                    if (inner.FieldNumber == 1) cell = (int)inner.VarIntValue;
                    else if (inner.FieldNumber == 2) typeId = (long)inner.VarIntValue;
                    else if (inner.FieldNumber == 3) orientation = (int)inner.VarIntValue;
                }

                // A piece of furniture that is not in the client's catalogue is not stored: the client
                // would not know how to draw it and the room would be left with an invisible blocking gap.
                if (typeId == 0 || !Merkasako.IsFurniture(typeId)) continue;

                SessionContext.State.PendingHavenBagFurniture.Add(new HavenBagStore.Furniture
                {
                    Cell = cell,
                    TypeId = typeId,
                    Orientation = orientation,
                });
            }

            long who = SessionContext.State.CharacterId;
            int theme = Merkasako.ThemeOfMap(SessionContext.State.MapId);
            HavenBagStore.SaveFurniture(who, theme, SessionContext.State.PendingHavenBagFurniture);
            Console.WriteLine($"[Merkasako] Theme {theme}: {SessionContext.State.PendingHavenBagFurniture.Count} piece(s) of furniture saved.");
        }

        /// <summary>
        /// The client closes the menu (jav, jaw): the room is sent back to him as it is stored.
        /// It was written here, but its pieces are written as they come (<see cref="CollectFurniture"/>).
        /// </summary>
        public static async Task CloseEditorAsync(NetworkStream stream)
        {
            SessionContext.State.IsHavenBagEditing = false;

            await SendFurnitureAsync(stream);

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Jba));
        }

        /// <summary>
        /// The furniture and the permissions, which the client expects after the map. The permissions go
        /// empty: there is nobody here to invite.
        /// </summary>
        public static async Task SendFurnitureAsync(NetworkStream stream)
        {
            var pieces = HavenBagStore.FurnitureOf(Jondo.Unity.Server.Network.SessionContext.State.CharacterId,
                                                   Merkasako.ThemeOfMap(Jondo.Unity.Server.Network.SessionContext.State.MapId));

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Jbu, ConnectionProtocol.BuildHavenBagFurniture(pieces)));
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Jaz));
        }
    }
}
