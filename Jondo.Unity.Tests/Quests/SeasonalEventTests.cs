using System;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.Quests
{
    /// <summary>The quests' «Sc»: content that is on, and the yearly events on their dates (Dofus pour les noobs, 2026).</summary>
    public sealed class SeasonalEventTests
    {
        [Theory]
        [InlineData(SeasonalEvents.Halouine, 2026, 10, 22, true)]
        [InlineData(SeasonalEvents.Halouine, 2026, 11, 2, true)]
        [InlineData(SeasonalEvents.Halouine, 2026, 11, 3, false)]
        [InlineData(SeasonalEvents.Halouine, 2026, 10, 1, false)]
        [InlineData(SeasonalEvents.SainBallotin, 2026, 2, 10, true)]
        [InlineData(SeasonalEvents.SainBallotin, 2026, 2, 16, true)]
        [InlineData(SeasonalEvents.SainBallotin, 2026, 2, 17, false)]
        [InlineData(SeasonalEvents.Pwak, 2026, 3, 31, true)]
        [InlineData(SeasonalEvents.Pwak, 2026, 4, 27, true)]
        [InlineData(SeasonalEvents.Pwak, 2026, 4, 28, false)]
        [InlineData(SeasonalEvents.Vulkania, 2026, 7, 1, true)]
        [InlineData(SeasonalEvents.Vulkania, 2026, 9, 1, false)]
        [InlineData(SeasonalEvents.Ripate, 2026, 9, 19, true)]
        [InlineData(SeasonalEvents.Ripate, 2026, 10, 1, false)]
        [InlineData(703, 2026, 1, 1, true)]     // Frigost's quests: always on
        [InlineData(842, 2026, 6, 6, true)]
        public void Each_switch_is_on_when_it_should(int criterion, int year, int month, int day, bool on)
            => Assert.Equal(on, SeasonalEvents.IsOn(criterion, new DateTime(year, month, day, 12, 0, 0)));
    }
}
