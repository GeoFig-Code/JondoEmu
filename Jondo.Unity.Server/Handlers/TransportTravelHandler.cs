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
    /// A kart of route 666 (<see cref="Transports"/>): clicked, it lists its network's other stops
    /// in the zaapi's window, each at the network's fare; the choice (hjc) is the zaapi's trip,
    /// done by <see cref="ZaapTravelHandler.TravelAsync"/>, which charges the kart's fare.
    /// </summary>
    public static class TransportTravelHandler
    {
        public static async Task OpenAsync(NetworkStream stream, Interactives.Element vehicle, int skillId)
        {
            long here = SessionContext.State.MapId;
            if (Transports.On(here) is not { } found)
            {
                Console.WriteLine($"[Transports] Map {here} shows no vehicle of a known network.");
                return;
            }

            SessionContext.State.OpenZaapMapId = here;
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Iwn, ConnectionProtocol.BuildElementInUse(
                    vehicle.Id, skillId, SessionContext.State.CharacterId)));

            var destinations = new List<ConnectionProtocol.ZaapDestination>();
            foreach (var stop in found.Network.Stops)
            {
                if (MapManager.GetMapInfo(stop.MapId) == null) continue;
                // The stop one stands at with no fare: "you are here", as the zaapi's own.
                destinations.Add(new ConnectionProtocol.ZaapDestination(
                    stop.MapId, stop.SubAreaId, Interactives.LevelOfSubArea(stop.SubAreaId),
                    stop.MapId == found.Stop.MapId ? 0 : found.Network.Cost, Zaapis.Kind));
            }
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Hjj, ConnectionProtocol.BuildZaapList(0, destinations, Zaapis.Teleporter)));
            Console.WriteLine($"[Transports] {found.Network.Name}: {destinations.Count} stops from map {here}.");
        }
    }
}
