using System;
using System.Collections.Generic;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// What the alterations window shows, byte for byte against the captures: the alterations (lzs
    /// set, lzo lost, lzp all of them) and the items in the bonus slots (iua placed, iuj changed),
    /// which the same window lists beside them -- «data provided should be an ItemWrapper or an
    /// Alteration», says the client.
    /// </summary>
    /// <remarks>
    /// An alteration's entry (lzv):
    /// <code>
    ///   f1 since (ms)   f2 the alteration   f3 each effect   f4 its kind   f5 until (ms) or the count left
    /// </code>
    /// The effects are the item effects' entries (lng), with what is live in them: 811 and 817 as
    /// f6 { f1 left, f2 total, f3 total } -- the Trool potion's 624 fresh «320608011001180158ab06», at
    /// the end of the race «32041001180158ab06» --, 2833 as the date it ends, f2 { f1 day, f2 year,
    /// f3 month, f4 minute, f5 hour } in Paris time with Dofus's years (2026 is 656), and the ones
    /// that count minutes (2867 «Experiencia x#3% (#1 minuto)») as f6 { f1 the percentage, f2 the
    /// minutes left }: the oracle set at 01:13:12 that ends at 03:13:00 goes out with 119. The rest
    /// carry their one number in f4: 174's 50 initiative, 148's creature 2850.
    /// <para>
    /// The order is not the data's. Three of the four captured come in the order opposite to the
    /// client's list -- 625, the oracle's 859, the gladiators' 572 -- and the Trool's 624 in the same
    /// one; the reversed order is kept, which is right three times out of four and only changes the
    /// order of the lines in the tooltip.
    /// </para>
    /// </remarks>
    public static class AlterationProtocol
    {
        /// <summary>The effects whose second number is the minutes left of a timed alteration.</summary>
        private static readonly HashSet<int> MinuteEffects = new() { 2867, 2869, 3797 };

        /// <summary>Dofus's calendar runs 1370 years behind: the captures' September 2026 is 656.</summary>
        public const int DofusYearOffset = 1370;

        /// <summary>An alteration set (lzs): f1 its entry.</summary>
        public static byte[] BuildSet(Alterations.Active active, DateTime nowUtc)
            => Pb.New().Msg(1, Entry(active, nowUtc)).Build();

        /// <summary>An alteration lost (lzo): f1 its entry, as it stood.</summary>
        public static byte[] BuildLost(Alterations.Active active, DateTime nowUtc)
            => Pb.New().Msg(1, Entry(active, nowUtc)).Build();

        /// <summary>Every alteration (lzp): f1 each entry. Empty when there are none, as the captures send it.</summary>
        public static byte[] BuildList(IEnumerable<Alterations.Active> list, DateTime nowUtc)
        {
            var lzp = Pb.New();
            foreach (var active in list) lzp.Msg(1, Entry(active, nowUtc));
            return lzp.Build();
        }

        /// <summary>One alteration's entry (lzv).</summary>
        internal static Pb Entry(Alterations.Active active, DateTime nowUtc)
        {
            var entry = Pb.New().Var(1, active.FromMs).Var(2, active.Id);
            var definition = AlterationCatalogue.Of(active.Id);
            if (definition != null)
            {
                for (int i = definition.Effects.Count - 1; i >= 0; i--)
                    entry.Msg(3, Effect(definition.Effects[i], active, nowUtc));
            }
            entry.Var(4, (int)active.Kind);
            switch (active.Kind)
            {
                case Alterations.Kind.Timed:
                    entry.Var(5, active.UntilMs);
                    break;
                case Alterations.Kind.Fights:
                case Alterations.Kind.Victories:
                    entry.VarIfNotZero(5, active.Left);
                    break;
            }
            return entry;
        }

        /// <summary>One effect of an alteration, with what is live in it.</summary>
        private static Pb Effect(Equipment.ItemEffect effect, Alterations.Active active, DateTime nowUtc)
        {
            var line = Pb.New();
            int id = effect.Effect;
            if (id is AlterationCatalogue.FightsLeft or AlterationCatalogue.VictoriesLeft)
            {
                bool counted = active.Kind is Alterations.Kind.Fights or Alterations.Kind.Victories;
                long total = counted ? active.Total : Alterations.CountOf(effect);
                long left = counted ? active.Left : total;
                line.Msg(6, Pb.New().VarIfNotZero(1, left).Var(2, total).Var(3, total));
            }
            else if (id == AlterationCatalogue.Expiry)
            {
                long until = active.Kind == Alterations.Kind.Timed
                    ? active.UntilMs
                    : new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds() + Alterations.CountOf(effect) * 60_000L;
                line.Msg(2, Date(until));
            }
            else if (MinuteEffects.Contains(id))
            {
                long minutes = effect.DiceNum;
                if (active.Kind == Alterations.Kind.Timed)
                {
                    long left = (active.UntilMs - new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds()) / 60_000L;
                    minutes = Math.Clamp(left, 0, minutes > 0 ? minutes : long.MaxValue);
                }
                line.Msg(6, Pb.New().VarIfNotZero(1, effect.Value).VarIfNotZero(2, minutes).VarIfNotZero(3, effect.DiceSide));
            }
            else
            {
                long number = effect.Value != 0 ? effect.Value : (effect.DiceNum != 0 ? effect.DiceNum : effect.DiceSide);
                if (number != 0) line.Var(4, number);
            }
            return line.Var(11, id);
        }

        /// <summary>
        /// A date as the effects carry it: f1 day, f2 Dofus's year, f3 month, f4 minute, f5 hour, in
        /// the game's own time, which is Paris's.
        /// </summary>
        internal static Pb Date(long unixMs)
        {
            var when = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(unixMs), Paris);
            return Pb.New()
                .VarIfNotZero(1, when.Day)
                .Var(2, when.Year - DofusYearOffset)
                .Var(3, when.Month)
                .VarIfNotZero(4, when.Minute)
                .VarIfNotZero(5, when.Hour);
        }

        private static readonly TimeZoneInfo Paris = FindParis();

        private static TimeZoneInfo FindParis()
        {
            foreach (string id in new[] { "Europe/Paris", "Romance Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.Local;
        }

        /// <summary>
        /// An item placed in a bonus slot (iua): f3 { f1 the slot, f5 the item }, as the orbalantir's
        /// mutation arrives in slot 20.
        /// </summary>
        public static byte[] BuildSlotItemPlaced(Equipment.Item item)
            => Pb.New().Msg(3, SlotItem(item)).Build();

        /// <summary>
        /// An item that has changed (iuj): f1 { f1 the slot, f5 the item }. The real server sends one at
        /// the end of every fight for the blessing in slot 22, its fights left one fewer each time.
        /// </summary>
        public static byte[] BuildSlotItemChanged(Equipment.Item item)
            => Pb.New().Msg(1, SlotItem(item)).Build();

        private static Pb SlotItem(Equipment.Item item)
            => Pb.New().VarIfNotZero(1, item.Position)
                       .Msg(5, ConnectionProtocol.ItemBody(item.Template, item.Effects, item.Quantity, item.Uid));
    }
}
