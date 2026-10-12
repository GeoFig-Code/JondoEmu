using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    /// <summary>
    /// The alliance frames. Their shapes are read off the client's message classes (clientscan
    /// proto) and checked against the one capture that has an alliance, "entrar en alianza y salir
    /// de alianza": the directory (mcv), joining «A B S I N T O» (mbm, mbz, mcx, mca, mcm, mcr)
    /// and leaving it (khj, lqn 410, mbt).
    /// </summary>
    public static class AllianceProtocol
    {
        /// <summary>The alliance banner emote, given on joining (khi) and taken on leaving (khj).</summary>
        public const int BannerEmote = 98;

        /// <summary>
        /// The second field of a member's line (lmr.f2) and of his details (lon.f5): -1 for every
        /// one of the 46 members of «A B S I N T O». Not understood; sent as measured.
        /// </summary>
        private const int MeasuredMinusOne = -1;

        /// <summary>
        /// The creation's results (lbv), as the client's handler of the mch (fgv.bevj, read in
        /// Ghidra) words them: 1 invalidName, 2 alreadyInAlliance, 3 alreadyUsedName, 6
        /// requirementUnmet, 8 invalidTag, 9 alreadyUsedTag, 10 unknownFail. 0, success, travels as
        /// an empty mch; 4, 5 and 7 say nothing.
        /// </summary>
        public const int CreationOk = 0;
        public const int CreationInvalidName = 1;
        public const int CreationAlreadyInAlliance = 2;
        public const int CreationNameUsed = 3;
        public const int CreationRequirementUnmet = 6;
        public const int CreationInvalidTag = 8;
        public const int CreationTagUsed = 9;

        /// <summary>
        /// Why a notice could not be set (mdf, mco), as fgv words it: 1 «no tienes los derechos»
        /// (ui.alliance.taxCollectorNoRights, the same text), 2 «espera antes de volver a cambiarlo».
        /// </summary>
        public const int NoticeNoRights = 1;
        public const int NoticeTooSoon = 2;

        /// <summary>How the applications list moves (mbb.f2, lbt): one more, one gone, one rewritten.</summary>
        public const int ApplicationAdded = 0;
        public const int ApplicationGone = 1;
        public const int ApplicationRewritten = 2;

        /// <summary>The emblem (lld): symbol, symbol colour, background, background shape, symbol RGB.</summary>
        internal static Pb Emblem(AllianceStore.Alliance alliance)
            => Pb.New()
                .VarIfNotZero(1, alliance.EmblemSymbol)
                .VarIfNotZero(2, alliance.EmblemSymbolColor)
                .VarIfNotZero(3, alliance.EmblemBackground)
                .VarIfNotZero(4, alliance.EmblemBackgroundShape)
                .VarIfNotZero(5, alliance.EmblemSymbolRgb);

        /// <summary>
        /// The alliance block (len): f2 the emblem, f3 the name, f4 the uuid, f5 the tag, and f6
        /// the card when the frame shows one. Byte for byte the shape of «A B S I N T O»'s.
        /// </summary>
        public static Pb Block(AllianceStore.Alliance alliance, Pb card = null)
        {
            var block = Pb.New()
                .Msg(2, Emblem(alliance))
                .Str(3, alliance.Name)
                .Str(4, alliance.Uuid)
                .Str(5, alliance.Tag);
            if (card != null) block.Msg(6, card);
            return block;
        }

        /// <summary>
        /// The card (lel) the directory and the alliance's sheet show: f1 the recruitment card,
        /// f2 when it was founded in seconds, f4 how many prisms it has and f5 how many members.
        /// </summary>
        /// <remarks>
        /// From the directory: «A B S I N T O» {f2 1774821557, f5 46}, «RIGHT» {f2 1785200130, f4 3,
        /// f5 27}. The f4 only shows on the alliances that hold territory, so it is read as their
        /// prisms; f3 shows on none.
        /// </remarks>
        public static Pb Card(AllianceStore.Alliance alliance, AllianceStore.Profile profile, string leaderName,
                              int members, int prisms = 0)
            => Pb.New()
                .Msg(1, Recruitment(alliance, profile, leaderName))
                .Var(2, alliance.CreatedUtcS)
                .VarIfNotZero(4, prisms)
                .Var(5, members);

        /// <summary>
        /// The recruitment card (lmn): f1 the leader's name, f2 the recruitment (1 open), f4 the
        /// minimum level, f5 the description, f6 the tags, f9 the title, f10 when it was written,
        /// f11 always 1, f12 the uuid again and f13 the languages.
        /// </summary>
        internal static Pb Recruitment(AllianceStore.Alliance alliance, AllianceStore.Profile profile, string leaderName)
            => Pb.New()
                .StrIfNotEmpty(1, leaderName)
                .VarIfNotZero(2, profile.Recruitment)
                .VarIfNotZero(4, profile.MinLevel)
                .StrIfNotEmpty(5, profile.Description)
                .PackedIfAny(6, profile.Tags)
                .StrIfNotEmpty(9, profile.Title)
                .VarIfNotZero(10, profile.WhenMs)
                .Var(11, 1)
                .Str(12, alliance.Uuid)
                .PackedIfAny(13, profile.Languages);

        /// <summary>The temple's founding editor opens (mcw): an empty message.</summary>
        public static byte[] BuildCreationStarted() => Array.Empty<byte>();

        /// <summary>The creation's result (mch): f1 the lbv, empty for success.</summary>
        public static byte[] BuildCreationResult(int result) => Pb.New().VarIfNotZero(1, result).Build();

        /// <summary>
        /// The editor opened to modify (mce): f1 the emblem, f2 the name and tag. fvl hands them to
        /// OpenSocialGroupCreator as (f2, f1): name alone is its mode 1, emblem alone its mode 2.
        /// </summary>
        public static byte[] BuildModificationStarted(bool name, bool emblem)
            => Pb.New().VarIfNotZero(1, emblem ? 1 : 0).VarIfNotZero(2, name ? 1 : 0).Build();

        /// <summary>The modification's result (mcn): f1 the lbv, empty for success.</summary>
        public static byte[] BuildModificationResult(int result) => Pb.New().VarIfNotZero(1, result).Build();

        /// <summary>
        /// Belonging to an alliance, said silently (mbz): f2 the rank, f3 the alliance. What world
        /// entry sends; the mca is joining, and its handler writes "you have joined" in the chat.
        /// </summary>
        public static byte[] BuildMembership(AllianceStore.Alliance alliance, int rank)
            => Pb.New().Var(2, rank).Msg(3, Block(alliance)).Build();

        /// <summary>Joining an alliance (mca): f1 the rank, f2 the alliance. {4, «A B S I N T O»} in the capture.</summary>
        public static byte[] BuildJoined(AllianceStore.Alliance alliance, int rank)
            => Pb.New().Var(1, rank).Msg(2, Block(alliance)).Build();

        /// <summary>
        /// The alliance's ranks (mbm): one f1 each, { f2 name, f3 { f1 modifiable, f3 rights },
        /// f4 { f2 icon, f3 order }, f5 id }. The four of «A B S I N T O» have that shape.
        /// </summary>
        public static byte[] BuildRanks(IReadOnlyList<AllianceStore.Rank> ranks)
        {
            var mbm = Pb.New();
            foreach (var rank in ranks)
            {
                mbm.Msg(1, Pb.New()
                    .Str(2, rank.Name)
                    .Msg(3, Pb.New().VarIfNotZero(1, rank.Flag ? 1 : 0).PackedIfAny(3, rank.Rights))
                    .Msg(4, Pb.New().Var(2, rank.Icon).VarIfNotZero(3, rank.Order))
                    .Var(5, rank.Id));
            }
            return mbm.Build();
        }

        /// <summary>A member's line, with what the entry needs besides the membership.</summary>
        public readonly record struct MemberRow(AllianceStore.Member Member, string Name, int Level, long AccountId,
                                                int Breed, int Sex, int AchievementPoints, bool Online);

        /// <summary>
        /// The alliance's sheet (mcx): f1 its tax collectors, f2 its prisms, f3 the alliance with its
        /// card and f4 one line per member. The capture's is 3,471 bytes: «A B S I N T O» and its 46.
        /// </summary>
        public static byte[] BuildInformation(AllianceStore.Alliance alliance, Pb card, IEnumerable<MemberRow> members,
                                              IEnumerable<Pb> taxCollectors = null, IEnumerable<Pb> prisms = null)
        {
            var mcx = Pb.New();
            foreach (var taxCollector in taxCollectors ?? Enumerable.Empty<Pb>()) mcx.Msg(1, taxCollector);
            foreach (var prism in prisms ?? Enumerable.Empty<Pb>()) mcx.Msg(2, prism);
            mcx.Msg(3, Block(alliance, card));
            foreach (var row in members) mcx.Msg(4, MemberLine(row));
            return mcx.Build();
        }

        /// <summary>
        /// A member's line (lmr): f2 -1 and f3 the member { f1 { f2 name, f3 level, f5 details },
        /// f2 the character }.
        /// </summary>
        /// <remarks>
        /// The details (lon) as the client reads them: f1 the achievement points (1893, 759, 116...),
        /// f2 the rank, f4 when he joined in milliseconds, f5 -1, f6 the class, f8 the status
        /// ({f1 1} connected), f9 the sex and f10 the account.
        /// </remarks>
        internal static Pb MemberLine(MemberRow row)
        {
            var details = Pb.New()
                .VarIfNotZero(1, row.AchievementPoints)
                .Var(2, row.Member.Rank)
                .Var(4, row.Member.JoinedUtcMs)
                .Var(5, MeasuredMinusOne)
                .Var(6, row.Breed)
                .Msg(8, row.Online ? Pb.New().Var(1, 1) : Pb.New())
                .VarIfNotZero(9, row.Sex)
                .Var(10, row.AccountId);

            return Pb.New()
                .Var(2, MeasuredMinusOne)
                .Msg(3, Pb.New()
                    .Msg(1, Pb.New().Str(2, row.Name).Var(3, row.Level).Msg(5, details))
                    .Var(2, row.Member.CharacterId));
        }

        /// <summary>
        /// An alliance's card seen from the directory (mcl), the answer to the mck: f1 the members it
        /// shows, f5 the alliance with its card -- the fields fgv's handler reads (bexk, in Ghidra);
        /// its f2 and f4 are left out, their meaning not read. It shows the leader.
        /// </summary>
        public static byte[] BuildCardView(AllianceStore.Alliance alliance, Pb card, IEnumerable<MemberRow> shown)
        {
            var mcl = Pb.New();
            foreach (var row in shown)
            {
                var line = ProtoMessage.Parse(MemberLine(row).Build());
                mcl.Bytes(1, line.Fields.Single(f => f.FieldNumber == 3).BytesValue);
            }
            return mcl.Msg(5, Block(alliance, card)).Build();
        }

        /// <summary>A directory alliance, with its card already built.</summary>
        public sealed record DirectoryEntry(AllianceStore.Alliance Alliance, Pb Card);

        /// <summary>
        /// The directory (mcv): one f2 per alliance with its card, f4 how many there are in all and
        /// f5 how many the page holds. The capture's: 40 alliances of 1,999, f5 40.
        /// </summary>
        public static byte[] BuildDirectory(IReadOnlyList<DirectoryEntry> page, long total, long pageSize)
        {
            var mcv = Pb.New();
            foreach (var entry in page) mcv.Msg(2, Block(entry.Alliance, entry.Card));
            return mcv.VarIfNotZero(4, total).VarIfNotZero(5, pageSize).Build();
        }

        /// <summary>
        /// The alliance message (mcm) and the bulletin (mcr), each a leg { f1 text, f2 when in
        /// seconds, f3 author, f4 author's id } -- the four fields fgv copies into the alliance
        /// service. On joining «A B S I N T O» both came with only the date, which was the
        /// alliance's founding.
        /// </summary>
        public static byte[] BuildNotice(AllianceStore.Notice notice)
            => Pb.New().Msg(1, Pb.New()
                    .StrIfNotEmpty(1, notice.Text)
                    .VarIfNotZero(2, notice.WhenS)
                    .StrIfNotEmpty(3, notice.AuthorName)
                    .VarIfNotZero(4, notice.AuthorId))
                .Build();

        /// <summary>Why a notice was not set (mdf, mco): f1 the reason.</summary>
        public static byte[] BuildNoticeRefused(int reason) => Pb.New().Var(1, reason).Build();

        /// <summary>The banner emote, given or taken (khi / khj): f1 the emote.</summary>
        public static byte[] BuildEmote() => Pb.New().Var(1, BannerEmote).Build();

        // ─── Invitations ─────────────────────────────────────────────────────────

        /// <summary>
        /// How an invitation goes (lbx), as fvl reads it: 1 sent -- the inviter waits, with a cancel
        /// button --, 2 refused or cancelled, 3 accepted; 2 and 3 close both popups.
        /// </summary>
        public const int InvitationSent = 1;
        public const int InvitationRefused = 2;
        public const int InvitationAccepted = 3;

        /// <summary>«X te invita a unirte a su alianza» (mbs): f1 who invites, f2 the alliance.</summary>
        public static byte[] BuildInvited(string inviterName, AllianceStore.Alliance alliance)
            => Pb.New().Str(1, inviterName).Msg(2, Block(alliance)).Build();

        /// <summary>The invitation, for whoever invites (mbq): f1 the invited, f2 who invites, f3 how it goes.</summary>
        public static byte[] BuildInvitationForInviter(string invitedName, string inviterName, int state)
            => Pb.New().Str(1, invitedName).Str(2, inviterName).Var(3, state).Build();

        /// <summary>The invitation, for whoever is invited (mcb): f1 how it goes.</summary>
        public static byte[] BuildInvitationForInvited(int state) => Pb.New().Var(1, state).Build();

        // ─── Members, seen by the others ─────────────────────────────────────────

        /// <summary>A member's line, new or changed (mbv): f1 the lmr. fgv adds it or replaces it by id.</summary>
        public static byte[] BuildMemberUpdate(MemberRow row) => Pb.New().Msg(1, MemberLine(row)).Build();

        /// <summary>A member gone (mby): f1 his character, which fgv takes out of the list.</summary>
        public static byte[] BuildMemberGone(long characterId) => Pb.New().Var(1, characterId).Build();

        // ─── Applications and the recruitment card ──────────────────────────────

        /// <summary>An application with the applicant's sheet.</summary>
        public readonly record struct ApplicationRow(AllianceStore.Application Application, string Name, int Level,
                                                     long AccountId, int Breed, int Sex, bool Online);

        /// <summary>
        /// An application (lpo): f1 when, f3 the message, f4 the applicant (lgm) { f1 class, f3 sex,
        /// f4 account, f5 name, f6 account tag, f7 character, f8 account nickname, f9 status,
        /// f10 level } -- the guild's measured application has that shape, and ih/ig read it so.
        /// </summary>
        internal static Pb ApplicationBlock(ApplicationRow row)
            => Pb.New()
                .Var(1, row.Application.WhenMs)
                .StrIfNotEmpty(3, row.Application.Message)
                .Msg(4, Pb.New()
                    .Var(1, row.Breed)
                    .VarIfNotZero(3, row.Sex)
                    .Var(4, row.AccountId)
                    .Str(5, row.Name)
                    .Var(7, row.Application.CharacterId)
                    .Str(8, row.Name)
                    .Msg(9, row.Online ? Pb.New().Var(1, 1) : Pb.New())
                    .Var(10, row.Level));

        /// <summary>One's own pending application (lzx): f1 the application, f2 the alliance.</summary>
        public static byte[] BuildOwnApplication(ApplicationRow row, AllianceStore.Alliance alliance)
            => Pb.New().Msg(1, ApplicationBlock(row)).Msg(2, Block(alliance)).Build();

        /// <summary>«Candidatura recibida» (mad): f1 the applicant, f2 his name.</summary>
        public static byte[] BuildApplicationArrived(long characterId, string name)
            => Pb.New().Var(1, characterId).Str(2, name).Build();

        /// <summary>One's application answered (maq): f1 the alliance, f3 whether it was accepted.</summary>
        public static byte[] BuildApplicationAnswered(AllianceStore.Alliance alliance, bool accepted)
            => Pb.New().Msg(1, Block(alliance)).VarIfNotZero(3, accepted ? 1 : 0).Build();

        /// <summary>One's application deleted (mav): f1 whether it was; false says «espera un poco».</summary>
        public static byte[] BuildApplicationDeleted(bool deleted) => Pb.New().VarIfNotZero(1, deleted ? 1 : 0).Build();

        /// <summary>
        /// A page of applications (maz): f1 how many are pending in all -- the window's counter --,
        /// f2 the page, f3 how many were asked for and f4 after which date.
        /// </summary>
        public static byte[] BuildApplications(int pending, IEnumerable<ApplicationRow> page, long asked, long after)
        {
            var maz = Pb.New().VarIfNotZero(1, pending);
            foreach (var row in page) maz.Msg(2, ApplicationBlock(row));
            return maz.VarIfNotZero(3, asked).VarIfNotZero(4, after).Build();
        }

        /// <summary>The list moves (mbb): f2 how, f3 the application, f4 who handled it.</summary>
        public static byte[] BuildApplicationsMoved(int how, ApplicationRow row, long handledBy)
            => Pb.New().VarIfNotZero(2, how).Msg(3, ApplicationBlock(row)).VarIfNotZero(4, handledBy).Build();

        /// <summary>The recruitment card (mar): f1 the lmn.</summary>
        public static byte[] BuildRecruitmentCard(AllianceStore.Alliance alliance, AllianceStore.Profile profile,
                                                  string leaderName)
            => Pb.New().Msg(1, Recruitment(alliance, profile, leaderName)).Build();

        /// <summary>
        /// The recruitment card the window sends (lmn) read into a profile: f2 the recruitment, f4 the
        /// minimum level, f5 the description, f6 the tags, f9 the title, f13 the languages.
        /// </summary>
        public static void ReadRecruitment(byte[] lmn, AllianceStore.Profile into)
        {
            into.Tags = new List<int>();
            into.Languages = new List<int>();
            into.Description = "";
            into.Title = "";
            foreach (var field in ProtoMessage.Parse(lmn).Fields)
            {
                switch (field.FieldNumber, field.WireType)
                {
                    case (2, 0): into.Recruitment = (int)field.VarIntValue; break;
                    case (4, 0): into.MinLevel = (int)field.VarIntValue; break;
                    case (5, 2): into.Description = System.Text.Encoding.UTF8.GetString(field.BytesValue); break;
                    case (9, 2): into.Title = System.Text.Encoding.UTF8.GetString(field.BytesValue); break;
                    case (6, 2): into.Tags.AddRange(Packed(field.BytesValue)); break;
                    case (6, 0): into.Tags.Add((int)field.VarIntValue); break;
                    case (13, 2): into.Languages.AddRange(Packed(field.BytesValue)); break;
                    case (13, 0): into.Languages.Add((int)field.VarIntValue); break;
                }
            }
        }

        /// <summary>A rank as the window sends it.</summary>
        public sealed record RankSent(int Id, string Name, int Icon, int Order, bool Modifiable, List<int> Rights);

        /// <summary>
        /// A rank as the window sends it (ldt), the fields ch.mpy fills: f2 the name, f3 { f1
        /// modifiable, f3 rights }, f4 { f2 icon, f3 order }, f5 the id. Its f1, f3.f2 and f4.f1 are
        /// the obfuscator's decoys, never written.
        /// </summary>
        public static RankSent ReadRank(byte[] ldt)
        {
            int id = 0, icon = 0, order = 0;
            bool modifiable = false;
            string name = "";
            var rights = new List<int>();
            foreach (var field in ProtoMessage.Parse(ldt).Fields)
            {
                if (field.FieldNumber == 5 && field.WireType == 0) id = (int)field.VarIntValue;
                else if (field.FieldNumber == 2 && field.WireType == 2) name = System.Text.Encoding.UTF8.GetString(field.BytesValue);
                else if (field.FieldNumber == 3 && field.WireType == 2)
                {
                    foreach (var inner in ProtoMessage.Parse(field.BytesValue).Fields)
                    {
                        if (inner.FieldNumber == 1 && inner.WireType == 0) modifiable = inner.VarIntValue != 0;
                        else if (inner.FieldNumber == 3 && inner.WireType == 2) rights.AddRange(Packed(inner.BytesValue));
                        else if (inner.FieldNumber == 3 && inner.WireType == 0) rights.Add((int)inner.VarIntValue);
                    }
                }
                else if (field.FieldNumber == 4 && field.WireType == 2)
                {
                    foreach (var inner in ProtoMessage.Parse(field.BytesValue).Fields)
                    {
                        if (inner.FieldNumber == 2 && inner.WireType == 0) icon = (int)inner.VarIntValue;
                        else if (inner.FieldNumber == 3 && inner.WireType == 0) order = (int)inner.VarIntValue;
                    }
                }
            }
            return new RankSent(id, name, icon, order, modifiable, rights);
        }

        private static IEnumerable<int> Packed(byte[] bytes)
        {
            int i = 0;
            while (i < bytes.Length)
            {
                ulong value = 0;
                int shift = 0;
                while (i < bytes.Length)
                {
                    byte b = bytes[i++];
                    value |= (ulong)(b & 0x7F) << shift;
                    if ((b & 0x80) == 0) break;
                    shift += 7;
                }
                yield return (int)value;
            }
        }

        /// <summary>A packed list, left out when empty as proto3 does: a rank without rights has no f3.</summary>
        private static Pb PackedIfAny(this Pb pb, int field, IReadOnlyCollection<int> values)
            => values == null || values.Count == 0 ? pb : pb.Packed(field, values.Select(v => (long)v));
    }
}
