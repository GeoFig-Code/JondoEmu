using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// Combat rewards planned once for every participant before any end screen is sent.
    /// </summary>
    /// <remarks>
    /// PvM uses Giny 2.68's base level ratios and personal wisdom. Challenges remain common.
    /// A defeat pays 20% on killed opponents only. Donations are taken mount first, then guild,
    /// with Stump 2.71's conversion by level gap and no custom guild multiplier.
    /// </remarks>
    public static partial class FightHandler
    {
        /// <summary>What one player takes out of a won fight.</summary>
        internal sealed class Reward
        {
            public long Xp { get; set; }
            public CombatExperience.Allocation Donations { get; set; }
            public long Kamas { get; set; }
            public Dictionary<int, int> Loot { get; set; } = new();

            /// <summary>The perfect pieces of the random loot inside <see cref="Loot"/>, with their exo.</summary>
            public List<RandomLoot.Drop> Perfect { get; set; } = new();
        }

        /// <summary>The group bonus to experience, by how many players count for it.</summary>
        internal static readonly double[] GroupCoefficients = CombatExperience.GroupCoefficients;

        /// <summary>Each fight's shares, from the moment it is decided to the last player's end.</summary>
        private static readonly ConcurrentDictionary<long, Dictionary<long, Reward>> _rewards = new();

        /// <summary>
        /// One player's PvM share, including the level mismatch in solo as well as in a group.
        /// </summary>
        internal static long XpShare(long baseXp, int level, IReadOnlyList<int> levels, int strongestMonster,
                                     int totalMonsterLevel, int wisdom = 0)
            => CombatExperience.Share(baseXp, level, levels, strongestMonster, totalMonsterLevel, wisdom);

        /// <summary>One player's kamas out of the fight's: his prospecting's part of the team's.</summary>
        internal static long KamasShare(long total, int prospecting, IReadOnlyList<int> prospectings)
        {
            if (total <= 0 || prospectings.Count <= 1) return Math.Max(0, total);
            long sum = prospectings.Sum(p => (long)Math.Max(1, p));
            return (long)Math.Floor((double)total * Math.Max(1, prospecting) / sum);
        }

        /// <summary>Prospecting: a hundred, a tenth of chance, and what the gear gives.</summary>
        private static int ProspectingOf(GameSession session)
        {
            using (SessionContext.Push(session))
            {
                Equipment.Bonuses().TryGetValue(ConnectionProtocol.Stat.Prospecting, out long fromGear);
                return 100 + session.State.TotalChance / 10 + (int)fromGear;
            }
        }

        /// <summary>
        /// PvM rewards for every participant, including XP-only defeat rewards. Victory loot is
        /// rolled in each player's session. A dream's fights are not cut: in the four-player dream
        /// capture every player gets the same experience and the same items.
        /// </summary>
        /// <remarks>
        /// And a dream's fights pay the dream's loot, not the monsters' (see <see cref="DreamLootOf"/>):
        /// in the jyg of the invitation capture (frame 5572) each of the four players has 17 dream
        /// reflections, one of them a Runa astral legendaria too, and none of them a kama or
        /// anything of the monsters'. The Fin du rêve is won once its minimum of waves has fallen,
        /// whoever is standing at its end -- the client's own words for it, "Una vez pasado el
        /// umbral, la victoria es definitiva" (1178499) -- so its players are paid then even when
        /// the last wave had the better of them.
        /// </remarks>
        internal static void PlanRewards(FightInstance fight)
        {
            if (_rewards.ContainsKey(fight.FightId)) return;
            // A tax collector's attackers share its bag; a prism pays nothing, and the defenders of
            // either win nothing but having saved it.
            if (fight.DefendingAllianceId != 0)
            {
                var shares = TaxCollectorHandler.ShareTheBag(fight);
                if (shares.Count > 0) _rewards[fight.FightId] = shares.ToDictionary(s => s.Key, s => new Reward { Loot = s.Value });
                return;
            }
            if (!fight.Reglas.ReparteBotin || fight.Reglas.PagaElKoliseo) return;

            bool won = fight.Azul.Concat(fight.Rojo).Any(f => !f.IsMonster && !f.EsInvocado && fight.HaGanado(f.Id));
            var dream = DreamHandler.PayOf(fight, won);

            var participants = new List<(Fighter Fighter, GameSession Session)>();
            foreach (var fighter in fight.Azul.Concat(fight.Rojo))
            {
                if (fighter.IsMonster || fighter.EsInvocado || fighter.EsIlusion) continue;
                var session = SessionRegistry.FindByCharacter(fighter.Id);
                if (session != null) participants.Add((fighter, session));
            }
            if (participants.Count == 0) return;

            var paying = fight.Rojo.Where(m => m.IsMonster && !m.EsInvocado && !m.EsIlusion).ToList();
            var killed = paying.Where(m => !m.IsAlive).ToList();
            int extra = ChallengeWatcher.EndBonus(fight, won);
            long xpAlone = ServerSettings.WithBonus(ConElExtra(paying.Sum(m => (long)m.XpReward), extra),
                                                    ServerSettings.Current.ExperienceBonusPercent);
            long kamasAlone = ServerSettings.WithBonus(ConElExtra(paying.Sum(m => 10L + (m.Level * 5L)), extra),
                                                       ServerSettings.Current.KamasBonusPercent);
            bool shared = !Dreams.IsDreamMap(fight.RoleplayMapId);
            var prospectings = participants.Select(w => ProspectingOf(w.Session)).ToList();

            var plan = new Dictionary<long, Reward>();
            for (int i = 0; i < participants.Count; i++)
            {
                var (fighter, session) = participants[i];
                bool rewardedVictory = fight.HaGanado(fighter.Id) || dream?.Finished == true;
                Dictionary<int, int> loot = new();
                var perfect = new List<RandomLoot.Drop>();
                if (rewardedVictory)
                {
                    if (dream != null) loot = DreamLootOf(fight, dream);
                    else using (SessionContext.Push(session)) loot = RollLoot(fight, extra, perfect);
                }
                long xp = 0;
                CombatExperience.Allocation donations = default;
                if (!shared)
                {
                    // Dreams have their own reward rules; neither their losses nor PvP use PvM XP.
                    xp = rewardedVictory ? xpAlone : 0;
                }
                else if (fight.Reglas.EnfrenteHayMonstruos)
                {
                    var defeated = rewardedVictory ? paying : killed;
                    var levels = fight.Aliados(fighter.Id).Where(f => !f.IsMonster && !f.EsInvocado && !f.EsIlusion)
                        .Select(f => CombatExperience.CombatLevel(f.Level)).ToList();
                    using (SessionContext.Push(session))
                    {
                        int wisdom = session.State.TotalWisdom + StatsHandler.GetEquipBonus(ConnectionProtocol.Stat.Wisdom);
                        xp = XpShare(defeated.Sum(m => (long)m.XpReward), session.State.CharacterLevel, levels,
                            defeated.Count == 0 ? 0 : defeated.Max(m => m.Level), defeated.Sum(m => m.Level), wisdom);
                        xp = ServerSettings.WithBonus(xp, ServerSettings.Current.ExperienceBonusPercent);
                        xp = ConElExtra(xp, extra);
                        xp = CombatExperience.AfterOutcome(xp, rewardedVictory);
                        var mount = Equipment.All.FirstOrDefault(item => item.Position == Mounts.Slot
                            && Mounts.CanGainExperience(item.Template));
                        donations = ExperienceDonations.Credit(fighter.Id, xp, session.State.CharacterLevel, mount?.Uid ?? 0);
                        xp = donations.Character;
                    }
                }
                plan[fighter.Id] = new Reward
                {
                    Xp = xp,
                    Donations = donations,
                    Kamas = !rewardedVictory || dream != null ? 0 : shared ? KamasShare(kamasAlone, prospectings[i], prospectings) : kamasAlone,
                    Loot = loot,
                    Perfect = perfect,
                };
            }
            // A Gigalodón fight's salt goes into the raid's pool, the same amount for every winner.
            if (dream == null) GuildRaidManager.ShareFightSalt(fight.FightId, plan.ToDictionary(p => p.Key, p => p.Value.Loot));
            _rewards[fight.FightId] = plan;

            if (dream != null)
            {
                Program.LogDebug($"[Dreams] Fight #{fight.FightId} pays {participants.Count} dreamer(s) at a loot bonus of " +
                                 $"{dream.LootBonus}: {Dreams.ReflectionsFor(dream.LootBonus)} reflection(s) each" +
                                 (dream.Finished ? $", {dream.Fragments} dream fragment(s) for {dream.Waves} wave(s)." : "."));
            }

            if (participants.Count > 1)
            {
                Program.LogDebug($"[Fight] Share of #{fight.FightId} among {participants.Count}: " +
                                 string.Join(", ", plan.Select(p => $"{p.Key} {p.Value.Xp} xp {p.Value.Kamas} kamas " +
                                                                    $"{p.Value.Loot.Sum(l => l.Value)} item(s)")));
            }
        }

        /// <summary>
        /// A Koliseo fight is over: the ladder moves every player of both sides (see
        /// <see cref="KoliseoLadder"/>). Summons and illusions are nobody's rating.
        /// </summary>
        private static void RecordLadder(FightInstance fight)
        {
            if (!fight.Reglas.PagaElKoliseo || fight.KoliseoMode < 0) return;
            // Against JondoBots it is training: the ladder is not touched.
            if (fight.Todos.Any(f => f.IsBot)) return;
            var players = fight.Azul.Concat(fight.Rojo)
                               .Where(f => !f.IsMonster && !f.EsInvocado && !f.EsIlusion)
                               .ToList();
            var winners = players.Where(f => fight.HaGanado(f.Id)).Select(f => (f.Id, f.Level)).ToList();
            var losers = players.Where(f => !fight.HaGanado(f.Id)).Select(f => (f.Id, f.Level)).ToList();
            var after = KoliseoLadder.Record(fight.KoliseoMode, winners, losers);
            if (after.Count > 0)
            {
                Program.LogDebug($"[Koliseo] Ladder after #{fight.FightId}, mode {fight.KoliseoMode}: " +
                                 string.Join(", ", after.Select(s => $"{s.CharacterId} {s.Rating} " +
                                     (s.Placed ? $"league {s.League}" : $"{s.PlacementLeft} placement left"))));
            }
        }

        /// <summary>
        /// What a dream's fight drops for one of its winners: the dream's loot at the room's loot
        /// bonus (<see cref="Dreams.LootOf"/>), the Jondo coin every monster pays on this server,
        /// and, when the fight finished the dream, the dream fragments of its waves.
        /// </summary>
        private static Dictionary<int, int> DreamLootOf(FightInstance fight, DreamHandler.DreamPay dream)
        {
            var loot = Dreams.LootOf(dream.Difficulty, dream.Row, dream.LootBonus, TirarPorcentaje);
            void Add(int item, int count)
            {
                if (count <= 0) return;
                loot.TryGetValue(item, out int had);
                loot[item] = had + count;
            }

            Add(Managers.JondoCoin.TemplateId,
                fight.Rojo.Where(m => m.IsMonster && !m.EsInvocado).Sum(m => Managers.JondoCoin.RewardFor(m.Level)));
            Add(Dreams.FragmentItem, dream.Fragments);
            return loot;
        }

        /// <summary>This player's share of the fight, if it was planned.</summary>
        internal static Reward? RewardOf(FightInstance fight, long characterId)
            => _rewards.TryGetValue(fight.FightId, out var plan) && plan.TryGetValue(characterId, out var reward)
                ? reward
                : null;

        /// <summary>A share as the end screen shows it.</summary>
        private static FightProtocol.Spoils SpoilsOf(Reward reward)
        {
            var spoils = new FightProtocol.Spoils { Kamas = reward.Kamas };
            foreach (var kv in reward.Loot) spoils.Items.Add((kv.Value, kv.Key));
            return spoils;
        }

        /// <summary>The fight is over for everybody: its shares go.</summary>
        private static void ForgetRewards(FightInstance fight) => _rewards.TryRemove(fight.FightId, out _);
    }
}
