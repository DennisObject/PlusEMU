using System.Collections.Immutable;
using Dapper;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.Chat.Filter;

namespace Plus.HabboHotel.Rooms;

public sealed record RoomSettingsRequest(uint RoomId, string Name, string Description, int Access, string Password, int MaxUsers, int CategoryId,
    ImmutableArray<string> Tags, int TradeSettings, bool AllowPets, bool AllowPetsEat,
    bool RoomBlockingEnabled, bool Hidewall, int WallThickness, int FloorThickness, int WhoMute, int WhoKick, int WhoBan,
    int ChatMode, int ChatSize, int ChatSpeed, int ChatDistance, int ExtraFlood);

public interface IRoomSettingsStore
{
    void Save(RoomSettingsRequest values, int ownerId, RoomAccess access);
}

public sealed class RoomSettingsStore(IDatabase database) : IRoomSettingsStore
{
    public void Save(RoomSettingsRequest values, int ownerId, RoomAccess access)
    {
        using var connection = database.Connection();
        var state = access switch { RoomAccess.Password => "password", RoomAccess.Doorbell => "locked", RoomAccess.Invisible => "invisible", _ => "open" };
        var affected = connection.Execute("""
            UPDATE rooms SET caption=@Name, description=@Description, password=@Password, category=@CategoryId,
                state=@state, tags=@tags, users_max=@MaxUsers, allow_pets=@AllowPets, allow_pets_eat=@AllowPetsEat,
                room_blocking_disabled=@RoomBlockingEnabled, allow_hidewall=@Hidewall, floorthick=@FloorThickness,
                wallthick=@WallThickness, mute_settings=@WhoMute, kick_settings=@WhoKick, ban_settings=@WhoBan,
                chat_mode=@ChatMode, chat_size=@ChatSize, chat_speed=@ChatSpeed, chat_extra_flood=@ExtraFlood,
                chat_hearing_distance=@ChatDistance, trade_settings=@TradeSettings
            WHERE id=@RoomId AND owner=@ownerId LIMIT 1
            """, new
        {
            values.RoomId,
            values.Name,
            values.Description,
            values.Password,
            values.CategoryId,
            state,
            tags = string.Join(",", values.Tags),
            values.MaxUsers,
            values.AllowPets,
            values.AllowPetsEat,
            values.RoomBlockingEnabled,
            values.Hidewall,
            values.FloorThickness,
            values.WallThickness,
            values.WhoMute,
            WhoKick = values.WhoKick.ToString(System.Globalization.CultureInfo.InvariantCulture),
            values.WhoBan,
            values.ChatMode,
            values.ChatSize,
            values.ChatSpeed,
            values.ExtraFlood,
            values.ChatDistance,
            values.TradeSettings,
            ownerId
        });

        if (affected != 1) {
            throw new InvalidOperationException("Room settings could not be saved for the expected owner.");
        }
    }
}

public interface IRoomSettingsService
{
    void Save(GameClient session, RoomSettingsRequest request);
    void Show(GameClient session, uint roomId);
}

