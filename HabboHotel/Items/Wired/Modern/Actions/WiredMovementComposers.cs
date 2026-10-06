using System.Globalization;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Active Octane ABI: WiredMovementsParser, furniture type 1 / avatar type 0.</summary>
public sealed record WiredMovementComposer(int Type, int Id, int FromX, int FromY, double FromZ,
    int ToX, int ToY, double ToZ, int BodyRotation, int HeadRotation, int DurationMs) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredMovementsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        packet.WriteInteger(Type);
        packet.WriteInteger(FromX);
        packet.WriteInteger(FromY);
        packet.WriteInteger(ToX);
        packet.WriteInteger(ToY);
        packet.WriteString(FromZ.ToString(CultureInfo.InvariantCulture));
        packet.WriteString(ToZ.ToString(CultureInfo.InvariantCulture));
        packet.WriteInteger(Id);

        if (Type == 1)
        {
            packet.WriteInteger(BodyRotation);
            packet.WriteInteger(DurationMs);
            packet.WriteInteger(0);
            packet.WriteInteger(0);
            packet.WriteInteger(0);
        }
        else
        {
            packet.WriteInteger(1);
            packet.WriteInteger(BodyRotation);
            packet.WriteInteger(HeadRotation);
            packet.WriteInteger(DurationMs);
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
