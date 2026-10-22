using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// What the client's own data says about alliances (datos/alliances_*.json, extract_alliances.py):
    /// the four ranks an alliance is born with, the eighteen rights, and the tax collectors' names.
    /// </summary>
    /// <remarks>
    /// The rights are the client's ids, the ones a rank carries in its list (ldt.f3.f3): 1 manage the
    /// ranks below one's own, 2 give ranks below one's own, 3 the alliance message, 4 place a prism,
    /// 6 and 7 the nuggets, 8 to 12 and 17 to 19 the tax collectors, 13 the alliance's information,
    /// 15 applications and invitations, 16 expel a member, 21 the recruitment.
    ///
    /// And the emblem symbols' skins: a tax collector wears its alliance's symbol as a skin, 1647
    /// for the symbol 104 of «Alliance2» and 1762 for the 136 of «Alliance-3» in the captures.
    /// </remarks>
    public static class AllianceData
    {
        public sealed record RankTemplate(int Id, string Name, int Order, bool Modifiable, int Icon);
        public sealed record Right(int Id, int GroupId, int Order, string Name);
        public sealed record NamePart(int Id, string Name);
        public sealed record EmblemSymbol(int Id, int SkinId, bool Colorizable, int PrismSkinId = 0);
        public sealed record KothRole(int Id, bool Default, string Name, int Certificate = 0);

        private sealed record Document(List<RankTemplate> Ranks, List<Right> Rights,
                                       List<NamePart> TaxCollectorFirstNames, List<NamePart> TaxCollectorNames,
                                       List<EmblemSymbol> EmblemSymbols, List<KothRole> KothRoles = null);

        /// <summary>Manage the ranks and rights below one's own.</summary>
        public const int ManageRanks = 1;

        /// <summary>Give a member a rank below one's own.</summary>
        public const int AssignRanks = 2;

        /// <summary>Modify the alliance message.</summary>
        public const int EditMessage = 3;

        /// <summary>Modify the alliance's information (name, tag, emblem).</summary>
        public const int EditInformation = 13;

        /// <summary>Manage applications and invitations.</summary>
        public const int ManageApplications = 15;

        /// <summary>Expel a member.</summary>
        public const int Expel = 16;

        /// <summary>Manage the recruitment.</summary>
        public const int ManageRecruitment = 21;

        /// <summary>Place or modify an alliance prism: the prisms' criterion Ox=4.</summary>
        public const int PlacePrism = 4;

        /// <summary>Place a tax collector: the potions' criterion Ox=10.</summary>
        public const int PlaceTaxCollector = 10;

        /// <summary>Collect one's own tax collectors.</summary>
        public const int CollectOwnTaxCollectors = 18;

        /// <summary>Collect the tax collectors of the other members.</summary>
        public const int CollectMembersTaxCollectors = 19;

        /// <summary>«Ver el saldo de pepitas»: the alliance's nugget pot.</summary>
        public const int SeeNuggets = 6;

        /// <summary>«Distribuir pepitas»: handing the pot out to the members.</summary>
        public const int DistributeNuggets = 7;

        private static Document _document;
        private static readonly object _lock = new();

        private static Document Loaded
        {
            get
            {
                if (_document != null) return _document;
                lock (_lock)
                {
                    _document ??= Load(Paths.AlliancesJson);
                    return _document;
                }
            }
        }

        private static Document Load(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Alliance] {path} not found: alliances get the four ranks without names.");
                return new Document(new(), new(), new(), new(), new(), new());
            }
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), options);
            return new Document(document?.Ranks ?? new(), document?.Rights ?? new(),
                                document?.TaxCollectorFirstNames ?? new(), document?.TaxCollectorNames ?? new(),
                                document?.EmblemSymbols ?? new(), document?.KothRoles ?? new());
        }

        /// <summary>The four ranks an alliance is born with, leader first.</summary>
        public static IReadOnlyList<RankTemplate> Ranks
            => Loaded.Ranks.Count > 0
                ? Loaded.Ranks.OrderBy(r => r.Order).ToList()
                : new List<RankTemplate>
                  {
                      new(1, "", 0, false, 116), new(2, "", 1, true, 115),
                      new(3, "", 2, true, 114), new(4, "", 3, false, 117),
                  };

        /// <summary>Every right a rank can hold, as the client numbers them.</summary>
        public static IReadOnlyList<Right> Rights => Loaded.Rights;

        public static IReadOnlyList<NamePart> TaxCollectorFirstNames => Loaded.TaxCollectorFirstNames;
        public static IReadOnlyList<NamePart> TaxCollectorNames => Loaded.TaxCollectorNames;

        /// <summary>
        /// The role a member takes in a conquest when he learnt none: «carne de cañón», the
        /// kothRoles' default; zero without the data.
        /// </summary>
        public static int DefaultKothRole => Loaded.KothRoles?.FirstOrDefault(r => r.Default)?.Id ?? 0;

        /// <summary>
        /// The conquests' roles, each with its certificate, «Certificado de &lt;role&gt;», the item
        /// carrying which is holding the role (see <see cref="KothRoles"/>).
        /// </summary>
        public static IReadOnlyList<KothRole> KothRoles => Loaded.KothRoles ?? new List<KothRole>();

        /// <summary>The skin an emblem symbol dresses a tax collector with; zero when it is not known.</summary>
        public static int SkinOfSymbol(int symbol)
            => Loaded.EmblemSymbols.FirstOrDefault(e => e.Id == symbol)?.SkinId ?? 0;

        /// <summary>
        /// The skin an emblem symbol dresses a prism with, another set than the tax collector's;
        /// zero when it is not known. Matched by texture, see tools/match_prism_skins.py.
        /// </summary>
        public static int PrismSkinOfSymbol(int symbol)
            => Loaded.EmblemSymbols.FirstOrDefault(e => e.Id == symbol)?.PrismSkinId ?? 0;
    }
}
