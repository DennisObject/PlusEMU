using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Outgoing.Game.SnowStorm;

internal static class SnowStormLobbyWire
{
    public static void WriteLobby(IOutgoingPacket packet, SnowStormLobbySnapshot lobby)
    {
        packet.WriteInteger(lobby.GameId);
        packet.WriteString(lobby.LevelName);
        packet.WriteInteger(lobby.GameType);
        packet.WriteInteger(lobby.FieldType);
        packet.WriteInteger(lobby.NumberOfTeams);
        packet.WriteInteger(lobby.MaximumPlayers);
        packet.WriteString(lobby.OwningPlayerName);
        packet.WriteInteger(lobby.LevelEntryId);
        packet.WriteInteger(lobby.Players.Length);

        foreach (var player in lobby.Players) {
            WritePlayer(packet, player);
        }
    }

    public static void WritePlayer(IOutgoingPacket packet, SnowStormLobbyPlayer player)
    {
        packet.WriteInteger(player.UserId);
        packet.WriteString(player.Name);
        packet.WriteString(player.Figure);
        packet.WriteString(player.Gender);
        packet.WriteInteger(player.TeamId);
        packet.WriteInteger(player.SkillLevel);
        packet.WriteInteger(player.TotalScore);
        packet.WriteInteger(player.ScoreToNextLevel);
    }
}

public sealed class Game2GameCreatedComposer(SnowStormLobbySnapshot lobby) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2GameCreatedComposer;

    public void Compose(IOutgoingPacket packet) => SnowStormLobbyWire.WriteLobby(packet, lobby);
}

public sealed class Game2GameLongDataComposer(SnowStormLobbySnapshot lobby) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2GameLongDataComposer;

    public void Compose(IOutgoingPacket packet) => SnowStormLobbyWire.WriteLobby(packet, lobby);
}

public sealed class Game2GameStartedComposer(SnowStormLobbySnapshot lobby) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2GameStartedComposer;

    public void Compose(IOutgoingPacket packet) => SnowStormLobbyWire.WriteLobby(packet, lobby);
}

public sealed class Game2UserJoinedGameComposer(SnowStormLobbyPlayer player, bool wasTeamSwitched) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2UserJoinedGameComposer;

    public void Compose(IOutgoingPacket packet)
    {
        SnowStormLobbyWire.WritePlayer(packet, player);
        packet.WriteBoolean(wasTeamSwitched);
    }
}

public sealed class Game2UserLeftGameComposer(int userId) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2UserLeftGameComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(userId);
}

public sealed class Game2InArenaQueueComposer(int position) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2InArenaQueueComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(position);
}

public sealed class Game2StartCounterComposer(int countDownSeconds) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2StartCounterComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(countDownSeconds);
}

public sealed class Game2StopCounterComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2StopCounterComposer;

    // AIR reads the InArenaQueue shape and ignores the value.
    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(0);
}

public sealed class Game2JoiningGameFailedComposer(int reason) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2JoiningGameFailedComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(reason);
}

public sealed class Game2StartingGameFailedComposer(int reason) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2StartingGameFailedComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(reason);
}

public sealed class Game2GameCancelledComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2GameCancelledComposer;

    public void Compose(IOutgoingPacket packet) { }
}

public sealed class Game2GameNotFoundComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2GameNotFoundComposer;

    public void Compose(IOutgoingPacket packet) { }
}

public sealed class Game2UserBlockedComposer(int blockSeconds) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2UserBlockedComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(blockSeconds);
}

public sealed class Game2GameDirectoryStatusComposer(int status, int blockSeconds, int gamesPlayed, int freeGamesLeft) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2GameDirectoryStatusComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(status);
        packet.WriteInteger(blockSeconds);
        packet.WriteInteger(gamesPlayed);
        packet.WriteInteger(freeGamesLeft);
    }
}
