using Plus.HabboHotel.Catalog;
namespace Plus.Communication.Packets.Incoming.Catalog;

internal class PurchaseBasicMembershipExtensionEvent(IClubCatalogService clubCatalog) : PurchaseMembershipExtensionEvent(clubCatalog);
