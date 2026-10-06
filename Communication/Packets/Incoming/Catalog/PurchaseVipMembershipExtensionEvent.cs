using Plus.HabboHotel.Catalog;
namespace Plus.Communication.Packets.Incoming.Catalog;

internal class PurchaseVipMembershipExtensionEvent(IClubCatalogService clubCatalog) : PurchaseMembershipExtensionEvent(clubCatalog);
