using System;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>PvM experience: Giny 2.68's level ratios and personal wisdom, without idols.</summary>
    public static class CombatExperience
    {
        internal static readonly double[] GroupCoefficients = { 1, 1.1, 1.5, 2.3, 3.1, 3.6, 4.2, 4.7 };

        public static int CombatLevel(int level) => Math.Clamp(level, 1, 200);

        public static long Share(long baseXp, int level, IReadOnlyList<int> levels,
                                 int strongestMonster, int totalMonsterLevel, int wisdom = 0)
        {
            if (baseXp <= 0 || levels.Count == 0 || strongestMonster <= 0 || totalMonsterLevel <= 0) return 0;
            int own = CombatLevel(level);
            double total = levels.Sum(l => (double)CombatLevel(l));
            int highest = levels.Max(CombatLevel);
            int counting = Math.Clamp(levels.Count(l => CombatLevel(l) * 3 >= highest), 1, 8);
            double difference = total - 5 > totalMonsterLevel ? totalMonsterLevel / total
                : total + 10 < totalMonsterLevel ? (total + 10) / totalMonsterLevel : 1;
            long groupXp = (long)Math.Truncate(baseXp * GroupCoefficients[counting - 1] * difference);
            double capped = Math.Min(own, Math.Truncate(2.5 * strongestMonster));
            double limitPercent = capped / total * 100;
            long personal = (long)Math.Truncate(limitPercent / 100 * groupXp);
            return Math.Max(1, (long)Math.Truncate(personal * (100d + Math.Max(0, wisdom)) / 100d));
        }

        /// <summary>Stump's loss reward, at twenty percent. No minimum XP when no monster died.</summary>
        public static long AfterOutcome(long experience, bool won)
            => won ? Math.Max(0, experience) : (long)Math.Round(Math.Max(0, experience) * 0.20);

        public readonly record struct Allocation(long Character, long MountTaken, long Mount,
                                                 long GuildTaken, long Guild);

        /// <summary>Stump's conversion by level gap; the full donation is taken, only the converted XP credited.</summary>
        public static long ConvertedDonation(long taken, int characterLevel, int recipientLevel)
        {
            int gap = CombatLevel(characterLevel) - Math.Max(1, recipientLevel);
            decimal percent = gap > 70 ? 1 : gap > 60 ? 1.5m : gap > 50 ? 2 : gap > 40 ? 3
                : gap > 30 ? 4 : gap > 20 ? 6 : gap > 10 ? 8 : 10;
            return (long)(Math.Max(0, taken) * percent / 100m);
        }

        public static Allocation Allocate(long experience, int level, int mountPercent = 0, int mountLevel = 1,
                                          int guildPercent = 0, int guildLevel = 1)
        {
            long remaining = Math.Max(0, experience);
            long mountTaken = (long)(remaining * (decimal)Math.Clamp(mountPercent, 0, 90) / 100m);
            remaining -= mountTaken;
            long guildTaken = (long)(remaining * (decimal)Math.Clamp(guildPercent, 0, 90) / 100m);
            return new Allocation(remaining - guildTaken, mountTaken,
                ConvertedDonation(mountTaken, level, mountLevel), guildTaken,
                ConvertedDonation(guildTaken, level, guildLevel));
        }
    }
}
