using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Handlers
{
    public static partial class FightHandler
    {
        /// <summary>
        /// A fighter marks a cell for his side (kmh { 2 cell }), and his side sees it marked by him:
        /// kmj { 1 fighter, 2 cell }.
        /// </summary>
        /// <remarks>
        /// MEASURED in «Combate/combate contra poutch nivel 75…-marcadores permanentes»: kmh 10 a0 02,
        /// cell 288, answered kmj { 302677754146, 288 }. The client's fight frame (fml) sends it from
        /// five places, each with the cell. Only his own side gets it, as the side's own messages go
        /// (ASuBandoAsync); out of a fight there is nothing to mark.
        /// </remarks>
        public static async Task MarkCellAsync(NetworkStream stream, byte[] payload)
        {
            byte[]? kmh = ConnectionProtocol.ReadPayload(payload, "kmh");
            long me = SessionContext.State.CharacterId;
            var fight = FightOf(me);
            var fighter = fight?.Buscar(me);
            if (kmh == null || fight == null || fighter == null) return;

            long cell = ProtoMessage.Parse(kmh).Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 0)?.VarIntValue ?? 0;
            await ASuBandoAsync(fight, fighter.TeamId,
                ConnectionProtocol.Push("kmj", Pb.New().Var(1, me).VarIfNotZero(2, cell).Build()));
        }
    }
}
