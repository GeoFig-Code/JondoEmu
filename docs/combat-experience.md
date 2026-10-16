# Combat experience

Ordinary PvM uses the base distribution from Giny.NETCore branch `2.68`, commit
`45641404ef0eaf757d9921249c2ca4d20259574d`. Mount and guild donations use the level-gap
conversion from Stump-2.71 commit `c4713c90a232611c5289724869320a1837df213a`, without
its custom guild multiplier. These are emulator references, not a claim of exact
compatibility with the official Unity server.

`CombatExperience.Share` sums monster grade XP and applies the group coefficient
(1, 1.1, 1.5, 2.3, 3.1, 3.6, 4.2, 4.7). Only characters at least one third of the
highest character level count for that bonus. All participating character levels
count in the denominator, including characters who died or are disconnected.
Combat levels are limited to 200; Omega levels do not increase them.

If P is the sum of character levels and M the sum of monster levels, the level
coefficient is M/P when P-5 > M, (P+10)/M when P+10 < M, and 1 otherwise. Individual
weight is min(character level, truncate(2.5 * strongest monster level)) / P.
The denominator is never reduced to the sum of the capped weights. The same
rules apply in solo. Group XP and the individual share are truncated separately,
then personal wisdom multiplies the share by (100 + max(0, wisdom)) / 100.
Positive base XP has Giny's minimum of one XP before server/challenge/outcome bonuses.

The server XP bonus and the shared challenge bonus follow the personal share.
On defeat, only dead, non-summoned, non-illusion monsters contribute XP **and**
monster levels to the calculation. The result pays 20%, rounded as in Stump
(`Math.Round`, midpoint to even). Defeat gives no challenge bonus, kamas, or loot.
No kills means no XP. PvP and Infinite Dreams retain their separate reward rules.

## Donations

Donations follow the outcome adjustment. The full mount percentage is removed
first, then the guild percentage is removed from the remainder. Each percentage
is truncated to whole XP. The recipient gets the converted amount, not the entire
amount taken from the character. The unconverted portion is not redistributed.

Conversion uses character combat level minus recipient level. Stump's strict `>`
boundaries are retained:

| Level gap | XP credited from the amount taken |
| --- | --- |
| <= 10 | 10% |
| > 10 and <= 20 | 8% |
| > 20 and <= 30 | 6% |
| > 30 and <= 40 | 4% |
| > 40 and <= 50 | 3% |
| > 50 and <= 60 | 2% |
| > 60 and <= 70 | 1.5% |
| > 70 | 1% |

Converted credits are truncated. There is no guild x10, automatic mount bonus,
VIP bonus, IP bonus, or random XP cap. Actual mount XP, guild XP, and the member's
guild contribution are persisted in SQLite. Guild level follows its existing
Jondo curve. The donation transaction completes before end screens are sent.

## Configuration API

The client does not yet have handlers for donation settings or a mount progression
window. The server-side API is prepared in `ExperienceDonations`:

```csharp
// Rates are percentages, each in [0, 90]. Settings persist across connections.
ExperienceDonations.SetRates(characterId, mountPercent: 20, guildId: guildId, guildPercent: 50);

// Register the mount's known level; this preserves XP already earned by its item UID.
ExperienceDonations.SetMountLevel(mountItemUid, level: 50);
var progress = ExperienceDonations.MountOf(mountItemUid);
```

Both rates default to zero. Mount XP follows the equipped real mount's item UID;
pets, petsmounts, cosmetic mounts and unequipped mounts do not receive donations.
An unregistered mount starts at level 1 with zero XP. Mount XP is accumulated, but
automatic mount level progression/breeding is not implemented: a future mount
subsystem must supply its level through `SetMountLevel`. No new XP curve is invented.
Guild settings are bound to the configured guild membership; a missing or different
guild does not take XP. The caller must authorize configuration changes before
exposing these APIs to clients.
