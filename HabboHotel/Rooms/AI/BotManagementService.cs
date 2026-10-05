using Plus.HabboHotel.Users.Inventory.Bots;
using System.Text.RegularExpressions;
using Dapper;
using Plus.Communication.Packets.Outgoing.Inventory.Bots;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Core.FigureData;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.HabboHotel.Subscriptions;

using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Rooms.AI;

public enum BotAction { CopyLooks = 1, Speech = 2, Relax = 3, Dance = 4, Rename = 5 }
public sealed record BotActionRequest(int BotId, BotAction Action, string Data);
public sealed record BotPlacementData(string AiType, string WalkMode, bool AutomaticChat, int SpeakingInterval, bool MixSentences, int ChatBubble,
    IReadOnlyList<string> Speech);

public interface IBotManagementStore
{
    BotPlacementData Place(int botId, int ownerId, uint roomId, int x, int y);
    void PickUp(int botId, uint roomId);
    void SaveAppearance(int botId, uint roomId, string look, string gender);
    IReadOnlyList<string> SaveSpeech(int botId, uint roomId, IReadOnlyList<string> speech, bool automatic, int interval, bool mix);
    void SaveWalkingMode(int botId, uint roomId, string mode);
    void SaveName(int botId, uint roomId, string name);
}

public sealed class BotManagementStore(IDatabase database) : IBotManagementStore
{
    public BotPlacementData Place(int botId, int ownerId, uint roomId, int x, int y)
    {
        using var connection = database.Connection(); connection.Open(); using var transaction = connection.BeginTransaction();
        if (connection.Execute("UPDATE bots SET room_id=@roomId,x=@x,y=@y WHERE id=@botId AND user_id=@ownerId AND room_id=0 LIMIT 1",
                new { roomId, x, y, botId, ownerId }, transaction) != 1) throw new InvalidOperationException("Bot placement was not persisted.");
        var row = connection.QuerySingle<PlacementRow>(
            "SELECT ai_type AS AiType,walk_mode AS WalkMode,automatic_chat AS AutomaticChat,speaking_interval AS SpeakingInterval,mix_sentences AS MixSentences,chat_bubble AS ChatBubble FROM bots WHERE id=@botId LIMIT 1",
            new { botId }, transaction);
        var speech = connection.Query<string>("SELECT text FROM bots_speech WHERE bot_id=@botId", new { botId }, transaction).ToArray();
        transaction.Commit();
        return new(row.AiType, row.WalkMode, row.AutomaticChat, row.SpeakingInterval, row.MixSentences, row.ChatBubble, speech);
    }

    public void PickUp(int botId, uint roomId) => Execute("UPDATE bots SET room_id=0 WHERE id=@botId AND room_id=@roomId LIMIT 1", new { botId, roomId });
    public void SaveAppearance(int botId, uint roomId, string look, string gender) => Execute("UPDATE bots SET look=@look,gender=@gender WHERE id=@botId AND room_id=@roomId LIMIT 1", new { botId, roomId, look, gender });
    public void SaveWalkingMode(int botId, uint roomId, string mode) => Execute("UPDATE bots SET walk_mode=@mode WHERE id=@botId AND room_id=@roomId LIMIT 1", new { botId, roomId, mode });
    public void SaveName(int botId, uint roomId, string name) => Execute("UPDATE bots SET name=@name WHERE id=@botId AND room_id=@roomId LIMIT 1", new { botId, roomId, name });

    public IReadOnlyList<string> SaveSpeech(int botId, uint roomId, IReadOnlyList<string> speech, bool automatic, int interval, bool mix)
    {
        using var connection = database.Connection(); connection.Open(); using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM bots_speech WHERE bot_id=@botId", new { botId }, transaction);
        foreach (var text in speech) connection.Execute("INSERT INTO bots_speech (bot_id,text) VALUES (@botId,@text)", new { botId, text }, transaction);
        if (connection.Execute("UPDATE bots SET automatic_chat=@automatic,speaking_interval=@interval,mix_sentences=@mix WHERE id=@botId AND room_id=@roomId LIMIT 1",
                new { botId, roomId, automatic, interval, mix }, transaction) != 1) throw new InvalidOperationException("Bot speech was not persisted.");
        transaction.Commit();
        return speech;
    }

    private void Execute(string sql, object args)
    {
        using var connection = database.Connection();
        if (connection.Execute(sql, args) != 1) throw new InvalidOperationException("Bot mutation was not persisted.");
    }
    private sealed record PlacementRow(string AiType, string WalkMode, bool AutomaticChat, int SpeakingInterval, bool MixSentences, int ChatBubble);
}

public interface IBotManagementService
{
    void Place(Room room, GameClient session, int botId, int x, int y);
    void PickUp(GameClient session, int botId);
    void SaveAction(GameClient session, BotActionRequest request);
}

