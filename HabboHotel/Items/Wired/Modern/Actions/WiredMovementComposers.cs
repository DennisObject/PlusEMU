using System.Globalization;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Active Octane ABI: WiredMovementsParser, furniture type 1 / avatar type 0.</summary>
public sealed record WiredMovementComposer(int Type, int Id, int FromX, int FromY, double FromZ,
    int ToX, int ToY, double ToZ, int BodyRotation, int HeadRotation, int DurationMs) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredMovementsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1); packet.WriteInteger(Type);
        packet.WriteInteger(FromX); packet.WriteInteger(FromY); packet.WriteInteger(ToX); packet.WriteInteger(ToY);
        packet.WriteString(FromZ.ToString(CultureInfo.InvariantCulture)); packet.WriteString(ToZ.ToString(CultureInfo.InvariantCulture));
        packet.WriteInteger(Id);
        if (Type == 1)
        {
            packet.WriteInteger(BodyRotation); packet.WriteInteger(DurationMs);
            packet.WriteInteger(0); packet.WriteInteger(0); packet.WriteInteger(0);
        }
        else
        {
            packet.WriteInteger(1); packet.WriteInteger(BodyRotation); packet.WriteInteger(HeadRotation); packet.WriteInteger(DurationMs);
        }
    }
}

public sealed record WiredMoveStyleComposer(int Id, int Style, int Intensity, int Overshoot, bool Avatar = false) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredFurniMoveStyleComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1); packet.WriteInteger(Id); packet.WriteInteger(Style); packet.WriteInteger(Intensity);
        packet.WriteInteger(Overshoot); packet.WriteInteger(Avatar ? 1 : 0);
    }
}

public sealed record WiredClickSettingsComposer(int UserOption, int FurniOption) : IServerPacket
{
    // Internal ID differs from the active wire ID (2288 is already the old trading packet).
    public uint MessageId => ServerPacketHeader.WiredClickSettingsComposer;
    public void Compose(IOutgoingPacket packet) { packet.WriteInteger(UserOption); packet.WriteInteger(FurniOption); }
}

/// <summary>Active Octane chat parser includes the colour/prefix extension and optional bubble width.</summary>
public sealed record WiredChatComposer(int VirtualId, string Message, int BubbleStyle, int BubbleWidth, bool Private) : IServerPacket
{
    public uint MessageId => Private ? ServerPacketHeader.WhisperComposer : ServerPacketHeader.ChatComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(VirtualId); packet.WriteString(Message); packet.WriteInteger(0); packet.WriteInteger(BubbleStyle);
        packet.WriteInteger(0); packet.WriteString(""); packet.WriteInteger(Message.Length);
        for (var index = 0; index < 6; index++) packet.WriteString("");
        packet.WriteString("icon-prefix-name"); packet.WriteInteger(BubbleWidth);
    }
}
