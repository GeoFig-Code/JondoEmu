using System;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The inventory's bonus slots, past the worn ones: what a candy, a blessing or a mutation puts
    /// there, and how it counts down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They are the slots the item types say: 20 the mutation (type 27), 21 the boost food (28, «Alimento
    /// boost», what a candy gives), 22 and 23 the blessings (29), 24 and 25 the curses (30), 26 the roleplay
    /// buff (31) and 27 the follower (32). Measured: the orbalantir's mutations arrive in 20 (iua) and the
    /// tournament character carries a blessing in 22, +1 AP, +1 MP and +1 range for 666 fights.
    /// </para>
    /// <para>
    /// The fights they last are effect 811 with three numbers -- left, total, total --: the blessing
    /// enters the world at 665 of 666 and goes down by one at the end of every fight (iuj), and the
    /// mutation at 1 of 1 is taken away at the end of its fight (ium).
    /// </para>
    /// <para>
    /// The boost food and the blessings are placed as they are: they do nothing but add
    /// characteristics. The mutation goes through MutationHandler, which tells the client its spells,
    /// look and sheet. The roleplay buff and the follower, which change the look, are not placed, and
    /// the curses have nothing that gives them.
    /// </para>
    /// </remarks>
    public static class BonusSlots
    {
        public const int First = 20;
        public const int Last = 27;

        /// <summary>The boost food's slot, the candies'.</summary>
        public const int BoostFood = 21;

        public const int MutationType = 27;
        public const int BoostFoodType = 28;
        public const int BlessingType = 29;

        /// <summary>The item types that go to a bonus slot, placed or not.</summary>
        private static readonly HashSet<int> SlotTypes = new() { 27, 28, 29, 30, 31, 32 };

        public static bool IsBonusSlot(int position) => position >= First && position <= Last;

        /// <summary>Whether an item of this type belongs in a bonus slot.</summary>
        public static bool IsSlotType(int itemType) => SlotTypes.Contains(itemType);

        /// <summary>The slots this server places an item type in; empty for the ones it does not.</summary>
        public static IReadOnlyList<int> SlotsFor(int itemType) => itemType switch
        {
            MutationType => new[] { Mutations.Slot },
            BoostFoodType => new[] { BoostFood },
            BlessingType => new[] { 22, 23 },
            _ => Array.Empty<int>(),
        };

        /// <summary>
        /// An item's effects as it is placed: its fights and victories as left, total and total, the way
        /// the captured blessing carries them. The template has the count alone.
        /// </summary>
        public static List<Equipment.ItemEffect> Fresh(IEnumerable<Equipment.ItemEffect> effects)
            => effects.Select(e => IsCount(e.Effect)
                    ? Counted(e.Effect, Alterations.CountOf(e), Alterations.CountOf(e))
                    : e)
                .ToList();

        /// <summary>
        /// One fight over for an item: its fights one fewer, and its victories if it was won. Null when
        /// nothing in it counts; Spent when it has reached zero and goes.
        /// </summary>
        public static (List<Equipment.ItemEffect> Effects, bool Spent)? CountDown(IReadOnlyList<Equipment.ItemEffect> effects,
                                                                                bool won)
        {
            bool counted = false, spent = false;
            var after = new List<Equipment.ItemEffect>();
            foreach (var effect in effects)
            {
                bool counts = effect.Effect == AlterationCatalogue.FightsLeft
                              || (won && effect.Effect == AlterationCatalogue.VictoriesLeft);
                if (!counts || effect.Value <= 0)
                {
                    after.Add(effect);
                    continue;
                }
                counted = true;
                long left = effect.Value - 1;
                if (left <= 0) spent = true;
                long total = Math.Max(effect.DiceNum, effect.Value);
                after.Add(Counted(effect.Effect, left, total));
            }
            return counted ? (after, spent) : null;
        }

        private static bool IsCount(int effect)
            => effect is AlterationCatalogue.FightsLeft or AlterationCatalogue.VictoriesLeft;

        /// <summary>A count as the item carries it: f6 { f1 left, f2 total, f3 total }.</summary>
        private static Equipment.ItemEffect Counted(int effect, long left, long total)
            => new(effect, left, total, total);
    }
}
