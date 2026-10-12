using System;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// The frames of the travel items that are not the map change itself, byte for byte against the
    /// captures: the choice an ankla proposes, and the delayed use a home potion opens with.
    /// </summary>
    public static class TravelProtocol
    {
        /// <summary>The delay a use of an item is (hpp's and hpq's f3/f2, the client's enum lau): 1.</summary>
        public const int ObjectUseDelay = 1;

        /// <summary>
        /// A choice proposed (kss): f1 each option { f1 its place, f2 its id }, f2 true, f3 the
        /// choice. «usar ankla de anutropia» frame by frame: options 239, 240, 241 and 258 at 0
        /// to 3, then 1 and 28, «0a0310ef010a05080110f0010a05080210f1010a0508031082021001181c».
        /// The client answers with the place chosen (kso { f1 }).
        /// </summary>
        public static byte[] BuildChoice(TravelData.Choice choice)
        {
            var kss = Pb.New();
            for (int place = 0; place < choice.Options.Count; place++)
                kss.Msg(1, Pb.New().VarIfNotZero(1, place).Var(2, choice.Options[place].Id));
            return kss.Var(2, 1).Var(3, choice.Id).Build();
        }

        /// <summary>
        /// An item's use under way (hpp): f1 the item, f2 when it ends, f3 the kind of delay, f5 who.
        /// «utilizar pocima de hogar»: { 2542, the clock, 1, the character }, its f2 the same
        /// milliseconds as the lqu's, so it ends at once.
        /// </summary>
        public static byte[] BuildDelayedUse(int itemTemplate, long endsMs, long characterId)
            => Pb.New().Var(1, itemTemplate).Var(2, endsMs).Var(3, ObjectUseDelay).Var(5, characterId).Build();

        /// <summary>The use done (hpq): f1 who, f2 the kind of delay.</summary>
        public static byte[] BuildDelayFinished(long characterId)
            => Pb.New().Var(1, characterId).Var(2, ObjectUseDelay).Build();
    }
}
