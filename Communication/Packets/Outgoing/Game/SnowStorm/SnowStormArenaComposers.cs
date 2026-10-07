using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;
using Plus.HabboHotel.Games.SnowStorm.Simulation;

namespace Plus.Communication.Packets.Outgoing.Game.SnowStorm;

internal static class SnowStormArenaWire
{
    public static void WritePlayer(IOutgoingPacket packet, SnowStormArenaPlayer player)
    {
        packet.WriteInteger(player.ReferenceId);
        packet.WriteString(player.UserName);
        packet.WriteString(player.Figure);
        packet.WriteString(player.Gender);
        packet.WriteInteger(player.TeamId);
    }

    public static void WriteObjects(IOutgoingPacket packet, ImmutableArray<SnowStormWireObject> objects)
    {
        packet.WriteInteger(objects.Length);

        foreach (var item in objects) {
            foreach (var value in item.Variables) {
                packet.WriteInteger(value);
            }

            foreach (var text in item.Strings) {
                packet.WriteString(text);
            }
        }
    }

    public static void WriteStatus(IOutgoingPacket packet, SnowStormStatusSnapshot status)
    {
        packet.WriteInteger(status.Turn);
        packet.WriteInteger(status.Checksum);
        packet.WriteInteger(status.Subturns.Length);

        foreach (var events in status.Subturns) {
            packet.WriteInteger(events.Length);

            foreach (var item in events) {
                packet.WriteInteger(item.Type);

                foreach (var field in item.Fields) {
                    packet.WriteInteger(field);
                }
            }
        }
    }
}

/// <summary>AIR EnterArena: the players (Game2PlayerData) and the GameLevelData with its FuseObjectData list.</summary>
public sealed class Game2EnterArenaComposer(int gameType, int fieldType, int numberOfTeams, ImmutableArray<SnowStormArenaPlayer> players, SnowStormArenaLevel arena) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2EnterArenaComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(gameType);
        packet.WriteInteger(fieldType);
        packet.WriteInteger(numberOfTeams);
        packet.WriteInteger(players.Length);

        foreach (var player in players) {
            SnowStormArenaWire.WritePlayer(packet, player);
        }

        var level = arena.Level;
        packet.WriteInteger(level.Width);
        packet.WriteInteger(level.Height);
        packet.WriteString(level.HeightMap);
        packet.WriteInteger(level.FuseObjects.Count);

        foreach (var item in level.FuseObjects) {
            packet.WriteString(item.Name);
            packet.WriteInteger(item.Id);
            packet.WriteInteger(item.X);
            packet.WriteInteger(item.Y);
            packet.WriteInteger(item.XDimension);
            packet.WriteInteger(item.YDimension);
            packet.WriteInteger(item.Height);
            packet.WriteInteger(item.Direction);
            packet.WriteInteger(item.Altitude);
            packet.WriteBoolean(item.CanStandOn);

            // Full StuffData: map (type 1) for decoration such as the backdrop, else legacy (type 0) with the state.
            if (arena.MapStuff.TryGetValue(item.Id, out var stuff)) {
                packet.WriteInteger(1);
                packet.WriteInteger(stuff.Length);

                foreach (var (key, value) in stuff) {
                    packet.WriteString(key);
                    packet.WriteString(value);
                }
            }
            else {
                packet.WriteInteger(0);
                packet.WriteString(item.Stuff);
            }
        }
    }
}

public sealed class Game2ArenaEnteredComposer(SnowStormArenaPlayer player) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2ArenaEnteredComposer;

    public void Compose(IOutgoingPacket packet) => SnowStormArenaWire.WritePlayer(packet, player);
}

public sealed class Game2EnterArenaFailedComposer(int reason) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2EnterArenaFailedComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(reason);
}

public sealed class Game2StageLoadComposer(int gameType) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2StageLoadComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(gameType);
}

public sealed class Game2StageStillLoadingComposer(int percentage, ImmutableArray<int> finishedUserIds) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2StageStillLoadingComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(percentage);
        packet.WriteInteger(finishedUserIds.Length);

        foreach (var userId in finishedUserIds) {
            packet.WriteInteger(userId);
        }
    }
}

public sealed class Game2StageStartingComposer(int gameType, string roomType, int countDown, ImmutableArray<SnowStormWireObject> objects) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2StageStartingComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(gameType);
        packet.WriteString(roomType);
        packet.WriteInteger(countDown);
        SnowStormArenaWire.WriteObjects(packet, objects);
    }
}

public sealed class Game2StageRunningComposer(int timeToStageEnd) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2StageRunningComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(timeToStageEnd);
}

public sealed class Game2GameStatusComposer(SnowStormStatusSnapshot status) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2GameStatusComposer;

    public void Compose(IOutgoingPacket packet) => SnowStormArenaWire.WriteStatus(packet, status);
}

public sealed class Game2FullGameStatusComposer(SnowStormFullStatusSnapshot status) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2FullGameStatusComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // AIR ignores the first and fifth ints.
        packet.WriteInteger(0);
        packet.WriteInteger(status.RemainingSeconds);
        packet.WriteInteger(status.DurationSeconds);
        SnowStormArenaWire.WriteObjects(packet, status.Objects);
        packet.WriteInteger(0);
        packet.WriteInteger(status.NumberOfTeams);
        SnowStormArenaWire.WriteStatus(packet, status.Status);
    }
}

public sealed class Game2StageEndingComposer(int timeToNextState) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2StageEndingComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(timeToNextState);
}

public sealed class Game2GameEndingComposer(int timeToNextState, SnowStormGameResult result) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2GameEndingComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(timeToNextState);
        packet.WriteBoolean(result.IsDeathMatch);
        packet.WriteInteger(result.ResultType);
        packet.WriteInteger(result.WinnerId);
        packet.WriteInteger(result.Teams.Length);

        foreach (var team in result.Teams) {
            packet.WriteInteger(team.TeamId);
            packet.WriteInteger(team.Score);
            packet.WriteInteger(team.Players.Length);

            foreach (var player in team.Players) {
                packet.WriteString(player.UserName);
                packet.WriteInteger(player.UserId);
                packet.WriteString(player.Figure);
                packet.WriteString(player.Gender);
                packet.WriteInteger(player.Score);
                var stats = player.Stats;
                packet.WriteInteger(stats.Score);
                packet.WriteInteger(stats.Kills);
                packet.WriteInteger(stats.Deaths);
                packet.WriteInteger(stats.SnowballHits);
                packet.WriteInteger(stats.SnowballHitsTaken);
                packet.WriteInteger(stats.SnowballsThrown);
                packet.WriteInteger(stats.SnowballsCreated);
                packet.WriteInteger(stats.SnowballsFromMachine);
                packet.WriteInteger(stats.FriendlyHits);
                packet.WriteInteger(stats.FriendlyKills);
            }
        }

        packet.WriteInteger(result.PlayerWithMostKills);
        packet.WriteInteger(result.PlayerWithMostHits);
    }
}

public sealed class Game2PlayerExitedGameArenaComposer(int userId, int playerGameObjectId) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2PlayerExitedGameArenaComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(userId);
        packet.WriteInteger(playerGameObjectId);
    }
}

public sealed class Game2RejoinPreviousRoomComposer(int roomId) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2RejoinPreviousRoomComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(roomId);
}

public sealed class Game2PlayerRematchesComposer(int userId) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2PlayerRematchesComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(userId);
}

public sealed class Game2GameChatComposer(int userId, string message) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2GameChatComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(userId);
        packet.WriteString(message);
    }
}
