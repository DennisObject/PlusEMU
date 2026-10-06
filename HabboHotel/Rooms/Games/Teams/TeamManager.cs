using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.Games.Teams;

public class TeamManager
{
    public List<RoomUser> BlueTeam;
    public string Game;
    public List<RoomUser> GreenTeam;
    public List<RoomUser> RedTeam;
    public List<RoomUser> YellowTeam;

    public static TeamManager CreateTeam(string game)
    {
        var t = new TeamManager();
        t.Game = game;
        t.BlueTeam = new();
        t.RedTeam = new();
        t.GreenTeam = new();
        t.YellowTeam = new();

        return t;
    }

    public bool CanEnterOnTeam(Team t)
    {
        if (t.Equals(Team.Blue))
        {
            return BlueTeam.Count < 5;
        }

        if (t.Equals(Team.Red))
        {
            return RedTeam.Count < 5;
        }

        if (t.Equals(Team.Yellow))
        {
            return YellowTeam.Count < 5;
        }

        if (t.Equals(Team.Green))
        {
            return GreenTeam.Count < 5;
        }

        return false;
    }

    public void AddUser(RoomUser user)
    {
        if (user.Team.Equals(Team.Blue) && !BlueTeam.Contains(user))
        {
            BlueTeam.Add(user);
        }
        else if (user.Team.Equals(Team.Red) && !RedTeam.Contains(user))
        {
            RedTeam.Add(user);
        }
        else if (user.Team.Equals(Team.Yellow) && !YellowTeam.Contains(user))
        {
            YellowTeam.Add(user);
        }
        else if (user.Team.Equals(Team.Green) && !GreenTeam.Contains(user))
        {
            GreenTeam.Add(user);
        }

        switch (Game.ToLower())
        {
            case "banzai":
                {
                    var room = user.GetClient().GetHabbo().CurrentRoom;

                    if (room == null)
                    {
                        return;
                    }

                    foreach (var item in room.GetRoomItemHandler().GetFloor.ToList())
                    {
                        if (item == null)
                        {
                            continue;
                        }

                        if (item.Definition.InteractionType.Equals(InteractionType.Banzaigateblue))
                        {
                            item.LegacyDataString = BlueTeam.Count.ToString();
                            item.UpdateState();

                            if (BlueTeam.Count == 5)
                            {
                                foreach (var sser in room.GetGameMap().GetRoomUsers(new(item.GetX, item.GetY)))
                                {
                                    sser.SqState = 0;
                                }

                                room.GetGameMap().SetFloorStatus(item.GetX, item.GetY, 0);
                            }
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.Banzaigatered))
                        {
                            item.LegacyDataString = RedTeam.Count.ToString();
                            item.UpdateState();

                            if (RedTeam.Count == 5)
                            {
                                foreach (var sser in room.GetGameMap().GetRoomUsers(new(item.GetX, item.GetY)))
                                {
                                    sser.SqState = 0;
                                }

                                room.GetGameMap().SetFloorStatus(item.GetX, item.GetY, 0);
                            }
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.Banzaigategreen))
                        {
                            item.LegacyDataString = GreenTeam.Count.ToString();
                            item.UpdateState();

                            if (GreenTeam.Count == 5)
                            {
                                foreach (var sser in room.GetGameMap().GetRoomUsers(new(item.GetX, item.GetY)))
                                {
                                    sser.SqState = 0;
                                }

                                room.GetGameMap().SetFloorStatus(item.GetX, item.GetY, 0);
                            }
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.Banzaigateyellow))
                        {
                            item.LegacyDataString = YellowTeam.Count.ToString();
                            item.UpdateState();

                            if (YellowTeam.Count == 5)
                            {
                                foreach (var sser in room.GetGameMap().GetRoomUsers(new(item.GetX, item.GetY)))
                                {
                                    sser.SqState = 0;
                                }

                                room.GetGameMap().SetFloorStatus(item.GetX, item.GetY, 0);
                            }
                        }
                    }

