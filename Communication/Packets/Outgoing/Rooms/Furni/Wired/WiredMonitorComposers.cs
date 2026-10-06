using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Rooms.Instance;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

/// <summary>
/// The monitor tab. Usage is the busiest pass of the last window against the per-pass budget,
/// times are whole milliseconds per pass and recursion is the deepest chain. Plus has no heavy,
/// overload or kill state, so heavy is false, killed is 0 and their six thresholds are 0.
/// </summary>
public sealed class WiredMonitorDataComposer(WiredMonitorSnapshot snapshot) : IServerPacket
{
    private readonly WiredMonitorSnapshot _captured = snapshot with { Logs = snapshot.Logs with { Tallies = snapshot.Logs.Tallies.ToImmutableArray(), Recent = snapshot.Logs.Recent.ToImmutableArray() } };
    public uint MessageId => ServerPacketHeader.WiredMonitorDataComposer;
    public void Compose(IOutgoingPacket packet)
    {
        var engine = _captured.Engine;
        packet.WriteInteger(engine.PeakExecutions); packet.WriteInteger(_captured.ExecutionsPerPass);
        packet.WriteBoolean(false);
        packet.WriteInteger(engine.Pending); packet.WriteInteger(_captured.PendingLimit);
        packet.WriteInteger(engine.AverageMs); packet.WriteInteger(engine.PeakMs);
        packet.WriteInteger(engine.PeakDepth); packet.WriteInteger(_captured.DepthLimit);
        packet.WriteInteger(0);
        packet.WriteInteger(engine.WindowMs);
        for (var threshold = 0; threshold < 6; threshold++) packet.WriteInteger(0);
        packet.WriteInteger(_captured.Logs.Tallies.Count);
        foreach (var tally in _captured.Logs.Tallies)
        {
            var latest = tally.Latest;
            packet.WriteString(WiredRoomLogEntry.TypeName(tally.Source));
            packet.WriteString(WiredRoomLogEntry.LevelName(latest?.Level ?? (tally.Source == WiredLogSource.WiredLog ? 1 : WiredRoomLog.ErrorLevel)));
            packet.WriteInteger(tally.Count);
            packet.WriteInteger(latest == null ? 0 : Seconds(latest));
            packet.WriteString(latest?.Reason ?? ""); packet.WriteString(latest?.Label ?? "");
            packet.WriteInteger(unchecked((int)(latest?.BoxId ?? 0)));
        }
        packet.WriteInteger(_captured.Logs.Recent.Count);
        foreach (var entry in _captured.Logs.Recent)
        {
            packet.WriteString(WiredRoomLogEntry.TypeName(entry.Source)); packet.WriteString(WiredRoomLogEntry.LevelName(entry.Level));
            packet.WriteInteger(Seconds(entry));
            packet.WriteString(entry.Reason); packet.WriteString(entry.Label); packet.WriteInteger(unchecked((int)entry.BoxId));
        }
    }
    private static int Seconds(WiredRoomLogEntry entry) => (int)Math.Clamp(entry.Timestamp.ToUnixTimeSeconds(), 0, int.MaxValue);
}

/// <summary>Official AIR <c>WiredLogPage</c>: a 1-based page of the room log and the filters that produced it.</summary>
public sealed class WiredRoomLogPageComposer(WiredRoomLogPage page, int levelFilter, int sourceFilter, string query) : IServerPacket
{
    private readonly WiredRoomLogPage _captured = page with { Entries = page.Entries.ToImmutableArray() };
    public uint MessageId => ServerPacketHeader.WiredRoomLogPageComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_captured.Total); packet.WriteInteger(_captured.Page + 1); packet.WriteInteger(_captured.Amount);
        packet.WriteInteger(_captured.Entries.Count);
        foreach (var entry in _captured.Entries)
        {
            packet.WriteInteger(unchecked((int)(entry.Id >> 32))); packet.WriteInteger(unchecked((int)entry.Id));
            packet.WriteByte((byte)entry.Level); packet.WriteByte((byte)entry.Source);
            packet.WriteString(entry.Message);
            WiredVariableHoldersPageComposer.WriteTimestamp(packet, entry.Timestamp);
        }
        packet.WriteBoolean(levelFilter >= 0);
        if (levelFilter >= 0) packet.WriteByte((byte)levelFilter);
        packet.WriteBoolean(sourceFilter >= 0);
        if (sourceFilter >= 0) packet.WriteByte((byte)sourceFilter);
        packet.WriteBoolean(query.Length > 0);
        if (query.Length > 0) packet.WriteString(query);
    }
}