public sealed class BotManagementService(IBotManagementStore store, IFigureDataManager figures) : IBotManagementService
{
    public void Place(Room room, GameClient session, int botId, int x, int y)
    {
        if (!room.CheckRights(session, true)) return;
        if (!room.GetGameMap().CanWalk(x, y, false) || !room.GetGameMap().ValidTile(x, y)) { session.SendNotification("You cannot place a bot here!"); return; }
        if (!session.GetHabbo().Inventory.Bots.Bots.TryGetValue(botId, out var bot)) return;
        var count = room.GetRoomUserManager().GetUserList().Count(user => user != null && user.IsBot && !user.IsPet);
        if (count >= 5 && !session.GetHabbo().Access.Can(PermissionKeys.BotPlaceAnyOverride)) { session.SendNotification("Sorry; 5 bots per room only!"); return; }
        var data = store.Place(bot.Id, bot.OwnerId, room.RoomId, x, y);
        var speeches = data.Speech.Select(text => new RandomSpeech(text, bot.Id)).ToList();
        var botUser = room.GetRoomUserManager().DeployBot(new(bot.Id, room.RoomId, data.AiType, data.WalkMode, bot.Name, "", bot.Figure, x, y, 0,
            4, 0, 0, 0, 0, ref speeches, "", 0, bot.OwnerId, data.AutomaticChat, data.SpeakingInterval, data.MixSentences, data.ChatBubble), null);
        botUser.Chat("Hello!");
        room.GetGameMap().UpdateUserMovement(new(x, y), new(x, y), botUser);
        if (session.GetHabbo().Inventory.Bots.RemoveBot(botId)) session.Send(new BotInventoryComposer(BotInventorySnapshot.Capture(session.GetHabbo().Inventory.Bots.Bots.Values)));
    }

    public void PickUp(GameClient session, int botId)
    {
        var habbo = session.GetHabbo(); if (!habbo.InRoom || botId == 0 || habbo.CurrentRoom == null) return;
        var room = habbo.CurrentRoom;
        if (!room.GetRoomUserManager().TryGetBot(botId, out var bot) || bot.BotData.IsTemporary) return;
        if (habbo.Id != bot.BotData.OwnerId && !habbo.Access.Can(PermissionKeys.BotPlaceAnyOverride)) { session.SendWhisper("You can only pick up your own bots!"); return; }
        store.PickUp(botId, room.RoomId);
        room.GetGameMap().RemoveUserFromMap(bot, new(bot.X, bot.Y));
        habbo.Inventory.Bots.AddBot(new(bot.BotData.Id, bot.BotData.OwnerId, bot.BotData.Name, bot.BotData.Motto, bot.BotData.Look, bot.BotData.Gender));
        session.Send(new BotInventoryComposer(BotInventorySnapshot.Capture(habbo.Inventory.Bots.Bots.Values)));
        room.GetRoomUserManager().RemoveBot(bot.VirtualId, false);
    }

    public void SaveAction(GameClient session, BotActionRequest request)
    {
        var habbo = session.GetHabbo(); if (!habbo.InRoom || habbo.CurrentRoom == null || request.Action is < BotAction.CopyLooks or > BotAction.Rename) return;
        var room = habbo.CurrentRoom;
        if (!room.GetRoomUserManager().TryGetBot(request.BotId, out var bot) || bot.BotData.IsTemporary) return;
        if (bot.BotData.OwnerId != habbo.Id && !habbo.Access.Can(PermissionKeys.BotEditAnyOverride)) return;
        switch (request.Action)
        {
            case BotAction.CopyLooks:
                var look = figures.ProcessFigure(habbo.Look, habbo.Gender, habbo.Clothing.GetClothingParts, ClubAccess.LevelFor(habbo.Access));
                store.SaveAppearance(bot.BotData.Id, room.RoomId, look, habbo.Gender); bot.BotData.Look = look; bot.BotData.Gender = habbo.Gender;
                room.SendPacket(new UserChangeComposer(AvatarChangeSnapshot.Capture(bot.BotData))); break;
            case BotAction.Speech: SaveSpeech(bot.BotData, request.Data); break;
            case BotAction.Relax:
                var mode = bot.BotData.WalkingMode == "stand" ? "freeroam" : "stand";
                store.SaveWalkingMode(bot.BotData.Id, room.RoomId, mode); bot.BotData.WalkingMode = mode; break;
            case BotAction.Dance:
                bot.BotData.DanceId = bot.BotData.DanceId > 0 ? 0 : Random.Shared.Next(1, 4);
                room.SendPacket(new DanceComposer(bot, bot.BotData.DanceId)); break;
            case BotAction.Rename:
                if (!ValidName(session, request.Data)) return;
                store.SaveName(bot.BotData.Id, room.RoomId, request.Data); bot.BotData.Name = request.Data; room.SendUser(bot); break;
        }
    }

    private void SaveSpeech(RoomBot bot, string raw)
    {
        var parts = raw.Split(";#;", StringSplitOptions.None);
        if (parts.Length != 4 || !bool.TryParse(parts[1], out var automatic) ||
            !int.TryParse(parts[2], out var interval) || !bool.TryParse(parts[3], out var mix)) return;
        var speech = parts[0].Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(text => Regex.Replace(text, "<(.|\\n)*?>", string.Empty)).ToArray();
        interval = Math.Max(7, interval);
        var saved = store.SaveSpeech(bot.Id, bot.RoomId, speech, automatic, interval, mix);
        bot.AutomaticChat = automatic; bot.SpeakingInterval = interval; bot.MixSentences = mix; bot.RandomSpeech.Clear();
        foreach (var text in saved) bot.RandomSpeech.Add(new(text, bot.Id));
    }

    private static bool ValidName(GameClient session, string name)
    {
        if (name.Length == 0) { session.SendWhisper("Come on, atleast give the bot a name!"); return false; }
        if (name.Length >= 16) { session.SendWhisper("Come on, the bot doesn't need a name that long!"); return false; }
        if (name.Contains("<img src") || name.Contains("<font ") || name.Contains("</font>") || name.Contains("</a>") || name.Contains("<i>"))
        { session.SendWhisper("No HTML, please :<"); return false; }
        return true;
    }
}
