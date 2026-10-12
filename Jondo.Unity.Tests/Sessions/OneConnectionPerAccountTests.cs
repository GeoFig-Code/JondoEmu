using System;
using System.Threading.Tasks;
using Jondo.Unity.Server.Network;
using Xunit;
using Wire = Jondo.Unity.Tests.Combat.PortalTests.Wire;

namespace Jondo.Unity.Tests.Sessions
{
    /// <summary>
    /// One account plays from one connection: when it comes in again, the older connection is
    /// closed, so the same character is never in the world twice.
    /// </summary>
    public class OneConnectionPerAccountTests
    {
        private const long Character = 8_970_000_001;

        [Fact]
        public async Task The_account_coming_in_again_closes_its_older_connection()
        {
            // The same character, so the same account (Wire binds it from the character).
            await using var older = await Wire.Open(Character);
            await using var newer = await Wire.Open(Character);
            Assert.Equal(older.Session.AccountId, newer.Session.AccountId);

            int closed = await SessionRegistry.CloseOthersOfAsync(newer.Session, TimeSpan.FromMilliseconds(200));

            Assert.Equal(1, closed);
            Assert.Throws<ObjectDisposedException>(() => older.Session.Stream!.Write(new byte[1], 0, 1));
            newer.Session.Stream!.Write(new byte[1], 0, 1);   // the newer one is untouched
        }

        [Fact]
        public async Task Another_account_is_left_alone()
        {
            await using var one = await Wire.Open(Character + 1);
            await using var other = await Wire.Open(Character + 2);
            Assert.NotEqual(one.Session.AccountId, other.Session.AccountId);

            Assert.Equal(0, await SessionRegistry.CloseOthersOfAsync(other.Session, TimeSpan.FromMilliseconds(200)));
            one.Session.Stream!.Write(new byte[1], 0, 1);
        }
    }
}
