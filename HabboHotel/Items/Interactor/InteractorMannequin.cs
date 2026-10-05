using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Interactor;

internal class InteractorMannequin(IUserProfileService profiles) : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item) { }

    public void OnRemove(GameClient? session, Item item) { }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        if (item.LegacyDataString.Contains(Convert.ToChar(5).ToString()))
        {
            var stuff = item.LegacyDataString.Split(Convert.ToChar(5));
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
            profiles.ApplyMannequin(session, new(stuff[0], final.TrimEnd('.')));
        }
    }

    public void OnWiredTrigger(Item item) { }
}
