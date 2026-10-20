using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The party search's announcements («Búsqueda de grupo», the client's lobbies): a player posts
    /// one for an activity and others apply to it, or join it outright when it says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read off the client (Core.UILogic.GroupSearch.Lobbies, the services fgk and fgr) and the
    /// capture «busqueda de grupo-crear anuncio-postularse a otro-autoeliminacion de mi anuncio».
    /// The kinds are the client's LobbyType: 1 dungeon, 2 quest, 3 raid, 4 wanted notice,
    /// 5 archmonster, 6 Kolizeum, 7 Infinite Dream. The owner is a member of his own announcement,
    /// as the capture's lists have him in both f11 and f12.
    /// </para>
    /// <para>
    /// The result codes are the client's own: each answer's handler turns them into its text, and
    /// those texts say what each one means (fgr.beqw, bequ, beqv, bera, beqx).
    /// </para>
    /// <para>
    /// Announcements live in memory, as a party does: a restart empties the board. An owner who is
    /// not connected has his announcement left off the board until he is back.
    /// </para>
    /// </remarks>
    public static class Lobbies
    {
        /// <summary>A player as an announcement shows him (joa).</summary>
        public sealed record Player(long CharacterId, string Uid, string Name, int Breed, int Level, int Sex, DateTime Since);

        public sealed class Lobby
        {
            public string Uid { get; init; } = Guid.NewGuid().ToString();
            public int Type { get; init; }
            public int ActivityId { get; init; }
            public int GroupSize { get; init; }
            public bool AutoJoin { get; init; }
            public List<int> Tags { get; set; } = new();
            public List<int> Successes { get; init; } = new();
            public int MinLevel { get; init; }
            public string Description { get; set; } = "";
            public DateTime Created { get; init; } = DateTime.UtcNow;
            public Player Owner { get; init; }
            public List<Player> Members { get; } = new();
            public List<Player> Applicants { get; } = new();
            public List<Player> Rejected { get; } = new();

            public bool IsFull => Members.Count >= GroupSize;
        }

        /// <summary>What an announcement asks for, as the creation form (jnv) sends it.</summary>
        public sealed record Spec(int Type, int ActivityId, int GroupSize, bool AutoJoin, IReadOnlyList<int> Tags,
                                  IReadOnlyList<int> Successes, int MinLevel, string Description);

        // ─── The client's codes ─────────────────────────────────────────────

        /// <summary>Creating or editing (jmr, read by fgr.beqw and beqy).</summary>
        public static class CreateCode
        {
            public const int Ok = 0;
            public const int InvalidTags = 3;
            public const int InvalidDescription = 4;
            public const int NotOwned = 5;
            public const int AlreadyOwned = 6;
            public const int InvalidGroupSize = 7;
            public const int InvalidActivityType = 8;
            public const int AlreadyMember = 10;
            public const int InvalidLevel = 17;
        }

        /// <summary>Applying (jnl, read by fgr.bequ).</summary>
        public static class ApplyCode
        {
            public const int Applied = 0;
            public const int Joined = 1;
            public const int InvalidAnnounce = 4;
            public const int CriteriaForPlayer = 5;
            public const int LobbyFullApply = 6;
            public const int LobbyFullMember = 7;
            public const int BlackListed = 9;
            public const int AlreadyApplying = 10;
            public const int PlayerFullApply = 11;
            public const int AlreadyMember = 12;
            public const int AlreadyOwner = 13;
        }

        /// <summary>Leaving or taking an application back (joi, read by fgr.beqv).</summary>
        public static class LeaveCode
        {
            public const int Ok = 0;
            public const int InvalidAnnounce = 4;
            public const int NotInLobby = 5;
        }

        /// <summary>The owner's answer to an applicant (jpc, read by fgr.bera).</summary>
        public static class AnswerCode
        {
            public const int Rejected = 0;
            public const int Accepted = 1;
            public const int NotOwned = 3;
            public const int LobbyFull = 4;
            public const int NoApplier = 5;
        }

        /// <summary>Deleting (jni, read by fgr.beqx).</summary>
        public static class DeleteCode
        {
            public const int Ok = 0;
            public const int NotAuthor = 3;
        }

        // ─── Limits ─────────────────────────────────────────────────────────

        /// <summary>The kinds the client knows (LobbyType 1 to 7).</summary>
        public const int FirstType = 1, LastType = 7;

        /// <summary>A party is two to eight.</summary>
        public const int MinGroupSize = 2, MaxGroupSize = 8;

        /// <summary>
        /// INFERRED: how many applications an announcement takes and a player sends at once -- the
        /// client has a text for each limit (lobbyFullApply, playerFullApply) but no figure.
        /// </summary>
        public const int MaxApplicants = 20, MaxApplications = 10;

        public const int MaxDescription = 300;

        private static readonly List<Lobby> _all = new();
        private static readonly object _lock = new();
        private static readonly ConcurrentDictionary<long, string> _uids = new();

        /// <summary>
        /// A character's own uid on the board (joa f5): the same in every announcement, as the
        /// capture's «6917336a-…» is in his own and in the one he joined.
        /// </summary>
        public static string UidOf(long characterId) => _uids.GetOrAdd(characterId, _ => Guid.NewGuid().ToString());

        public static void Clear()
        {
            lock (_lock) _all.Clear();
        }

        /// <summary>
        /// Something read off the announcements while nobody changes them: their lists are changed
        /// by every player's session, and writing one out while another session adds to it throws.
        /// </summary>
        public static T Read<T>(Func<T> read)
        {
            lock (_lock) return read();
        }

        /// <summary>The board: every announcement whose owner is on, the oldest first.</summary>
        public static List<Lobby> Board(Func<long, bool> online)
        {
            lock (_lock) return _all.Where(l => online(l.Owner.CharacterId)).OrderBy(l => l.Created).ToList();
        }

        public static Lobby ByUid(string uid)
        {
            lock (_lock) return _all.FirstOrDefault(l => l.Uid == uid);
        }

        public static Lobby OwnedBy(long characterId)
        {
            lock (_lock) return _all.FirstOrDefault(l => l.Owner.CharacterId == characterId);
        }

        /// <summary>The announcement a character is a member of, not as its owner.</summary>
        public static Lobby JoinedBy(long characterId)
        {
            lock (_lock)
                return _all.FirstOrDefault(l => l.Owner.CharacterId != characterId &&
                                                l.Members.Any(m => m.CharacterId == characterId));
        }

        /// <summary>The announcements a character has applied to and is waiting on.</summary>
        public static List<Lobby> AppliedBy(long characterId)
        {
            lock (_lock) return _all.Where(l => l.Applicants.Any(a => a.CharacterId == characterId)).ToList();
        }

        /// <summary>
        /// A player posts an announcement; he is its owner and first member. One at a time, and not
        /// while he is someone else's member.
        /// </summary>
        public static (int Code, Lobby Lobby) Create(Player owner, Spec spec)
        {
            if (spec.Type < FirstType || spec.Type > LastType) return (CreateCode.InvalidActivityType, null);
            if (spec.GroupSize < MinGroupSize || spec.GroupSize > MaxGroupSize) return (CreateCode.InvalidGroupSize, null);
            if ((spec.Description ?? "").Length > MaxDescription) return (CreateCode.InvalidDescription, null);
            if (spec.MinLevel < 0) return (CreateCode.InvalidLevel, null);
            lock (_lock)
            {
                if (_all.Any(l => l.Owner.CharacterId == owner.CharacterId)) return (CreateCode.AlreadyOwned, null);
                if (_all.Any(l => l.Members.Any(m => m.CharacterId == owner.CharacterId))) return (CreateCode.AlreadyMember, null);

                var lobby = new Lobby
                {
                    Type = spec.Type,
                    ActivityId = spec.ActivityId,
                    GroupSize = spec.GroupSize,
                    AutoJoin = spec.AutoJoin,
                    Tags = (spec.Tags ?? Array.Empty<int>()).ToList(),
                    Successes = (spec.Successes ?? Array.Empty<int>()).ToList(),
                    MinLevel = spec.MinLevel,
                    Description = spec.Description ?? "",
                    Owner = owner,
                };
                lobby.Members.Add(owner);
                // Posting takes back his applications elsewhere: he cannot wait on two sides.
                foreach (var other in _all) other.Applicants.RemoveAll(a => a.CharacterId == owner.CharacterId);
                _all.Add(lobby);
                return (CreateCode.Ok, lobby);
            }
        }

        /// <summary>The owner edits his announcement: its tags and its text (jov).</summary>
        public static (int Code, Lobby Lobby) Edit(long ownerId, IReadOnlyList<int> tags, string description)
        {
            if ((description ?? "").Length > MaxDescription) return (CreateCode.InvalidDescription, null);
            lock (_lock)
            {
                var lobby = _all.FirstOrDefault(l => l.Owner.CharacterId == ownerId);
                if (lobby == null) return (CreateCode.NotOwned, null);
                lobby.Tags = (tags ?? Array.Empty<int>()).ToList();
                lobby.Description = description ?? "";
                return (CreateCode.Ok, lobby);
            }
        }

        /// <summary>The owner takes his announcement down; whoever was in it or waiting on it is out.</summary>
        public static (int Code, Lobby Lobby) Delete(long ownerId, string uid)
        {
            lock (_lock)
            {
                var lobby = _all.FirstOrDefault(l => l.Uid == uid);
                if (lobby == null || lobby.Owner.CharacterId != ownerId) return (DeleteCode.NotAuthor, null);
                _all.Remove(lobby);
                return (DeleteCode.Ok, lobby);
            }
        }

        /// <summary>
        /// A player applies to an announcement: in at once when it takes anyone (AutoJoin), waiting
        /// for the owner's answer otherwise.
        /// </summary>
        public static (int Code, Lobby Lobby) Apply(Player player, string uid)
        {
            lock (_lock)
            {
                var lobby = _all.FirstOrDefault(l => l.Uid == uid);
                if (lobby == null) return (ApplyCode.InvalidAnnounce, null);
                long id = player.CharacterId;
                if (lobby.Owner.CharacterId == id) return (ApplyCode.AlreadyOwner, lobby);
                if (_all.Any(l => l.Owner.CharacterId == id)) return (ApplyCode.AlreadyOwner, lobby);
                if (_all.Any(l => l.Members.Any(m => m.CharacterId == id))) return (ApplyCode.AlreadyMember, lobby);
                if (lobby.Applicants.Any(a => a.CharacterId == id)) return (ApplyCode.AlreadyApplying, lobby);
                if (lobby.Rejected.Any(r => r.CharacterId == id)) return (ApplyCode.BlackListed, lobby);
                if (player.Level < lobby.MinLevel) return (ApplyCode.CriteriaForPlayer, lobby);
                if (lobby.IsFull) return (ApplyCode.LobbyFullMember, lobby);

                if (lobby.AutoJoin)
                {
                    foreach (var other in _all) other.Applicants.RemoveAll(a => a.CharacterId == id);
                    lobby.Members.Add(player);
                    return (ApplyCode.Joined, lobby);
                }
                if (lobby.Applicants.Count >= MaxApplicants) return (ApplyCode.LobbyFullApply, lobby);
                if (_all.Count(l => l.Applicants.Any(a => a.CharacterId == id)) >= MaxApplications)
                    return (ApplyCode.PlayerFullApply, lobby);
                lobby.Applicants.Add(player);
                return (ApplyCode.Applied, lobby);
            }
        }

        /// <summary>
        /// A player leaves an announcement he is a member of, or takes his application back (jny).
        /// What he was in it, for whom to tell.
        /// </summary>
        public static (int Code, Lobby Lobby, Player Was, bool WasMember) Leave(long characterId, string uid)
        {
            lock (_lock)
            {
                var lobby = _all.FirstOrDefault(l => l.Uid == uid);
                if (lobby == null) return (LeaveCode.InvalidAnnounce, null, null, false);
                if (lobby.Owner.CharacterId == characterId) return (LeaveCode.NotInLobby, lobby, null, false);
                var member = lobby.Members.FirstOrDefault(m => m.CharacterId == characterId);
                if (member != null)
                {
                    lobby.Members.Remove(member);
                    return (LeaveCode.Ok, lobby, member, true);
                }
                var applicant = lobby.Applicants.FirstOrDefault(a => a.CharacterId == characterId);
                if (applicant != null)
                {
                    lobby.Applicants.Remove(applicant);
                    return (LeaveCode.Ok, lobby, applicant, false);
                }
                return (LeaveCode.NotInLobby, lobby, null, false);
            }
        }

        /// <summary>
        /// The owner answers an applicant (jom): in as a member, or out to the rejected, who cannot
        /// apply again until the owner throws them away (<see cref="Free"/>).
        /// </summary>
        public static (int Code, Lobby Lobby, Player Applicant) Answer(long ownerId, string applicantUid, bool accept)
        {
            lock (_lock)
            {
                var lobby = _all.FirstOrDefault(l => l.Owner.CharacterId == ownerId);
                if (lobby == null) return (AnswerCode.NotOwned, null, null);
                var applicant = lobby.Applicants.FirstOrDefault(a => a.Uid == applicantUid);
                if (applicant == null) return (AnswerCode.NoApplier, lobby, null);
                if (accept && lobby.IsFull) return (AnswerCode.LobbyFull, lobby, applicant);

                lobby.Applicants.Remove(applicant);
                if (!accept)
                {
                    lobby.Rejected.Add(applicant);
                    return (AnswerCode.Rejected, lobby, applicant);
                }
                if (_all.Any(l => l.Owner.CharacterId == applicant.CharacterId || l.Members.Any(m => m.CharacterId == applicant.CharacterId)))
                    return (AnswerCode.NoApplier, lobby, applicant);
                foreach (var other in _all) other.Applicants.RemoveAll(a => a.CharacterId == applicant.CharacterId);
                var joined = applicant with { Since = DateTime.UtcNow };
                lobby.Members.Add(joined);
                return (AnswerCode.Accepted, lobby, joined);
            }
        }

        /// <summary>The owner throws rejected applicants away (joh): they may apply again.</summary>
        public static List<Player> Free(long ownerId, IEnumerable<string> uids)
        {
            var wanted = uids.ToHashSet();
            lock (_lock)
            {
                var lobby = _all.FirstOrDefault(l => l.Owner.CharacterId == ownerId);
                if (lobby == null) return new List<Player>();
                var freed = lobby.Rejected.Where(r => wanted.Contains(r.Uid)).ToList();
                lobby.Rejected.RemoveAll(r => wanted.Contains(r.Uid));
                return freed;
            }
        }
    }
}
