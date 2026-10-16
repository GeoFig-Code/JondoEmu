# Combat experience

A fight against monsters pays each winner the experience the client itself works out
for the tooltip of a monster group. That tooltip's "experience" and "group experience"
lines come from `WorldRpMonstersGroupTooltipBuilder.GetMonstersGroupData` and its
formula service (class `ekn` in 3.6.10.11). The server uses the same sum, in
`CombatExperience.Share`.

## The sum

| Step | What the client does |
| --- | --- |
| Base | The `gradeXp` of every monster of the group, added up. The values in `world.db` match the client's `MonstersDataRoot`. |
| Levels | Every level counts up to 200: the player's, and each party member's. An Omega level is a 200. |
| Group bonus | 1, 1.1, 1.5, 2.3, 3.1, 3.6, 4.2, 4.7 by how many players count. A player counts with at least the highest level divided by three, in whole numbers. The eight values are the client's static `double[]`, found byte for byte in `global-metadata.dat`. |
| Level ratio | M is the monsters' levels added up, P the players'. The ratio is M/P when M < P − 5, (P + 10)/M when M > P + 10, and 1 otherwise. |
| Group XP | trunc(base × group bonus × ratio) |
| Player's part | His level, up to trunc(2.5 × the strongest monster's level), out of P: trunc(group XP × part). |
| Wisdom | trunc(part × (100 + wisdom) / 100), with a negative wisdom counting as zero. |
| Floor | At least 1 when the base is above zero. |
| Character bonus | trunc(xp × (bonus / 100 + 1)), the factor in single precision, so 2,800 with a 5 % bonus gives 2,939. See below. |

The group is the players of the winner's side who are still in the fight, dead or
alive. Someone who left is no longer in it, and gets nothing. The client also adds a
companion's level to P without counting the companion for the group bonus; this server
has no companions.

On top of the client's sum, the server adds:

* the challenges met, as a percentage shared by the whole side;
* the experience bonus of the server settings.

A defeat pays nothing. The two defeats in the captures pay no experience.

## The client's other factors

* **Map reward rate** (`pMapRewardRate`): the client multiplies by (100 + rate) / 100. No message of this client sets the rate, so it is 1.
* **Character experience bonus**: (1 + bonus / 100), with bonus = f1 − 100 of the `hox` sent at world entry. Every capture carries 105, a 5 % bonus, whatever gives it on the real server. So every character here has 5 % (`CombatExperience.CharacterBonusPercent`): the hox says so, the tooltip counts it, and the fights against monsters pay it. The f2 beside it, also 105, is sent as measured; the tooltip does not use it.
* **Alliance prism**: the client returns 25 when the player fights in a sub-area whose prism belongs to his own alliance. It then applies 1 + 25 / 100 in whole numbers, which is 1, so the tooltip never shows it.
* **Limit on earned experience**: last of all, the client keeps xp × limit / 100, truncated. The server does the same (see below).

## Against the captures

| Fight | Inputs | Client's sum | Real server |
| --- | --- | --- | --- |
| Solo, «entrar a combate-desconectarse-reconectar…» | 8 monsters, base 13,894, M = 109, strongest 15, level 354, wisdom 100, hox 105, its challenge failed | 2,800, 2,939 with the bonus | 2,980 |
| Group, four monsters of the follow capture | base 6,260, M = 50, strongest 14, levels 447 and 354; for the 354: wisdom 352, challenge 65 % | 339, 355 with the bonus, 585 with the challenge | 589 |

The last 1 % or so is not in the client and not in the captures, so it is not added.

## Limit on earned experience

The characteristics panel and the experience bar's menu offer «Limitar la experiencia
recibida a». The client sends `kts { f1: percent }`, and the server stores the limit and
answers with the `hox` that carries it in f4. The two captures «desde panel
caracteristicas limitar experiencia obtenida» are byte for byte:

| From → to | kts | hox |
| --- | --- | --- |
| 100 → 50 | `0832` | `086910692032` |
| 50 → 100 | `0864` | `086910692064` |

The limit is kept per character in world.db (`CharacterExperienceLimit`), and world
entry sends the character's own instead of the capture's 100. It is a percent between 0
and 100; experience under it is xp × limit / 100, truncated, as the client cuts its
tooltip.

The client writes «Has limitado tu ganancia de experiencia al {0}%» beside the experience
of a fight's end screen, of a quest's rewards and of the Koliseo. So those gains are the
ones cut: fights against monsters (each winner by his own limit, after the challenges and
the server's bonus), dreams, Koliseo victories and quests. Achievements, guild raid
rewards and experience scrolls carry no warning, and are paid whole.

## Not done

* **Experience given to a mount or a guild.** The client has the settings, its guild member card and the mount's experience share, but no capture shows how much of the given experience reaches the guild or the mount. Until one does, the character keeps all of it.
