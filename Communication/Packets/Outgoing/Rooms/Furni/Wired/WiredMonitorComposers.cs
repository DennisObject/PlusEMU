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
    public uint MessageId => ServerPacketHeader.WiredMonitorDataComposer;
    public void Compose(IOutgoingPacket packet)
    {
        var engine = snapshot.Engine;
        packet.WriteInteger(engine.PeakExecutions); packet.WriteInteger(snapshot.ExecutionsPerPass);
        packet.WriteBoolean(false);
        packet.WriteInteger(engine.Pending); packet.WriteInteger(snapshot.PendingLimit);
        packet.WriteInteger(engine.AverageMs); packet.WriteInteger(engine.PeakMs);
        packet.WriteInteger(engine.PeakDepth); packet.WriteInteger(snapshot.DepthLimit);
        packet.WriteInteger(0);
        packet.WriteInteger(engine.WindowMs);
        for (var threshold = 0; threshold < 6; threshold++) packet.WriteInteger(0);
        packet.WriteInteger(snapshot.Logs.Tallies.Count);
        foreach (var tally in snapshot.Logs.Tallies)
        {
            var latest = tally.Latest;
            packet.WriteString(WiredRoomLogEntry.TypeName(tally.Source));
            packet.WriteString(WiredRoomLogEntry.LevelName(latest?.Level ?? (tally.Source == WiredLogSource.WiredLog ? 1 : WiredRoomLog.ErrorLevel)));
            packet.WriteInteger(tally.Count);
            packet.WriteInteger(latest == null ? 0 : Seconds(latest));
            packet.WriteString(latest?.Reason ?? ""); packet.WriteString(latest?.Label ?? "");
            packet.WriteInteger(unchecked((int)(latest?.BoxId ?? 0)));
        }
        packet.WriteInteger(snapshot.Logs.Recent.Count);
        foreach (var entry in snapshot.Logs.Recent)
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
    public uint MessageId => ServerPacketHeader.WiredRoomLogPageComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(page.Total); packet.WriteInteger(page.Page + 1); packet.WriteInteger(page.Amount);
        packet.WriteInteger(page.Entries.Count);
        foreach (var entry in page.Entries)
        {
            packet.WriteInteger(unchecked((int)(entry.Id >> 32))); packet.WriteInteger(unchecked((int)entry.Id));
            packet.WriteByte((byte)entry.Level); packet.WriteByte((byte)entry.Source);
            packet.WriteString(entry.Message);
            WiredVariableHoldersPageComposer.WriteTimestamp(packet, entry.Timestamp.ToUnixTimeMilliseconds());
        }
        packet.WriteBoolean(levelFilter >= 0);
        if (levelFilter >= 0) packet.WriteByte((byte)levelFilter);
        packet.WriteBoolean(sourceFilter >= 0);
        if (sourceFilter >= 0) packet.WriteByte((byte)sourceFilter);
        packet.WriteBoolean(query.Length > 0);
        if (query.Length > 0) packet.WriteString(query);
    }
}
