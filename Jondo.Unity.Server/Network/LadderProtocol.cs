using System.Collections.Generic;
using System.Globalization;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// The ladder window's lines (see <see cref="Ladders"/>), as the capture of the real server sends
    /// them and the client reads them (bo.moq, mor, mos, mot).
    /// </summary>
    public static class LadderProtocol
    {
        /// <summary>
        /// A character on a line (ipx): f2 his server, f3 his class, f4 his name, f5 his sex (1 a
        /// woman), f6 his level, f7 the character.
        /// </summary>
        public static Pb Person(Ladders.Person who)
            => Pb.New()
                .VarIfNotZero(2, who.Server)
                .VarIfNotZero(3, who.Breed)
                .StrIfNotEmpty(4, who.Name)
                .VarIfNotZero(5, who.Sex)
                .VarIfNotZero(6, who.Level)
                .VarIfNotZero(7, who.Id);

        /// <summary>An experience line (iqb): f1 the experience, f3 the character, f4 the rank.</summary>
        public static Pb ExperienceLine(Ladders.ExperienceLine line)
            => Pb.New().VarIfNotZero(1, line.Experience).Msg(3, Person(line.Who)).Var(4, line.Rank);

        /// <summary>An achievement line (ird): f1 the rank, f2 the points, f3 the character.</summary>
        public static Pb AchievementLine(Ladders.AchievementLine line)
            => Pb.New().Var(1, line.Rank).VarIfNotZero(2, line.Points).Msg(3, Person(line.Who));

        /// <summary>
        /// A Koliseo line (iqx): f1 the rank, f2 the win rate in percent, f3 the character, f4 the
        /// league, f5 the rating.
        /// </summary>
        public static Pb KoliseoLine(Ladders.KoliseoLine line)
            => Pb.New().Var(1, line.Rank).VarIfNotZero(2, line.WinRate).Msg(3, Person(line.Who))
                 .Var(4, line.League).VarIfNotZero(5, line.Rating);

        /// <summary>
        /// A dream line (iqn): f1 the dreamer himself, f2 the intensity, f3 the rank, f4 the character,
        /// f5 the turn the last wave fell on, f6 when, f7 the waves.
        /// </summary>
        public static Pb DreamLine(Ladders.DreamLine line)
        {
            var iqn = Pb.New();
            if (line.Dreamer) iqn.Var(1, 1);
            return iqn.VarIfNotZero(2, line.Intensity).Var(3, line.Rank).Msg(4, Person(line.Who))
                      .VarIfNotZero(5, line.Turn).Str(6, Iso(line.DoneUtc)).VarIfNotZero(7, line.Waves);
        }

        /// <summary>The capture's form of a day: «2026-07-05T23:25:49.342624537Z».</summary>
        public static string Iso(System.DateTime utc)
            => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

        /// <summary>A list of lines in one repeated field.</summary>
        public static byte[] BuildList<T>(int field, IEnumerable<T> lines, System.Func<T, Pb> line)
        {
            var list = Pb.New();
            foreach (var l in lines) list.Msg(field, line(l));
            return list.Build();
        }

        /// <summary>The player's own line (iqh, iqw, iri: f1; iqr: f2), or nothing when he has none.</summary>
        public static byte[] BuildOwn(int field, Pb? line)
            => line == null ? System.Array.Empty<byte>() : Pb.New().Msg(field, line).Build();
    }
}
