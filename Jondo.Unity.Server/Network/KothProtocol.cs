using System;
using System.Globalization;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// The conquest window's frames (AllianceAvaUi): the King of the Hill's score every minute
    /// (mep) and its end (mdz). No conquest is captured: all is read off the client's code.
    /// </summary>
    /// <remarks>
    /// fgv.bewm reads the mep: it needs the player's alliance, and it sorts each line by the
    /// alliance's uuid -- the prism's owner is «en defensa», one's own «mi alianza», the rest «en
    /// ataque» --. kp.nvv, the line, takes:
    /// <code>
    ///   men { f1 the alliance, f2 its roles [mei { f1 how many, f2 the role }],
    ///         f3 its minutes of domination, f5 its points [mel { f1 points, f4 the kind }],
    ///         f6 its members in the zone }
    /// </code>
    /// The total is the sum of the f5's points, each kind its column (AllianceAvaUi.ScoreType: maps,
    /// fights, prism). The men's f4 and the mel's f2 and f3 are read by nobody the window draws.
    /// The mep's f3, when the conquest began, is a date the client parses itself (gji.bkla,
    /// DateTime.Parse in the invariant culture, round trip) for the clock of how long it lasts.
    /// </remarks>
    public static class KothProtocol
    {
        /// <summary>The score (mep): { f1 each alliance, f2 when the next minute is counted, f3 when it began }.</summary>
        public static byte[] BuildUpdate(Conquests.War war, Func<long, AllianceStore.Alliance> allianceOf, int defaultRole)
        {
            var mep = Pb.New();
            foreach (var side in war.Sides)
            {
                var alliance = allianceOf(side.AllianceId);
                if (alliance != null) mep.Msg(1, Line(side, alliance, defaultRole));
            }
            return mep.Var(2, war.NextTickUtcMs)
                      .Str(3, DateTimeOffset.FromUnixTimeMilliseconds(war.StartedUtcMs).UtcDateTime
                                            .ToString("o", CultureInfo.InvariantCulture))
                      .Build();
        }

        /// <summary>An alliance's line (men). Its members go as the default role, the only one there is yet.</summary>
        public static Pb Line(Conquests.Side side, AllianceStore.Alliance alliance, int defaultRole)
        {
            var men = Pb.New().Msg(1, AllianceProtocol.Block(alliance));
            if (side.Present > 0 && defaultRole > 0)
                men.Msg(2, Pb.New().Var(1, side.Present).Var(2, defaultRole));
            men.VarIfNotZero(3, side.Domination);
            for (int kind = 0; kind < Conquests.ScoreKinds; kind++)
                men.Msg(5, Pb.New().VarIfNotZero(1, side.Scores[kind]).VarIfNotZero(4, kind));
            return men.VarIfNotZero(6, side.Present);
        }

        /// <summary>The conquest over (mdz): { f1 the winner }, «KohEnd» to the window.</summary>
        public static byte[] BuildEnd(AllianceStore.Alliance winner)
            => Pb.New().Msg(1, AllianceProtocol.Block(winner)).Build();
    }
}
