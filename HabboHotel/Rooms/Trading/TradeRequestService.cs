using Plus.Communication.Packets.Outgoing.Inventory.Trading;
using Plus.Communication.Packets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Trading;

public interface ITradeRequestService
{
    void Start(GameClient session, int virtualUserId);
}

public sealed class TradeRequestService(ITradingLockService tradingLocks) : ITradeRequestService
{
    public void Start(GameClient session, int userId)
    {
        if (!session.GetHabbo().InRoom)
            return;
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return;

        var roomUser = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (roomUser == null)
            return;
        var targetUser = room.GetRoomUserManager().GetRoomUserByVirtualId(userId);
        if (targetUser == null)
            return;
        var hadLock = session.GetHabbo().TradingLockExpiresAt != null;
        if (tradingLocks.IsLocked(session.GetHabbo()))
        {
            session.SendNotification("You're currently banned from trading.");
            return;
        }
        if (hadLock) session.SendNotification("Your trading ban has now expired.");
        if (!session.GetHabbo().Access.Can(PermissionKeys.RoomTradeOverride))
        {
            if (room.TradeSettings == 0)
            {
                session.Send(new TradingErrorComposer(TradingError.RoomDisallowsTrading, targetUser.GetUsername()));
                return;
            }
            if (room.TradeSettings == 1 && room.OwnerId != session.GetHabbo().Id)
            {
                session.Send(new TradingErrorComposer(TradingError.RoomDisallowsTrading, targetUser.GetUsername()));
                return;
            }
        }
        if (roomUser.IsTrading && roomUser.TradePartner != targetUser.UserId)
        {
            session.Send(new TradingErrorComposer(TradingError.AlreadyTrading, targetUser.GetUsername()));
            return;
        }
        if (targetUser.IsTrading && targetUser.TradePartner != roomUser.UserId)
        {
            session.Send(new TradingErrorComposer(TradingError.PartnerAlreadyTrading, targetUser.GetUsername()));
            return;
        }
        var target = targetUser.GetClient()?.GetHabbo();
        if (target == null || !target.AllowTradingRequests)
        {
            session.Send(new TradingErrorComposer(TradingError.PartnerUnavailable, targetUser.GetUsername()));
            return;
        }
        if (tradingLocks.IsLocked(target))
        {
            session.Send(new TradingErrorComposer(TradingError.PartnerUnavailable, targetUser.GetUsername()));
            return;
        }
        if (!room.GetTrading().StartTrade(roomUser, targetUser, out var trade))
        {
            session.SendNotification("An error occured trying to start this trade");
            return;
        }
        if (targetUser.HasStatus("trd"))
            targetUser.RemoveStatus("trd");
        if (roomUser.HasStatus("trd"))
            roomUser.RemoveStatus("trd");
        targetUser.SetStatus("trd");
        targetUser.UpdateNeeded = true;
        roomUser.SetStatus("trd");
        roomUser.UpdateNeeded = true;
        trade.SendPacket(new TradingStartComposer(roomUser.UserId, targetUser.UserId));
    }
}
