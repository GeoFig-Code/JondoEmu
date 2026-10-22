using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The ladder window (LadderUI): each tab asks for its list and for the player's own line, or,
    /// with a name typed in, for the lines of that name (see <see cref="Ladders"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured in «Combate/koliseo completo con invitacion-koli 2vs2-hasta final de combate»:
    /// iqy and iqu → iqt and iqw (the Koliseo), ipz and iqa → iqc and iqh (experience), irj and irg
    /// → iri and irc (achievements), iql and iqq → iqr and iqj (dreams), hfz and hfv → hei and hfe
    /// (world events); every one an answer to its request but the hei, a push.
    /// </para>
    /// <para>
    /// The searches by name are read off the client (egd.xko, xkp, xkq, xkr): iqd { f2 the name, f3
    /// the server } → iqg { f2 }, iqz { f1 the band of levels, f2 the mode, f3 the class, f4 the
    /// name } → iqv, irb { f1 the server, f2 the name } → irh, iqk { f1 the server, f2 the name } →
    /// iqm. The Koliseo's filters likewise: iqy { f2 the mode, f3 the band, f4 the class }, iqu { f2
    /// the class, f3 the mode, f4 the band }. A server 0 is every server.
    /// </para>
    /// </remarks>
    public static class LadderHandler
    {
        private static Task AnswerAsync(NetworkStream stream, byte[] frame, string op, byte[] payload)
            => Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, ConnectionProtocol.Answer(op, payload, ConnectionProtocol.RequestId(frame)));

        /// <summary>A request's numbers and texts by field.</summary>
        internal sealed class Request
        {
            private readonly Dictionary<int, long> _numbers = new();
            private readonly Dictionary<int, string> _texts = new();

            public Request(byte[]? payload)
            {
                if (payload == null || payload.Length == 0) return;
                foreach (var field in ProtoMessage.Parse(payload).Fields)
                {
                    if (field.WireType == 0) _numbers[field.FieldNumber] = (long)field.VarIntValue;
                    else if (field.WireType == 2) _texts[field.FieldNumber] = Encoding.UTF8.GetString(field.BytesValue);
                }
            }

            public int Number(int field) => _numbers.TryGetValue(field, out long n) ? (int)n : 0;

            public string Text(int field) => _texts.TryGetValue(field, out var s) ? s : "";
        }

        private static Request Read(byte[] frame, string op) => new Request(ConnectionProtocol.ReadPayload(frame, op));

        private static long Me => SessionContext.State.CharacterId;

        // ─── Experience ────────────────────────────────────────────────────────────────────

        /// <summary>The experience tab's list (ipz { f3 the server } → iqc).</summary>
        public static Task ExperienceListAsync(NetworkStream stream, byte[] frame)
            => AnswerAsync(stream, frame, Op.Iqc, LadderProtocol.BuildList(1,
                Ladders.Top(Ladders.Experience(Read(frame, Op.Ipz).Number(3))), LadderProtocol.ExperienceLine));

        /// <summary>The player's own experience line (iqa { f1 the server } → iqh).</summary>
        public static Task ExperienceOwnAsync(NetworkStream stream, byte[] frame)
        {
            var mine = Ladders.Experience(Read(frame, Op.Iqa).Number(1)).FirstOrDefault(l => l.Who.Id == Me);
            return AnswerAsync(stream, frame, Op.Iqh, LadderProtocol.BuildOwn(1, mine == null ? null : LadderProtocol.ExperienceLine(mine)));
        }

        /// <summary>A name searched in the experience tab (iqd → iqg { f2 the lines }).</summary>
        public static Task ExperienceSearchAsync(NetworkStream stream, byte[] frame)
        {
            var request = Read(frame, Op.Iqd);
            var found = Ladders.Named(Ladders.Experience(request.Number(3)), l => l.Who, request.Text(2));
            return AnswerAsync(stream, frame, Op.Iqg, LadderProtocol.BuildList(2, found, LadderProtocol.ExperienceLine));
        }

        // ─── Achievements ──────────────────────────────────────────────────────────────────

        /// <summary>The achievements tab's list (irg { f2 the server } → irc).</summary>
        public static Task AchievementListAsync(NetworkStream stream, byte[] frame)
            => AnswerAsync(stream, frame, Op.Irc, LadderProtocol.BuildList(1,
                Ladders.Top(Ladders.Achievement(Read(frame, Op.Irg).Number(2))), LadderProtocol.AchievementLine));

        /// <summary>The player's own achievements line (irj { f1 the server } → iri).</summary>
        public static Task AchievementOwnAsync(NetworkStream stream, byte[] frame)
        {
            var mine = Ladders.Achievement(Read(frame, Op.Irj).Number(1)).FirstOrDefault(l => l.Who.Id == Me);
            return AnswerAsync(stream, frame, Op.Iri, LadderProtocol.BuildOwn(1, mine == null ? null : LadderProtocol.AchievementLine(mine)));
        }

        /// <summary>A name searched in the achievements tab (irb → irh).</summary>
        public static Task AchievementSearchAsync(NetworkStream stream, byte[] frame)
        {
            var request = Read(frame, Op.Irb);
            var found = Ladders.Named(Ladders.Achievement(request.Number(1)), l => l.Who, request.Text(2));
            return AnswerAsync(stream, frame, Op.Irh, LadderProtocol.BuildList(1, found, LadderProtocol.AchievementLine));
        }

        // ─── The Koliseo ───────────────────────────────────────────────────────────────────

        /// <summary>The Koliseo tab's list (iqy { f2 the mode, f3 the band, f4 the class } → iqt).</summary>
        public static Task KoliseoListAsync(NetworkStream stream, byte[] frame)
        {
            var request = Read(frame, Op.Iqy);
            var lines = Ladders.Koliseo(request.Number(2), breed: request.Number(4), firstLevel: request.Number(3));
            return AnswerAsync(stream, frame, Op.Iqt, LadderProtocol.BuildList(1, Ladders.Top(lines), LadderProtocol.KoliseoLine));
        }

        /// <summary>The player's own Koliseo line (iqu { f2 the class, f3 the mode, f4 the band } → iqw).</summary>
        public static Task KoliseoOwnAsync(NetworkStream stream, byte[] frame)
        {
            var request = Read(frame, Op.Iqu);
            var mine = Ladders.Koliseo(request.Number(3), breed: request.Number(2), firstLevel: request.Number(4))
                              .FirstOrDefault(l => l.Who.Id == Me);
            return AnswerAsync(stream, frame, Op.Iqw, LadderProtocol.BuildOwn(1, mine == null ? null : LadderProtocol.KoliseoLine(mine)));
        }

        /// <summary>A name searched in the Koliseo tab (iqz → iqv).</summary>
        public static Task KoliseoSearchAsync(NetworkStream stream, byte[] frame)
        {
            var request = Read(frame, Op.Iqz);
            var lines = Ladders.Koliseo(request.Number(2), breed: request.Number(3), firstLevel: request.Number(1));
            return AnswerAsync(stream, frame, Op.Iqv,
                LadderProtocol.BuildList(1, Ladders.Named(lines, l => l.Who, request.Text(4)), LadderProtocol.KoliseoLine));
        }

        // ─── Infinite dreams ───────────────────────────────────────────────────────────────

        /// <summary>The dreams tab's list (iqq { f3 the server } → iqj).</summary>
        public static Task DreamListAsync(NetworkStream stream, byte[] frame)
            => AnswerAsync(stream, frame, Op.Iqj, LadderProtocol.BuildList(1,
                Ladders.Top(Ladders.Dreams(Read(frame, Op.Iqq).Number(3))), LadderProtocol.DreamLine));

        /// <summary>The player's own dream line (iql { f1 the server } → iqr { f2 }).</summary>
        public static Task DreamOwnAsync(NetworkStream stream, byte[] frame)
        {
            var mine = Ladders.Dreams(Read(frame, Op.Iql).Number(1)).FirstOrDefault(l => l.Who.Id == Me);
            return AnswerAsync(stream, frame, Op.Iqr, LadderProtocol.BuildOwn(2, mine == null ? null : LadderProtocol.DreamLine(mine)));
        }

        /// <summary>A name searched in the dreams tab (iqk → iqm).</summary>
        public static Task DreamSearchAsync(NetworkStream stream, byte[] frame)
        {
            var request = Read(frame, Op.Iqk);
            var found = Ladders.Named(Ladders.Dreams(request.Number(1)), l => l.Who, request.Text(2));
            return AnswerAsync(stream, frame, Op.Iqm, LadderProtocol.BuildList(1, found, LadderProtocol.DreamLine));
        }

        // ─── World events ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// The world events tab: the events going on (hfz → hei, a push) and the instances that have a
        /// ladder (hfv → hfe { f1 and f2 hgc { f1 the start, f2 the instance, f3 the event } }). No
        /// world event runs here, so both are empty -- what the real server sent too, for the first.
        /// </summary>
        public static Task WorldEventsAsync(NetworkStream stream, byte[] frame)
            => Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, ConnectionProtocol.Push(Op.Hei, Array.Empty<byte>()));

        /// <inheritdoc cref="WorldEventsAsync"/>
        public static Task WorldEventInstancesAsync(NetworkStream stream, byte[] frame)
            => AnswerAsync(stream, frame, Op.Hfe, Array.Empty<byte>());
    }
}
