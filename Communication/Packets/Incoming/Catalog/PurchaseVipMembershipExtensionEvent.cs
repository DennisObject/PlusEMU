using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class PurchaseVipMembershipExtensionEvent(ICatalogManager catalog, IClubMembershipService memberships) : PurchaseMembershipExtensionEvent(catalog, memberships);
