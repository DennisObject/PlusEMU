using Dapper;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;

using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Interactor;

internal class InteractorMannequin : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item) { }

    public void OnRemove(GameClient? session, Item item) { }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        if (item.LegacyDataString.Contains(Convert.ToChar(5).ToString()))
        {
            var stuff = item.LegacyDataString.Split(Convert.ToChar(5));
            session.GetHabbo().Gender = stuff[0].ToUpper();
            var newFig = new Dictionary<string, string>();
            newFig.Clear();
            foreach (var man in stuff[1].Split('.'))
            {
                foreach (var fig in session.GetHabbo().Look.Split('.'))
                {
                    if (fig.Split('-')[0] == man.Split('-')[0])
                    {
                        if (newFig.ContainsKey(fig.Split('-')[0]) && !newFig.ContainsValue(man))
                        {
                            newFig.Remove(fig.Split('-')[0]);
                            newFig.Add(fig.Split('-')[0], man);
                        }
                        else if (!newFig.ContainsKey(fig.Split('-')[0]) && !newFig.ContainsValue(man)) newFig.Add(fig.Split('-')[0], man);
                    }
                    else
                    {
                        if (!newFig.ContainsKey(fig.Split('-')[0])) newFig.Add(fig.Split('-')[0], fig);
                    }
                }
            }
            var final = "";
            foreach (var str in newFig.Values) final += $"{str}.";
            session.GetHabbo().Look = PlusEnvironment.FigureManager.ProcessFigure(final.TrimEnd('.'), session.GetHabbo().Gender, session.GetHabbo().Clothing.GetClothingParts, Plus.HabboHotel.Subscriptions.ClubAccess.LevelFor(session.GetHabbo().Access));
            using var connection = PlusEnvironment.DatabaseManager.Connection();
            connection.Execute("UPDATE users SET look=@look,gender=@gender WHERE id=@id LIMIT 1",
                new { session.GetHabbo().Look, session.GetHabbo().Gender, session.GetHabbo().Id });
            var room = session.GetHabbo().CurrentRoom;
            if (room != null)
            {
                var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Username);
                if (user != null)
                {
                    session.Send(new UserChangeComposer(AvatarChangeSnapshot.Capture(user, true)));
                    session.GetHabbo().CurrentRoom.SendPacket(new UserChangeComposer(AvatarChangeSnapshot.Capture(user, false)));
                }
            }
        }
    }

    public void OnWiredTrigger(Item item) { }
}
