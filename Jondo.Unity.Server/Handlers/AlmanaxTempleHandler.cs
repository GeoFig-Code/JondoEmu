using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The Almanax temple's ways (<see cref="AlmanaxTemple"/>): the same passage as any other door --
    /// iwn, the use closed, and the jru -- to where today says.
    /// </summary>
    public static class AlmanaxTempleHandler
    {
        /// <summary>A door or way out of the temple, clicked.</summary>
        public static async Task UseAsync(NetworkStream stream, Interactives.Element element, int type, int skillId)
        {
            long from = SessionContext.State.MapId;
            if (AlmanaxTemple.Destination(from, element.Cell, Almanax.Clock()) is not { } to) return;

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Iwn, ConnectionProtocol.BuildElementInUse(
                    element.Id, skillId, SessionContext.State.CharacterId)));
            await TeleportHandler.ReleaseAsync(stream, element.Id, skillId, element.Cell, type);
            await GoAsync(stream, from, to);
        }

        /// <summary>A way out of the temple walked onto. False when the cell is none.</summary>
        public static async Task<bool> WalkAsync(NetworkStream stream, long mapId, int cellId)
        {
            if (AlmanaxTemple.Destination(mapId, cellId, Almanax.Clock()) is not { } to) return false;
            await GoAsync(stream, mapId, to);
            return true;
        }

        private static async Task GoAsync(NetworkStream stream, long from, (long Map, int Cell) to)
        {
            int landed = await TeleportHandler.ToMapAsync(stream, to.Map, to.Cell);
            if (landed >= 0)
                Console.WriteLine($"[Almanax] {SessionContext.State.CharacterName}: temple {from} -> {to.Map}, cell {landed}.");
        }
    }
}
