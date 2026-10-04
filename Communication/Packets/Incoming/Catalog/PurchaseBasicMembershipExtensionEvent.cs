using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class PurchaseBasicMembershipExtensionEvent(ICatalogManager catalog, IClubMembershipService memberships) : PurchaseMembershipExtensionEvent(catalog, memberships);