                    break;
                }
            case "freeze":
                {
                    var room = user.GetClient().GetHabbo().CurrentRoom;

                    if (room == null)
                    {
                        return;
                    }

                    foreach (var item in room.GetRoomItemHandler().GetFloor.ToList())
                    {
                        if (item == null)
                        {
                            continue;
                        }

                        if (item.Definition.InteractionType.Equals(InteractionType.FreezeBlueGate))
                        {
                            item.LegacyDataString = BlueTeam.Count.ToString();
                            item.UpdateState();
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.FreezeRedGate))
                        {
                            item.LegacyDataString = RedTeam.Count.ToString();
                            item.UpdateState();
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.FreezeGreenGate))
                        {
                            item.LegacyDataString = GreenTeam.Count.ToString();
                            item.UpdateState();
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.FreezeYellowGate))
                        {
                            item.LegacyDataString = YellowTeam.Count.ToString();
                            item.UpdateState();
                        }
                    }

                    break;
                }
        }
    }

    public void OnUserLeave(RoomUser user)
    {
        //Console.WriteLine("remove user from team! (" + Game + ")");
        if (user.Team.Equals(Team.Blue) && BlueTeam.Contains(user))
        {
            BlueTeam.Remove(user);
        }
        else if (user.Team.Equals(Team.Red) && RedTeam.Contains(user))
        {
            RedTeam.Remove(user);
        }
        else if (user.Team.Equals(Team.Yellow) && YellowTeam.Contains(user))
        {
            YellowTeam.Remove(user);
        }
        else if (user.Team.Equals(Team.Green) && GreenTeam.Contains(user))
        {
            GreenTeam.Remove(user);
        }

        switch (Game.ToLower())
        {
            case "banzai":
                {
                    var room = user.GetClient().GetHabbo().CurrentRoom;

                    if (room == null)
                    {
                        return;
                    }

                    foreach (var item in room.GetRoomItemHandler().GetFloor.ToList())
                    {
                        if (item == null)
                        {
                            continue;
                        }

                        if (item.Definition.InteractionType.Equals(InteractionType.Banzaigateblue))
                        {
                            item.LegacyDataString = BlueTeam.Count.ToString();
                            item.UpdateState();

                            if (room.GetGameMap().GameMap[item.GetX, item.GetY] == 0)
                            {
                                foreach (var sser in room.GetGameMap().GetRoomUsers(new(item.GetX, item.GetY)))
                                {
                                    sser.SqState = 1;
                                }

                                room.GetGameMap().SetFloorStatus(item.GetX, item.GetY, 1);
                            }
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.Banzaigatered))
                        {
                            item.LegacyDataString = RedTeam.Count.ToString();
                            item.UpdateState();

                            if (room.GetGameMap().GameMap[item.GetX, item.GetY] == 0)
                            {
                                foreach (var sser in room.GetGameMap().GetRoomUsers(new(item.GetX, item.GetY)))
                                {
                                    sser.SqState = 1;
                                }

                                room.GetGameMap().SetFloorStatus(item.GetX, item.GetY, 1);
                            }
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.Banzaigategreen))
                        {
                            item.LegacyDataString = GreenTeam.Count.ToString();
                            item.UpdateState();

                            if (room.GetGameMap().GameMap[item.GetX, item.GetY] == 0)
                            {
                                foreach (var sser in room.GetGameMap().GetRoomUsers(new(item.GetX, item.GetY)))
                                {
                                    sser.SqState = 1;
                                }

                                room.GetGameMap().SetFloorStatus(item.GetX, item.GetY, 1);
                            }
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.Banzaigateyellow))
                        {
                            item.LegacyDataString = YellowTeam.Count.ToString();
                            item.UpdateState();

                            if (room.GetGameMap().GameMap[item.GetX, item.GetY] == 0)
                            {
                                foreach (var sser in room.GetGameMap().GetRoomUsers(new(item.GetX, item.GetY)))
                                {
                                    sser.SqState = 1;
                                }

                                room.GetGameMap().SetFloorStatus(item.GetX, item.GetY, 1);
                            }
                        }
                    }

                    break;
                }
            case "freeze":
                {
                    var room = user.GetClient().GetHabbo().CurrentRoom;

                    if (room == null)
                    {
                        return;
                    }

                    foreach (var item in room.GetRoomItemHandler().GetFloor.ToList())
                    {
                        if (item == null)
                        {
                            continue;
                        }

                        if (item.Definition.InteractionType.Equals(InteractionType.FreezeBlueGate))
                        {
                            item.LegacyDataString = BlueTeam.Count.ToString();
                            item.UpdateState();
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.FreezeRedGate))
                        {
                            item.LegacyDataString = RedTeam.Count.ToString();
                            item.UpdateState();
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.FreezeGreenGate))
                        {
                            item.LegacyDataString = GreenTeam.Count.ToString();
                            item.UpdateState();
                        }
                        else if (item.Definition.InteractionType.Equals(InteractionType.FreezeYellowGate))
                        {
                            item.LegacyDataString = YellowTeam.Count.ToString();
                            item.UpdateState();
                        }
                    }

                    break;
                }
        }
    }

    public void Dispose()
    {
        BlueTeam.Clear();
        GreenTeam.Clear();
        RedTeam.Clear();
        YellowTeam.Clear();
    }
}
