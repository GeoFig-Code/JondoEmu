using System.Collections.Generic;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// The breeding window's messages, read off the client: its requests are built by eqa out of the
    /// window's events, and its answers handled by eqc, which raises the window's own events.
    /// </summary>
    /// <remarks>
    /// A paddock's content (hta) is { f1 repeated { f1 gauge, f2 value }, f2 repeated the gauges on,
    /// f3 map ride id → ride }. The gauge enum runs 0 to 5 for the six gauges.
    /// </remarks>
    public static class BreedingProtocol
    {
        /// <summary>How many gauges a paddock carries in its content.</summary>
        public const int GaugeCount = 6;

        /// <summary>hsb: opens the breeding window on a paddock.</summary>
        public static byte[] BuildOpenWindow(int paddock) => Pb.New().VarIfNotZero(1, paddock).Build();

        /// <summary>
        /// A paddock's content: every gauge with its value, the gauges on, and its rides (none yet).
        /// </summary>
        public static Pb PaddockContent(IReadOnlyList<int> gaugeValues, IReadOnlyList<int> activeGauges)
        {
            var content = Pb.New();
            for (int gauge = 0; gauge < GaugeCount; gauge++)
            {
                int value = gauge < gaugeValues.Count ? gaugeValues[gauge] : 0;
                content.Msg(1, Pb.New().VarIfNotZero(1, gauge).VarIfNotZero(2, value));
            }
            foreach (int gauge in activeGauges) content.Var(2, gauge);
            return content;
        }

        /// <summary>hql: the breeding window's content, the paddock's and the stable's (no ride yet).</summary>
        public static byte[] BuildWindowContent(Pb paddockContent) => Pb.New().Msg(2, paddockContent).Build();

        /// <summary>hrc: one paddock's content.</summary>
        public static byte[] BuildPaddock(int paddock, Pb paddockContent)
            => Pb.New().Msg(1, Pb.New().Msg(2, paddockContent)).VarIfNotZero(3, paddock).Build();

        /// <summary>hus: every paddock and whether the player may open it.</summary>
        public static byte[] BuildAccessiblePaddocks(IEnumerable<(int Paddock, bool Open)> paddocks)
        {
            var message = Pb.New();
            foreach (var (paddock, open) in paddocks)
                message.Msg(1, Pb.New().Var(1, paddock).VarIfNotZero(2, open ? 1 : 0));
            return message.Build();
        }

        /// <summary>The paddock a hrl picks: its f2.</summary>
        public static int ReadPickedPaddock(byte[] payload)
        {
            foreach (var field in ProtoMessage.Parse(payload).Fields)
                if (field.FieldNumber == 2 && field.WireType == 0) return (int)field.VarIntValue;
            return 0;
        }
    }
}
