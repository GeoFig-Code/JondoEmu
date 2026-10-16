using System;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The experience a player takes out of the monsters he beat: the client's own sum.
    /// </summary>
    /// <remarks>
    /// The client works it out itself, for the tooltip of a monster group
    /// (WorldRpMonstersGroupTooltipBuilder.GetMonstersGroupData and its formula service, ekn, in
    /// 3.6.10.11). Its "experience" and "group experience" lines are this sum, so a server paying
    /// anything else pays what the player was not shown.
    ///
    ///   xp      the gradeXp of every monster, added up
    ///   group   1, 1.1, 1.5, 2.3, 3.1, 3.6, 4.2, 4.7 by how many players count: those at a third
    ///           of the highest level or more, the third rounded down. It is the client's static
    ///           double[], and the eight values are in its global-metadata.dat byte for byte.
    ///   ratio   the monsters' levels M against the players' P: M/P when M is under P-5,
    ///           (P+10)/M when it is over P+10, 1 otherwise
    ///   part    his level, up to two and a half times the strongest monster's, out of P
    ///   wisdom  (100 + wisdom) / 100
    ///
    /// trunc(xp × group × ratio), then trunc(that × part), then trunc(that × wisdom), and at least
    /// one. Every level counts up to 200: the client clamps its own and the party's, so an Omega
    /// level is a 200. The companions it adds to P without counting them for the group, this
    /// server does not have.
    ///
    /// Three more factors of the client are one here, as they are there:
    ///   - the map's reward rate and the character's experience bonus, which no message of this
    ///     client sets;
    ///   - the alliance prism's 25, for fighting where one's own alliance has its prism, which
    ///     the client divides by a hundred in whole numbers: 1 + 25/100 is 1.
    ///
    /// Measured against the captures:
    ///   - The solo fight of «entrar a combate-desconectarse-reconectar…» had eight monsters,
    ///     13,894 gradeXp, levels adding up to 109, the strongest at 15, wisdom 100 and its only
    ///     challenge failed. It gives 1,400 before wisdom and 2,800 after; the real server paid 2,980.
    ///   - The four-monster fight of the follow capture gives 559 for the 354 (wisdom 352,
    ///     challenge 65 %), where it paid 589.
    /// What makes up those 5 or 6 %, the client does not show.
    /// </remarks>
    public static class CombatExperience
    {
        internal static readonly double[] GroupCoefficients = { 1, 1.1, 1.5, 2.3, 3.1, 3.6, 4.2, 4.7 };

        /// <summary>The level a character counts at for experience: up to 200.</summary>
        public static int CombatLevel(int level) => Math.Clamp(level, 1, 200);

        /// <summary>
        /// One player's experience out of the monsters' <paramref name="baseXp"/>, among the
        /// <paramref name="levels"/> of his side (his own included), with his own wisdom.
        /// </summary>
        public static long Share(long baseXp, int level, IReadOnlyList<int> levels,
                                 int strongestMonster, int totalMonsterLevel, int wisdom = 0)
        {
            if (baseXp <= 0 || levels.Count == 0 || strongestMonster <= 0 || totalMonsterLevel <= 0) return 0;
            int own = CombatLevel(level);
            double total = levels.Sum(l => (double)CombatLevel(l));
            int highest = levels.Max(CombatLevel);
            int counting = Math.Clamp(levels.Count(l => CombatLevel(l) >= highest / 3), 1, GroupCoefficients.Length);
            double ratio = totalMonsterLevel < total - 5 ? totalMonsterLevel / total
                : totalMonsterLevel > total + 10 ? (total + 10) / totalMonsterLevel : 1;
            long groupXp = (long)Math.Truncate(baseXp * GroupCoefficients[counting - 1] * ratio);
            double capped = Math.Min(own, Math.Truncate(2.5 * strongestMonster));
            long personal = (long)Math.Truncate(capped / total * 100 / 100 * groupXp);
            return Math.Max(1, (long)Math.Truncate(personal * (100d + Math.Max(0, wisdom)) / 100d));
        }
    }
}
