using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// The breeding window's messages, read off the client in Ghidra: its requests are built by eqa
    /// out of the window's events, and its answers handled by eqc, which raises the window's own events.
    /// </summary>
    /// <remarks>
    /// A paddock's content (hta) is { f1 repeated { f1 gauge, f2 value }, f2 repeated the gauges on,
    /// f3 map ride id → ride (hqu) }. The gauges travel as the client's hqf, 0 to 5, which drp::cgj
    /// turns into its PaddockGauge, 1 (slapper) to 6 (feeder). A line is the client's hqh: 0 the
    /// paddock, 1 the stable (eqa: "line != Paddock").
    ///
    /// The answers the client pairs with what it asked (eqc reads its pending requests by the
    /// request's id) go back with that id (<see cref="ConnectionProtocol.Answer"/>): hty, hti, hur,
    /// hqp, htr and hsh. The others it takes as they come.
    /// </remarks>
    public static class BreedingProtocol
    {
        /// <summary>The client's hqh: the paddock line, and the stable's.</summary>
        public const int PaddockLine = 0;
        public const int StableLine = 1;

        /// <summary>The client's OpenPaddockState: everything, or only the gauges ("Debes estar en el mapa de un cercado").</summary>
        public const int FullAccess = 0;
        public const int GaugeOnly = 1;

        /// <summary>What an answer says when it went through; anything else is the client's "error".</summary>
        public const int Ok = 0;
        public const int Failed = 1;

        /// <summary>hqp's "Nombre no válido".</summary>
        public const int InvalidName = 4;

        /// <summary>htf's errors: "falta el objeto", "no ha habido ninguna modificación".</summary>
        public const int GaugeBoostMissingItem = 1;
        public const int GaugeBoostNoChange = 3;

        /// <summary>The client's hqf of a PaddockGauge (1 to 6).</summary>
        public static int WireGauge(int gauge) => gauge - 1;

        /// <summary>The PaddockGauge (1 to 6) of the client's hqf.</summary>
        public static int GaugeOfWire(int wire) => wire is >= 0 and <= 4 ? wire + 1 : 6;

        // ─── Answers ─────────────────────────────────────────────────────────────────────

        /// <summary>hsb: opens the breeding window on a paddock.</summary>
        public static byte[] BuildOpenWindow(int paddock) => Pb.New().VarIfNotZero(1, paddock).Build();

        private static Pb RideMap(Pb target, int field, IEnumerable<Stable.StoredRide> rides)
        {
            foreach (var ride in rides)
                target.Msg(field, Pb.New().Str(1, ride.Uid.ToString()).Msg(2, Rides.Hqu(ride.Gid, ride.Effects)));
            return target;
        }

        private static Pb GaugeValues(Pb target, int field, Stable.Gauges gauges)
        {
            for (int gauge = 1; gauge <= Breeding.Gauges.Count; gauge++)
                target.Msg(field, Pb.New().VarIfNotZero(1, WireGauge(gauge)).VarIfNotZero(2, gauges.ValueOf(gauge)));
            return target;
        }

        /// <summary>A paddock's content, hta: every gauge with its value, the gauges on, and its rides.</summary>
        public static Pb PaddockContent(Stable.Gauges gauges, IEnumerable<Stable.StoredRide> rides)
        {
            var content = GaugeValues(Pb.New(), 1, gauges);
            foreach (int gauge in gauges.Active.Distinct()) content.Var(2, WireGauge(gauge));
            return RideMap(content, 3, rides);
        }

        /// <summary>hql: the breeding window's content, the paddock's and the stable's.</summary>
        public static byte[] BuildWindowContent(Pb paddockContent, IEnumerable<Stable.StoredRide> stable)
            => RideMap(Pb.New().Msg(2, paddockContent), 3, stable).Build();

        /// <summary>hrc: one paddock's content, with what may be done in it.</summary>
        public static byte[] BuildPaddock(int paddock, int state, Pb paddockContent)
            => Pb.New().Msg(2, Pb.New().VarIfNotZero(1, state).Msg(2, paddockContent)).VarIfNotZero(3, paddock).Build();

        /// <summary>hua: a paddock's content again, as it changes while it is shown.</summary>
        public static byte[] BuildPaddockUpdate(Pb paddockContent) => Pb.New().Msg(1, paddockContent).Build();

        /// <summary>huz: the stable again, whole.</summary>
        public static byte[] BuildStable(IEnumerable<Stable.StoredRide> stable)
            => Pb.New().Msg(2, RideMap(Pb.New(), 1, stable)).Build();

        /// <summary>hus: every paddock and whether the player may open it (the tab is enabled).</summary>
        public static byte[] BuildAccessiblePaddocks(IEnumerable<(int Paddock, bool Open)> paddocks)
        {
            var message = Pb.New();
            foreach (var (paddock, open) in paddocks)
                message.Msg(1, Pb.New().Var(1, paddock).VarIfNotZero(2, open ? 1 : 0));
            return message.Build();
        }

        /// <summary>hty: rides come into a line from the bag: item uid → { the ride, its id }, and the failed ones.</summary>
        public static byte[] BuildRidesAdded(IEnumerable<(long ItemUid, Stable.StoredRide Ride)> added, IEnumerable<long> failed)
        {
            var message = Pb.New();
            foreach (var (uid, ride) in added)
                message.Msg(1, Pb.New().Var(1, uid).Msg(2, Pb.New().Msg(1, Rides.Hqu(ride.Gid, ride.Effects)).Str(2, ride.Uid.ToString())));
            foreach (long uid in failed)
                message.Msg(2, Pb.New().Var(1, uid).Var(2, Failed));
            return message.Build();
        }

        /// <summary>hti and hur: each ride asked for, by id, and whether it went (0) or not.</summary>
        public static byte[] BuildRideResults(int field, IEnumerable<(string Uid, bool Done)> results)
        {
            var message = Pb.New();
            foreach (var (uid, done) in results)
                message.Msg(field, Pb.New().Str(1, uid).VarIfNotZero(2, done ? Ok : Failed));
            return message.Build();
        }

        /// <summary>hqp: a rename's result.</summary>
        public static byte[] BuildRenamed(int result) => Pb.New().VarIfNotZero(1, result).Build();

        /// <summary>htr: a gauge switched on: the gauges on now, or an error.</summary>
        public static byte[] BuildGaugeEnabled(IReadOnlyList<int> active, int error)
        {
            if (error != Ok) return Pb.New().Var(3, error).Build();
            var on = Pb.New();
            foreach (int gauge in active) on.Var(1, WireGauge(gauge));
            return Pb.New().Msg(2, on).Build();
        }

        /// <summary>hsh: a gauge switched off.</summary>
        public static byte[] BuildGaugeDisabled(int result) => Pb.New().VarIfNotZero(1, result).Build();

        /// <summary>htf: the gauges after fuel went in, or why none did.</summary>
        public static byte[] BuildGaugesFilled(Stable.Gauges gauges, int error)
        {
            if (error != Ok) return Pb.New().Var(1, error).Build();
            return Pb.New().Msg(2, GaugeValues(Pb.New(), 1, gauges)).Build();
        }

        /// <summary>hum: what the gauges would be with that fuel: { f3 { f1 item uid → used, f2 the gauges } }.</summary>
        public static byte[] BuildGaugesSimulated(Stable.Gauges gauges, IReadOnlyDictionary<long, int> used)
        {
            var result = Pb.New();
            foreach (var (uid, quantity) in used) result.Msg(1, Pb.New().Var(1, uid).VarIfNotZero(2, quantity));
            GaugeValues(result, 2, gauges);
            return Pb.New().Msg(3, result).Build();
        }

        private static Pb Float(Pb target, int field, double value) => target.Fixed32(field, BitConverter.GetBytes((float)value));

        /// <summary>hrh: rides extracted: { f1 { f1 map item → how many it gave, f2 the rides gone } }.</summary>
        public static byte[] BuildExtracted(IReadOnlyDictionary<int, int> items, IEnumerable<string> rides)
        {
            var result = Pb.New();
            foreach (var (item, quantity) in items) result.Msg(1, Pb.New().Var(1, item).VarIfNotZero(2, quantity));
            foreach (string ride in rides) result.Str(2, ride);
            return Pb.New().Msg(1, result).Build();
        }

        /// <summary>hrh: no ride could be extracted ("Extracción imposible").</summary>
        public static byte[] BuildExtractionFailed() => Pb.New().Var(2, Failed).Build();

        /// <summary>hsm: the clone, kept under the id of the original: { f1 { f1 the ride, f2 its id } }.</summary>
        public static byte[] BuildCloned(Stable.StoredRide clone)
            => Pb.New().Msg(1, Pb.New().Msg(1, Rides.Hqu(clone.Gid, clone.Effects)).Str(2, clone.Uid.ToString())).Build();

        /// <summary>hsm: why the two could not be cloned, the client's hsj (written even at 0).</summary>
        public static byte[] BuildCloneFailed(Mating.CloneRefusal refusal) => Pb.New().Var(3, (int)refusal).Build();

        /// <summary>
        /// hrz: what two rides would give: { f1 { f1 the breeder's experience { f1 to gain, f3 the
        /// new level's floor, f4 the new level, f5 its ceiling }, f3 the mákina's capacities
        /// { f2 chance, f3 capacity }, f4 how many children, f5 the colours { f1 the target, f2 the
        /// ride, f3 chance }, f7 whether the mákina fits, f8 the target's genetichas } }.
        /// </summary>
        public static byte[] BuildMatingPreview(Mating.Preview preview, long jobExperience)
        {
            long after = jobExperience + preview.Experience;
            int level = JobExperience.LevelOf(after);
            var result = Pb.New().Msg(1, Pb.New()
                .VarIfNotZero(1, preview.Experience)
                .VarIfNotZero(3, JobExperience.Floor(level))
                .Var(4, level)
                .VarIfNotZero(5, JobExperience.Next(level)));
            foreach (var (capacity, chance) in preview.Capacities.OrderBy(c => c.Key))
                result.Msg(3, Float(Pb.New(), 2, chance).VarIfNotZero(3, capacity - Rides.Chameleon));
            result.Var(4, preview.Children);
            if (preview.TargetRide != 0)
                result.Msg(5, Float(Pb.New().Var(1, 1).Var(2, preview.TargetRide), 3, preview.TargetPercent));
            foreach (var (ride, chance) in preview.Others.OrderBy(o => o.Key))
                result.Msg(5, Float(Pb.New().Var(2, ride), 3, chance));
            if (preview.MakinaFits) result.Var(7, 1);
            result.VarIfNotZero(8, preview.Tokens);
            return Pb.New().Msg(1, result).Build();
        }

        /// <summary>hrz: no preview for these two (its f3, the client's hrn).</summary>
        public static byte[] BuildMatingPreviewFailed() => Pb.New().Var(3, Failed).Build();

        /// <summary>hsu: the children: { f2 { f1 genetichas, f2 the children, f3 experience, f4 the target reached, f5 the parents } }.</summary>
        public static byte[] BuildBred(int tokens, IEnumerable<Stable.StoredRide> children, long experience, bool target,
                                       IEnumerable<Stable.StoredRide> parents)
        {
            var result = Pb.New().VarIfNotZero(1, tokens);
            RideMap(result, 2, children);
            result.VarIfNotZero(3, experience);
            if (target) result.Var(4, 1);
            RideMap(result, 5, parents);
            return Pb.New().Msg(2, result).Build();
        }

        /// <summary>hsu: why the two could not breed, the client's hsp (written even at 0).</summary>
        public static byte[] BuildBreedFailed(Mating.Refusal refusal) => Pb.New().Var(1, (int)refusal).Build();

        // ─── Requests ────────────────────────────────────────────────────────────────────

        private static IEnumerable<ProtoField> Fields(byte[] payload)
            => payload == null ? Enumerable.Empty<ProtoField>() : ProtoMessage.Parse(payload).Fields;

        private static IEnumerable<long> Varints(ProtoField field)
        {
            if (field.WireType == 0) { yield return field.VarIntValue; yield break; }
            if (field.WireType != 2) yield break;
            var bytes = field.BytesValue;
            int at = 0;
            while (at < bytes.Length)
            {
                long value = 0;
                int shift = 0;
                while (at < bytes.Length)
                {
                    byte b = bytes[at++];
                    value |= (long)(b & 0x7F) << shift;
                    shift += 7;
                    if (b < 0x80) break;
                }
                yield return value;
            }
        }

        private static long Varint(byte[] payload, int number)
            => Fields(payload).Where(f => f.FieldNumber == number && f.WireType == 0).Select(f => f.VarIntValue).FirstOrDefault();

        private static IEnumerable<string> Strings(byte[] payload, int number)
            => Fields(payload).Where(f => f.FieldNumber == number && f.WireType == 2).Select(f => Encoding.UTF8.GetString(f.BytesValue));

        /// <summary>A map of numbers to numbers (hub, htl): each entry { f1 key, f2 value }.</summary>
        private static Dictionary<long, long> NumberMap(byte[] payload, int number)
        {
            var map = new Dictionary<long, long>();
            foreach (var entry in Fields(payload).Where(f => f.FieldNumber == number && f.WireType == 2))
                map[Varint(entry.BytesValue, 1)] = Varint(entry.BytesValue, 2);
            return map;
        }

        /// <summary>The paddock a hrl picks: its f2, 1 to 6.</summary>
        public static int ReadPickedPaddock(byte[] payload) => (int)Varint(payload, 2);

        /// <summary>hse: items of the bag to put in a line: { f1 their uids, f3 the line }.</summary>
        public static (List<long> Items, int Line) ReadTransferFromBag(byte[] payload)
            => (Fields(payload).Where(f => f.FieldNumber == 1).SelectMany(Varints).ToList(), (int)Varint(payload, 3));

        /// <summary>hsn: rides of a line to send back to the bag: { f1 the line, f2 their ids }.</summary>
        public static (int Line, List<string> Rides) ReadSendToBag(byte[] payload)
            => ((int)Varint(payload, 1), Strings(payload, 2).ToList());

        /// <summary>huh: rides to move into a line: { f1 their ids, f2 the line they go to }.</summary>
        public static (List<string> Rides, int Line) ReadMove(byte[] payload)
            => (Strings(payload, 1).ToList(), (int)Varint(payload, 2));

        /// <summary>hut: a ride renamed: { f1 its id, f2 its line, f3 the name }.</summary>
        public static (string Ride, int Line, string Name) ReadRename(byte[] payload)
            => (Strings(payload, 1).FirstOrDefault() ?? "", (int)Varint(payload, 2), Strings(payload, 3).FirstOrDefault() ?? "");

        /// <summary>hun and hqw: a gauge switched on or off, as the client's hqf.</summary>
        public static int ReadGauge(byte[] payload) => GaugeOfWire((int)Varint(payload, 1));

        /// <summary>hsv: the rides to extract.</summary>
        public static List<string> ReadExtract(byte[] payload) => Strings(payload, 1).ToList();

        /// <summary>hqv: the two rides to clone, the original first.</summary>
        public static (string First, string Second) ReadClone(byte[] payload)
            => (Strings(payload, 1).FirstOrDefault() ?? "", Strings(payload, 2).FirstOrDefault() ?? "");

        private static long OptionalVarint(byte[] payload, int number)
            => Fields(payload).Any(f => f.FieldNumber == number && f.WireType == 0) ? Varint(payload, number) : -1;

        /// <summary>hsa, the mating preview: { f1 a ride, f4 the other, f3 the mákina's uid when there is one }.</summary>
        public static (string First, string Second, long Makina) ReadMatingPreview(byte[] payload)
            => (Strings(payload, 1).FirstOrDefault() ?? "", Strings(payload, 4).FirstOrDefault() ?? "", OptionalVarint(payload, 3));

        /// <summary>htu, the mating: { f3 a ride, f1 the other, f2 the mákina's uid when there is one }.</summary>
        public static (string First, string Second, long Makina) ReadMating(byte[] payload)
            => (Strings(payload, 3).FirstOrDefault() ?? "", Strings(payload, 1).FirstOrDefault() ?? "", OptionalVarint(payload, 2));

        /// <summary>hub and htl: fuel to pour in, item uid → how many.</summary>
        public static Dictionary<long, long> ReadFuel(byte[] payload) => NumberMap(payload, 1);
    }
}
