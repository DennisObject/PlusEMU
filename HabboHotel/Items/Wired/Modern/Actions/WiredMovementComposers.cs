using System.Globalization;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Canonical Wired movement body, furniture type 1 / avatar type 0.</summary>
public sealed record WiredMovementComposer(int Type, int Id, int FromX, int FromY, double FromZ,
    int ToX, int ToY, double ToZ, int BodyRotation, int HeadRotation, int DurationMs) : IServerPacket
{
    public int AnimationType { get; init; } = 1;
    public int? JumpPower { get; init; }
    public int? OvershootTimeMs { get; init; }
    public int? CurveStrength { get; init; }

    public uint MessageId => ServerPacketHeader.WiredMovementsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        WriteEntry(packet);
    }

    internal void WriteEntry(IOutgoingPacket packet)
    {
        packet.WriteInteger(Type);
        packet.WriteInteger(FromX);
        packet.WriteInteger(FromY);
        packet.WriteInteger(ToX);
        packet.WriteInteger(ToY);
        packet.WriteString(FromZ.ToString(CultureInfo.InvariantCulture));
        packet.WriteString(ToZ.ToString(CultureInfo.InvariantCulture));
        packet.WriteInteger(Id);

        if (Type == 1) {
            packet.WriteInteger(DurationMs);
            packet.WriteInteger(BodyRotation);
            packet.WriteBoolean(OvershootTimeMs.HasValue);

            if (OvershootTimeMs is { } overshoot) {
                packet.WriteInteger(overshoot);
            }

            packet.WriteBoolean(CurveStrength.HasValue);

            if (CurveStrength is { } curve) {
                packet.WriteInteger(curve);
            }
        }
        else {
            packet.WriteInteger(AnimationType);
            packet.WriteInteger(DurationMs);
            packet.WriteInteger(BodyRotation);
            packet.WriteInteger(HeadRotation);
            packet.WriteBoolean(JumpPower.HasValue);

            if (JumpPower is { } jump) {
                packet.WriteInteger(jump);
            }
        }
    }
}

internal sealed record WiredMovementBatchComposer(IReadOnlyList<WiredMovementComposer> Movements) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredMovementsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(Movements.Count);

        foreach (var movement in Movements) {
            movement.WriteEntry(packet);
        }
    }
}

public sealed record WiredMoveStyleComposer(int Id, int Style, int Intensity, int Overshoot, bool Avatar = false) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredFurniMoveStyleComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        packet.WriteInteger(Id);
        packet.WriteInteger(Style);
        packet.WriteInteger(Intensity);
        packet.WriteInteger(Overshoot);
        packet.WriteInteger(Avatar ? 1 : 0);
    }
}

public sealed record WiredWallMovementComposer(int Id, WiredWallSnapshot From, WiredWallSnapshot To, int DurationMs) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredMovementsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        packet.WriteInteger(2);
        packet.WriteInteger(Id);
        packet.WriteBoolean(!To.Left);
        packet.WriteInteger(From.TileX);
        packet.WriteInteger(From.TileY);
        packet.WriteInteger(From.LocalX);
        packet.WriteInteger(From.PixelY);
        packet.WriteInteger(To.TileX);
        packet.WriteInteger(To.TileY);
        packet.WriteInteger(To.LocalX);
        packet.WriteInteger(To.PixelY);
        packet.WriteInteger(DurationMs);
    }
}

public sealed record WiredClickSettingsComposer(int UserOption, int FurniOption) : IServerPacket
{
    // Internal ID differs from the active wire ID (2288 is already the old trading packet).
    public uint MessageId => ServerPacketHeader.WiredClickSettingsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(UserOption);
        packet.WriteInteger(FurniOption);
    }
}

/// <summary>The official chat packet with the wired bubble width as its optional trailing int.</summary>
public sealed record WiredChatComposer(int VirtualId, string Message, int BubbleStyle, int BubbleWidth, bool Private, bool Shout = false) : IServerPacket
{
    public uint MessageId => Private ? ServerPacketHeader.WhisperComposer : Shout ? ServerPacketHeader.ShoutComposer : ServerPacketHeader.ChatComposer;
    public void Compose(IOutgoingPacket packet)
    {
        RoomChatPacket.Write(packet, VirtualId, Message, 0, BubbleStyle);
        packet.WriteInteger(BubbleWidth);
    }
}
