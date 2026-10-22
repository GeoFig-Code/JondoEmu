using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jondo.Unity.Launcher;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>The guidebook's suggestions: the cards that fit the request, the locked ones aside.</summary>
    public class ActivitySuggestionTests
    {
        private static bool Available => File.Exists(Paths.ActivitySuggestionsJson);
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void The_cards_fit_the_levels_the_category_and_the_area()
        {
            if (!Available) return;
            Assert.Equal(1410, ActivitySuggestions.All.Count);

            var drawn = ActivitySuggestions.Draw(40, 60, 0, ActivitySuggestions.AnyArea, 50, new HashSet<int>(), _ => 0, Now, new Random(1));
            Assert.NotEmpty(drawn);
            Assert.True(drawn.Count <= 50);
            Assert.All(drawn, id => Assert.InRange(ActivitySuggestions.All.Single(s => s.Id == id).Level, 40, 60));

            // A category takes its children's: 31 to 35 are under 8.
            var underEight = ActivitySuggestions.Draw(1, 200, 8, ActivitySuggestions.AnyArea, 2000, new HashSet<int>(), _ => 0, Now, new Random(1));
            Assert.All(underEight, id => Assert.True(ActivitySuggestions.InCategory(ActivitySuggestions.All.Single(s => s.Id == id), 8)));

            // An area: only the suggestions whose map is in it.
            var nowhere = ActivitySuggestions.Draw(1, 200, 0, 99999, 50, new HashSet<int>(), _ => 1, Now, new Random(1));
            Assert.Empty(nowhere);
        }

        [Fact]
        public void A_locked_card_is_not_drawn_again_and_leaves_its_room()
        {
            if (!Available) return;
            var locked = new HashSet<int> { 128 };
            var drawn = ActivitySuggestions.Draw(1, 200, 0, ActivitySuggestions.AnyArea, 10, locked, _ => 0, Now, new Random(2));
            Assert.Equal(9, drawn.Count);
            Assert.DoesNotContain(128, drawn);
        }

        [Fact]
        public void The_answer_puts_the_locked_cards_first()
        {
            var hmn = ProtoMessage.Parse(ActivitySuggestionHandler.Build(new[] { 128 }, new[] { 130, 131 }));
            Assert.Equal(new[] { 1, 2 }, hmn.Fields.Select(f => f.FieldNumber));
        }
    }
}
