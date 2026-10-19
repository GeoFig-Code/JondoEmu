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
    /// After the wisdom, the client's other factors:
    ///   - the map's reward rate, which no message of this client sets: 1;
    ///   - the character's experience bonus, <see cref="WithCharacterBonus"/>: f1 − 100 of the hox,
    ///     5 in every capture, so 5 here too (<see cref="CharacterBonusPercent"/>);
    ///   - the alliance prism's 25, for fighting where one's own alliance has its prism, which
    ///     the client divides by a hundred in whole numbers: 1 + 25/100 is 1;
    ///   - last, the limit the player sets on what he earns (<see cref="ExperienceLimit"/>).
    ///
    /// Measured against the captures:
    ///   - The solo fight of «entrar a combate-desconectarse-reconectar…» had eight monsters,
    ///     13,894 gradeXp, levels adding up to 109, the strongest at 15, wisdom 100, a hox of 105
    ///     and its only challenge failed. It gives 1,400 before wisdom, 2,800 after and 2,939 with
    ///     the bonus; the real server paid 2,980.
    ///   - The four-monster fight of the follow capture gives 339 for the 354 (wisdom 352), 355
    ///     with the bonus and 585 with the challenge's 65 %, where it paid 589.
    /// What makes up the last 1 %, the client does not show.
    /// </remarks>
    public static class CombatExperience
    {
        internal static readonly double[] GroupCoefficients = { 1, 1.1, 1.5, 2.3, 3.1, 3.6, 4.2, 4.7 };

        /// <summary>
        /// The character's experience bonus, in percent: f1 − 100 of the hox (ees.wtx keeps it,
        /// eet.xbc gives it to the tooltip). Every capture carries 105, whatever gives it on the
        /// real server, so every character here has 5; the hox world entry sends says so.
        /// </summary>
        public const int CharacterBonusPercent = 5;

        /// <summary>
        /// The bonus for fighting in a subarea whose prism is one's own alliance's, in percent: the
        /// 25 the client's tooltip returns (WorldRpMonstersGroupTooltipBuilder.ComputeXpAlliancePrismBonus,
        /// 0x19 when the subarea's prism carries the player's alliance, whatever its state). The
        /// client's guide says 50 (Translations 1149317); the client's code is what reckons, so 25.
        /// Its tooltip then divides it by a hundred in whole numbers and never shows it.
        /// </summary>
        public const int AlliancePrismBonusPercent = 25;

        /// <summary>Whether a character's alliance holds the prism of the subarea of a map.</summary>
        public static bool FightsUnderOwnPrism(long characterId, long mapId)
        {
            int subArea = MapManager.GetMapInfo(mapId)?.SubAreaId ?? 0;
            var prism = subArea == 0 ? null : PrismStore.InSubArea(subArea);
            return prism != null && AllianceStore.AllianceOf(characterId)?.Id == prism.AllianceId;
        }

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

        /// <summary>
        /// Some experience with the character's bonus, as the client multiplies its tooltip:
        /// (int)(xp × (bonus / 100 + 1)), the factor worked out in single precision. So 5 % is
        /// 1.0499999523…, and 2,800 comes out at 2,939, not 2,940 -- what the tooltip shows.
        /// </summary>
        public static long WithCharacterBonus(long experience, int bonusPercent)
        {
            if (experience <= 0) return 0;
            float factor = (float)bonusPercent / 100f + 1f;
            return (long)(experience * (double)factor);
        }
    }
}
