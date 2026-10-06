using Plus.HabboHotel.Subscriptions;
﻿using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Dapper;
using Plus.Core.FigureData;
using Plus.Database;
using Plus.HabboHotel.GameClients;

using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User.Fun;

internal class FacelessCommand : IChatCommand
{
    private readonly IFigureDataManager _figureDataManager;
    private readonly IDatabase _database;
    public string Key => "faceless";

    public string Parameters => "";

    public string Description => "Allows you to go faceless!";

    public FacelessCommand(IFigureDataManager figureDataManager, IDatabase database)
    {
        _figureDataManager = figureDataManager;
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (user == null || user.GetClient() == null)
            return;
        string[] headParts;
        var figureParts = session.GetHabbo().Look.Split('.');
        foreach (var part in figureParts)
        {
            if (part.StartsWith("hd"))
            {
                headParts = part.Split('-');
                if (!headParts[1].Equals("99999"))
                    headParts[1] = "99999";
                else
                    return;
                session.GetHabbo().Look = session.GetHabbo().Look.Replace(part, $"hd-{headParts[1]}-{headParts[2]}");
                break;
            }
        }
        session.GetHabbo().Look = _figureDataManager.ProcessFigure(session.GetHabbo().Look, session.GetHabbo().Gender, session.GetHabbo().Clothing.GetClothingParts, ClubAccess.LevelFor(session.GetHabbo().Access));
        using var connection = _database.Connection();
        connection.Execute("UPDATE users SET look=@look WHERE id=@userId LIMIT 1",
            new { look = session.GetHabbo().Look, userId = session.GetHabbo().Id });
        session.Send(new UserChangeComposer(AvatarChangeSnapshot.Capture(user, true)));
        session.GetHabbo().CurrentRoom.SendPacket(new UserChangeComposer(AvatarChangeSnapshot.Capture(user, false)));
    }
}
