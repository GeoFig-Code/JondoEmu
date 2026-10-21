using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The estate agency: the agents' «Consultar las casas» (NPC action 9) lists the houses on sale
    /// -- put on sale by their owner, or nobody's -- in the client's UIEstateAgency, searched and
    /// paged.
    /// </summary>
    /// <remarks>
    /// Read off the client, not captured. The list is izy { f1: houses, f2: page, f3: pages }, the
    /// first page 1 (UIEstateAgency.FirstPageIndex); a house is izw { f1: owner, f2: chests,
    /// f3 { f1: x, f2: y }, f4: owned, f5: price, f6: house, f7: owner's tag, f8: rooms, f9: locked,
    /// f11: model, f13: skills, f14: subarea }, as the client's cd reads it (cd.mpq) and its
    /// estate window shows it. The search is jaj { f1: highest price, f2: fewest rooms, f3: skill,
    /// f4: area, f5: fewest chests, f6: order }, a page jam { f1: page }, and the window's X a kla.
    /// The order is the client's EstateSortEnum: price, area or name, up or down. INFERRED: ten to a
    /// page, and zero or less meaning «any» in the search. A building of many copies is listed once
    /// per copy its owner sells and once for its first free copy, as its street shows it: izw names
    /// no copy, so its rows differ by owner and price.
    /// </remarks>
    public static class EstateAgencyHandler
    {
        /// <summary>«Consultar las casas», the agents' action.</summary>
        public const int Action = 9;

        public const int PageSize = 10, FirstPage = 1;

        /// <summary>The client's EstateSortEnum.</summary>
        public enum Order { PriceUp = 0, PriceDown = 1, AreaUp = 2, AreaDown = 3, NameUp = 4, NameDown = 5 }

        /// <summary>What a search asks.</summary>
        public sealed record Filter(long MaxPrice, int Rooms, int Skill, int Area, int Chests, Order Order)
        {
            public static readonly Filter Any = new(0, 0, 0, 0, 0, Order.PriceUp);
        }

        /// <summary>A house on sale, as the agency lists it: one copy of a building.</summary>
        public sealed record Listing(int HouseId, int Instance, int Model, string Name, long Price, int Rooms, int Chests,
                                     int SubArea, int Area, int X, int Y, long OwnerAccount, bool Locked);

        private static Task SendAsync(NetworkStream? stream, string op, byte[] body)
            => stream == null ? Task.CompletedTask : Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, ConnectionProtocol.Push(op, body));

        /// <summary>Whether the agency's window is open in this session.</summary>
        public static bool IsOpen => SessionContext.State.EstateFilter != null;

        /// <summary>Every house on sale now: the first free copy of each building, and the copies their owners sell.</summary>
        public static List<Listing> OnSale()
        {
            var owned = HouseStore.AllOwned().ToLookup(h => (h.MapId, h.ElementId));
            var listings = new List<Listing>();
            foreach (var door in Houses.AllHouses())
            {
                if (!door.IsOwnable) continue;
                var copies = owned[(door.MapId, door.ElementId)].Where(h => door.HasCopy(h.Instance)).ToList();
                var shown = copies.Where(h => h.ForSale).ToList();
                for (int instance = Houses.FirstInstance; instance <= door.Copies; instance++)
                {
                    if (copies.Any(h => h.Instance == instance)) continue;
                    shown.Add(new HouseStore.House { MapId = door.MapId, ElementId = door.ElementId, Instance = instance });
                    break;
                }

                var street = MapManager.GetMapInfo(door.MapId);
                int subArea = street?.SubAreaId ?? 0;
                int chests = Houses.ChestCountOf(door.InteriorMapId);
                foreach (var house in shown.OrderBy(h => h.Instance))
                {
                    listings.Add(new Listing(Houses.HouseIdOf(door.MapId, door.ElementId), house.Instance, door.Model, door.Name,
                        HouseStore.AskingPrice(house, door.Price), door.Rooms, chests,
                        subArea, Interactives.AreaOfSubArea(subArea), street?.PosX ?? 0, street?.PosY ?? 0,
                        house.AccountId, house.Owned && house.Locked));
                }
            }
            return listings;
        }

        /// <summary>The houses a search wants, in its order. Zero or less in a field means any.</summary>
        public static List<Listing> Search(IEnumerable<Listing> houses, Filter filter)
        {
            var found = houses.Where(h => (filter.MaxPrice <= 0 || h.Price <= filter.MaxPrice)
                                          && h.Rooms >= filter.Rooms
                                          && h.Chests >= filter.Chests
                                          && (filter.Area <= 0 || h.Area == filter.Area)
                                          && filter.Skill <= 0);   // no house keeps a workshop
            return (filter.Order switch
            {
                Order.PriceDown => found.OrderByDescending(h => h.Price),
                Order.AreaUp => found.OrderBy(h => h.Area).ThenBy(h => h.Price),
                Order.AreaDown => found.OrderByDescending(h => h.Area).ThenBy(h => h.Price),
                Order.NameUp => found.OrderBy(h => h.Name, StringComparer.Ordinal).ThenBy(h => h.Price),
                Order.NameDown => found.OrderByDescending(h => h.Name, StringComparer.Ordinal).ThenBy(h => h.Price),
                _ => found.OrderBy(h => h.Price),
            }).ThenBy(h => h.HouseId).ThenBy(h => h.Instance).ToList();
        }

        /// <summary>One page of a result, from 1, and how many pages there are -- one, at least.</summary>
        public static (List<Listing> Page, int Pages) PageOf(IReadOnlyList<Listing> found, int page)
        {
            int pages = Math.Max(1, (found.Count + PageSize - 1) / PageSize);
            page = Math.Clamp(page, FirstPage, pages);
            return (found.Skip((page - FirstPage) * PageSize).Take(PageSize).ToList(), pages);
        }

        /// <summary>An agent's «Consultar las casas»: every house on sale, its first page.</summary>
        public static Task OpenAsync(NetworkStream? stream)
        {
            SessionContext.State.EstateFilter = Filter.Any;
            return SendPageAsync(stream, FirstPage);
        }

        /// <summary>jaj: a search, its first page.</summary>
        public static async Task<bool> SearchAsync(NetworkStream? stream, byte[] payload)
        {
            byte[]? jaj = ConnectionProtocol.ReadPayload(payload, Op.Jaj);
            if (jaj == null || !IsOpen) return false;
            SessionContext.State.EstateFilter = new Filter(Var(jaj, 1), (int)Var(jaj, 2), (int)Var(jaj, 3),
                                                           (int)Var(jaj, 4), (int)Var(jaj, 5), (Order)(int)Var(jaj, 6));
            await SendPageAsync(stream, FirstPage);
            return true;
        }

        /// <summary>jam: another page of the last search.</summary>
        public static async Task<bool> PageAsync(NetworkStream? stream, byte[] payload)
        {
            byte[]? jam = ConnectionProtocol.ReadPayload(payload, Op.Jam);
            if (jam == null || !IsOpen) return false;
            await SendPageAsync(stream, (int)Var(jam, 1));
            return true;
        }

        /// <summary>The window's X (kla): the empty answer of a conversation's. False when it is not open.</summary>
        public static async Task<bool> CloseAsync(NetworkStream? stream)
        {
            if (!IsOpen) return false;
            SessionContext.State.EstateFilter = null;
            await SendAsync(stream, Op.Kld, ConnectionProtocol.BuildDialogClosed(ConnectionProtocol.NpcDialogCloseReason));
            return true;
        }

        private static Task SendPageAsync(NetworkStream? stream, int page)
        {
            var found = Search(OnSale(), SessionContext.State.EstateFilter ?? Filter.Any);
            var (shown, pages) = PageOf(found, page);
            var owners = new Dictionary<long, (string Name, string Tag)>();
            foreach (var house in shown.Where(h => h.OwnerAccount > 0))
                owners[house.OwnerAccount] = HouseHandler.AccountTagOf(house.OwnerAccount);
            Console.WriteLine($"[Estate] {SessionContext.State.CharacterName}: {found.Count} house(s) on sale, page {Math.Clamp(page, FirstPage, pages)} of {pages}.");
            return SendAsync(stream, Op.Izy, HouseProtocol.BuildEstateList(shown, Math.Clamp(page, FirstPage, pages), pages, owners));
        }

        private static long Var(byte[] body, int number)
        {
            foreach (var field in ProtoMessage.Parse(body).Fields)
            {
                if (field.FieldNumber == number && field.WireType == 0) return field.VarIntValue;
            }
            return 0;
        }
    }
}
