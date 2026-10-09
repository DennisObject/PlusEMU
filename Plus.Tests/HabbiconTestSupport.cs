using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;
using Plus.HabboHotel.Users;

namespace Plus.Tests;

internal static class HabbiconTestSupport
{
    internal static Revision InternalRevision() => new()
    {
        ZeroHeaderIsValid = true,
        IncomingHeaders = typeof(ClientPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(field => field.Name, field => (uint)field.GetRawConstantValue()!),
        OutgoingHeaders = typeof(ServerPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(field => field.Name, field => (uint)field.GetRawConstantValue()!)
    };

    public static (FlashGameClient Client, List<(uint Header, byte[] Payload)> Sent) Client(Habbo habbo)
    {
        var sent = new List<(uint, byte[])>();
        var headers = typeof(ServerPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (uint)field.GetRawConstantValue()!).Where(id => id > 0).ToDictionary(id => id, id => id);
        var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
        {
            Revision = new Revision { InternalIdToOutgoingIdMapping = headers },
            SendCallback = args =>
            {
                var bytes = args.MemoryBuffer.ToArray();
                sent.Add((BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)), bytes[6..]));

                return true;
            }
        };
        client.SetHabbo(habbo);

        return (client, sent);
    }

    public static FlashIncomingPacket Incoming(params object[] values)
    {
        using var stream = new MemoryStream();

        foreach (var value in values) {
            if (value is int number) {
                var bytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(bytes, number);
                stream.Write(bytes);
            }
            else if (value is bool flag) {
                stream.WriteByte(flag ? (byte)1 : (byte)0);
            }
            else if (value is string text) {
                var bytes = Encoding.UTF8.GetBytes(text);
                var length = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)bytes.Length));
                stream.Write(length);
                stream.Write(bytes);
            }
        }

        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    public static HabbiconSnapshot Snapshot()
    {
        var member = new HabbiconItem(61, "toast_toast", 6, HabbiconState.Favorite, 5, 2, 5);
        var reward = new HabbiconItem(71, "toast_fine", 6, HabbiconState.Claimable, 0, 0, 0);
        var missing = new HabbiconItem(62, "toast_happy", 6, HabbiconState.NotOwned, 5, 0, 0);

        return new(new[] { new HabbiconCollection(6, "toast", false, 71, 1, 40, 0, 0, new[] { member, missing }) },
            new Dictionary<int, HabbiconItem> { [61] = member, [71] = reward, [62] = missing }, new[] { 61, 28 }, new[] { 61, 71 });
    }

    internal sealed class Service : IHabbiconService
    {
        public HabbiconSnapshot Data { get; set; } = Snapshot();
        public List<(HabbiconAction Action, int Id)> Actions { get; } = new();
        public List<int[]> Clears { get; } = new();
        public List<int> Used { get; } = new();
        public int? Rejection { get; set; }
        public HabbiconSnapshot Load(int userId) => Data;
        public HabbiconChange Change(Habbo habbo, HabbiconAction action, int id)
        {
            Actions.Add((action, id));

            if (Rejection is { } code) {
                throw new HabbiconRejected((HabbiconActionError)code);
            }

            return new(Data, new[] { Data.RequireItem(61) }, null);
        }
        public HabbiconChange BuyCatalog(Habbo habbo, int id, int credits, int duckets, int diamonds) => throw new NotImplementedException();
        public bool Use(int userId, int id)
        {
            Used.Add(id);

            return Data.Items.TryGetValue(id, out var item) && item.Owned;
        }
        public void ClearUnseen(int userId, IReadOnlyList<int> ids) => Clears.Add(ids.ToArray());
    }

    internal sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = new();
        public int MessageId { get; set; }
        public ReadOnlyMemory<byte> Buffer => ReadOnlyMemory<byte>.Empty;
        public void WriteByte(byte value) => Writes.Add(value);
        public void WriteShort(short value) => Writes.Add(value);
        public void WriteInt(int value) => Writes.Add(value);
        public void WriteInteger(int value) => Writes.Add(value);
        public void WriteUInt(uint value) => Writes.Add(value);
        public void WriteUInteger(uint value) => Writes.Add(value);
        public void WriteBool(bool value) => Writes.Add(value);
        public void WriteBoolean(bool value) => Writes.Add(value);
        public void WriteString(string value) => Writes.Add(value ?? "");
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