public sealed class RoomSettingsService(IRoomManager _roomManager, IWordFilterManager _wordFilterManager,
    INavigatorManager _navigationManager, IAchievementManager _achievementManager, IRoomSettingsStore store,
    ISettingsManager _settings) : IRoomSettingsService
{
    public void Show(GameClient session, uint roomId)
    {
        if (!_roomManager.TryLoadRoom(roomId, out var room)) {
            return;
        }

        var habbo = session.GetHabbo();

        if (habbo.Id != room.OwnerId && !habbo.Access.Can(PermissionKeys.RoomOwnerAny)) {
            return;
        }

        RoomSettingsSnapshot snapshot;

        lock (room.Data) {
            snapshot = RoomSettingsSnapshot.Capture(room);
        }

        session.Send(new RoomSettingsDataComposer(snapshot));
    }

    public void Save(GameClient session, RoomSettingsRequest request)
    {
        if (!_roomManager.TryLoadRoom(request.RoomId, out var room)) {
            return;
        }

        var habbo = session.GetHabbo();

        if (habbo.Id != room.OwnerId && !habbo.Access.Can(PermissionKeys.RoomOwnerAny)) {
            return;
        }

        lock (room.Data) {
            var name = _wordFilterManager.CheckMessage(request.Name);
            var description = _wordFilterManager.CheckMessage(request.Description);
            var access = RoomAccessUtility.ToRoomAccess(request.Access);
            var password = request.Password;
            var maxUsers = request.MaxUsers;
            var categoryId = request.CategoryId;
            var tags = request.Tags.Select(tag => tag.ToLowerInvariant()).ToList();
            var tradeSettings = request.TradeSettings;
            var allowPets = request.AllowPets;
            var allowPetsEat = request.AllowPetsEat;
            var roomBlockingEnabled = request.RoomBlockingEnabled;
            var hidewall = request.Hidewall;
            var wallThickness = request.WallThickness;
            var floorThickness = request.FloorThickness;
            var whoMute = request.WhoMute;
            var whoKick = request.WhoKick;
            var whoBan = request.WhoBan;
            var chatMode = request.ChatMode;
            var chatSize = request.ChatSize;
            var chatSpeed = request.ChatSpeed;
            var chatDistance = request.ChatDistance;
            var extraFlood = request.ExtraFlood;

            if (chatMode < 0 || chatMode > 1) {
                chatMode = 0;
            }

            if (chatSize < 0 || chatSize > 2) {
                chatSize = 0;
            }

            if (chatSpeed < 0 || chatSpeed > 2) {
                chatSpeed = 0;
            }

            if (chatDistance < 0) {
                chatDistance = 1;
            }

            if (chatDistance > 99) {
                chatDistance = 100;
            }

            if (extraFlood < 0 || extraFlood > 2) {
                extraFlood = 0;
            }

            if (tradeSettings < 0 || tradeSettings > 2) {
                tradeSettings = 0;
            }

            if (whoMute < 0 || whoMute > 1) {
                whoMute = 0;
            }

            if (whoKick < 0 || whoKick > 2) {
                whoKick = 0;
            }

            if (whoBan < 0 || whoBan > 1) {
                whoBan = 0;
            }

            if (wallThickness < -2 || wallThickness > 1) {
                wallThickness = 0;
            }

            if (floorThickness < -2 || floorThickness > 1) {
                floorThickness = 0;
            }

            if (name.Length < 1) {
                return;
            }

            if (name.Length > 60) {
                name = name.Substring(0, 60);
            }

            if (access == RoomAccess.Password && password.Length == 0) {
                access = RoomAccess.Open;
            }

            if (maxUsers < 0) {
                maxUsers = 10;
            }

            maxUsers = Math.Min(maxUsers, Plus.HabboHotel.Subscriptions.ClubLimits.For(session.GetHabbo().Access, "visitors", _settings));

            if (Plus.HabboHotel.Subscriptions.ClubAccess.LevelFor(session.GetHabbo().Access) == 0) {
                hidewall = false;
                wallThickness = 0;
                floorThickness = 0;
            }

            _navigationManager.TryGetSearchResultList(categoryId, out var searchResultList);
            categoryId = RoomCategoryChoice.Resolve(categoryId, searchResultList, session.GetHabbo().Access, session.GetHabbo().Id, room.OwnerId, applyOwnerRule: true);

            if (tags.Count > 2) {
                return;
            }

            var prepared = request with
            {
                Name = name,
                Description = description,
                Password = password,
                MaxUsers = maxUsers,
                CategoryId = categoryId,
                Tags = tags.ToImmutableArray(),
                TradeSettings = tradeSettings,
                Hidewall = hidewall,
                WallThickness = wallThickness,
                FloorThickness = floorThickness,
                WhoMute = whoMute,
                WhoKick = whoKick,
                WhoBan = whoBan,
                ChatMode = chatMode,
                ChatSize = chatSize,
                ChatSpeed = chatSpeed,
                ChatDistance = chatDistance,
                ExtraFlood = extraFlood
            };
            store.Save(prepared, room.OwnerId, access);
            room.AllowPets = allowPets;
            room.AllowPetsEating = allowPetsEat;
            room.RoomBlockingEnabled = roomBlockingEnabled;
            room.Hidewall = hidewall;
            room.Name = name;
            room.Access = access;
            room.Description = description;
            room.Category = categoryId;
            room.Password = password;
            room.WhoCanBan = whoBan;
            room.WhoCanKick = whoKick;
            room.WhoCanMute = whoMute;
            room.ClearTags();
            room.AddTagRange(tags);
            room.UsersMax = maxUsers;
            room.WallThickness = wallThickness;
            room.FloorThickness = floorThickness;
            room.ChatMode = chatMode;
            room.ChatSize = chatSize;
            room.ChatSpeed = chatSpeed;
            room.ChatDistance = chatDistance;
            room.ExtraFlood = extraFlood;
            room.TradeSettings = tradeSettings;
            room.GetGameMap().GenerateMaps();

            if (session.GetHabbo().CurrentRoom == null) {
                session.Send(new RoomSettingsSavedComposer(room.RoomId));
                session.Send(new RoomInfoUpdatedComposer(room.RoomId));
                session.Send(new RoomVisualizationSettingsComposer(room.WallThickness, room.FloorThickness, Convert.ToBoolean(room.Hidewall)));
            }
            else {
                room.SendPacket(new RoomSettingsSavedComposer(room.RoomId));
                room.SendPacket(new RoomInfoUpdatedComposer(room.RoomId));
                room.SendPacket(new RoomVisualizationSettingsComposer(room.WallThickness, room.FloorThickness, Convert.ToBoolean(room.Hidewall)));
            }

            _achievementManager.ProgressAchievement(session, "ACH_SelfModDoorModeSeen", 1);
            _achievementManager.ProgressAchievement(session, "ACH_SelfModWalkthroughSeen", 1);
            _achievementManager.ProgressAchievement(session, "ACH_SelfModChatScrollSpeedSeen", 1);
            _achievementManager.ProgressAchievement(session, "ACH_SelfModChatFloodFilterSeen", 1);
            _achievementManager.ProgressAchievement(session, "ACH_SelfModChatHearRangeSeen", 1);
        }
    }
}
