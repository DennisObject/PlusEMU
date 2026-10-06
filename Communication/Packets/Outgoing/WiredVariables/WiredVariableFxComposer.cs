using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables.Fx;

namespace Plus.Communication.Packets.Outgoing.WiredVariables;

/// <summary>Octane FX messages. The revision must explicitly map these IDs before sending.</summary>
public sealed class WiredVariableFxComposer : IServerPacket
{
    public const uint ConfigsId = 9473;
    public const uint RemovedConfigsId = 9474;
    public const uint StatusesId = 9475;
    public const uint RemovedStatusesId = 9476;
    private readonly WiredVariableFxBatch _batch;
    private WiredVariableFxComposer(uint id, WiredVariableFxBatch batch)
    {
        MessageId = id;
        _batch = batch;
    }
    public uint MessageId
    {
        get;
    }
    public static IReadOnlyList<WiredVariableFxComposer> ComposeBatch(WiredVariableFxBatch batch)
    {
        var result = new List<WiredVariableFxComposer>();

        if (batch.RemovedConfigs.Count > 0)
        {
            result.Add(new(RemovedConfigsId, batch));
        }

        if (batch.Configs.Count > 0)
        {
            result.Add(new(ConfigsId, batch));
        }

        if (batch.RemovedStatuses.Count > 0)
        {
            result.Add(new(RemovedStatusesId, batch));
        }

        if (batch.Statuses.Count > 0)
        {
            result.Add(new(StatusesId, batch));
        }

        return result;
    }
    public void Compose(IOutgoingPacket packet)
    {
        switch (MessageId)
        {
            case ConfigsId:
                packet.WriteInteger(_batch.Configs.Count);

                foreach (var config in _batch.Configs)
                {
                    packet.WriteInteger(config.Id);
                    packet.WriteBoolean(config.UserFx);
                    packet.WriteInteger(config.ShowMode);
                    packet.WriteInteger(0);
                    packet.WriteBoolean(false);
                    packet.WriteInteger(config.DurationMs);
                    packet.WriteInteger(config.Category);
                    packet.WriteInteger(config.StyleId);
                    packet.WriteInteger(config.ColorId);
                    packet.WriteInteger(config.WidthId);
                    packet.WriteInteger(config.RendererId);
                    WriteLong(packet, config.Min);
                    WriteLong(packet, config.Max);
                    WriteExtra(packet, config.Extra);
                }

                break;
            case RemovedConfigsId:
                packet.WriteInteger(_batch.RemovedConfigs.Count);

                foreach (var id in _batch.RemovedConfigs)
                {
                    packet.WriteInteger(id);
                }

                break;
            case StatusesId:
                packet.WriteBoolean(_batch.InitializeAll);
                packet.WriteInteger(_batch.Statuses.Count);

                foreach (var status in _batch.Statuses)
                {
                    packet.WriteString($"{status.Key.ConfigId}|{status.Key.VariableId}");
                    packet.WriteBoolean(status.Initialize);
                    packet.WriteBoolean(status.Key.UserEntity);
                    packet.WriteInteger(status.Key.EntityId);
                    WriteLong(packet, status.Value);
                    packet.WriteBoolean(status.Min is not null && status.Max is not null);

                    if (status.Min is long min && status.Max is long max)
                    {
                        WriteLong(packet, min);
                        WriteLong(packet, max);
                    }

                    WriteExtra(packet, status.Extra);
                }

                break;
            case RemovedStatusesId:
                packet.WriteInteger(_batch.RemovedStatuses.Count);

                foreach (var key in _batch.RemovedStatuses)
                {
                    packet.WriteString(key.ToString());
                }

                break;
        }
    }
    private static void WriteLong(IOutgoingPacket packet, long value)
    {
        packet.WriteInteger(unchecked((int)(value >> 32)));
        packet.WriteInteger(unchecked((int)value));
    }
    private static void WriteExtra(IOutgoingPacket packet, IReadOnlyDictionary<string, string> extra)
    {
        packet.WriteInteger(extra.Count);

        foreach (var (key, value) in extra)
        {
            packet.WriteString(key);
            packet.WriteString(value);
        }
    }
}
