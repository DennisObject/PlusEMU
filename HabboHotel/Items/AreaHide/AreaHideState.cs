using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.AreaHide;

public sealed record AreaHideValues(ImmutableArray<int> Values)
{
    public int this[int index] => Values[index];
    public bool On => Values[0] == 1;
}

// The client hides the area. The room owns and persists its eight NumberData settings.
public static class AreaHideState
{
    public const int ControllerMaxSize = 20; // Pinned Turbo RoomConfig.AreaHideMaxSize default.
    private static readonly ConditionalWeakTable<Item, object> Gates = new();
    private static readonly string[] Fields = ["state", "rootX", "rootY", "width", "length", "invisibility", "wallItems", "invert"];
    public static bool IsAreaHide(Item item) => item.Definition.InteractionType == InteractionType.AreaHide;
    public static IntArrayDataFormat Load(string stored)
    {
        var values = stored.Length == 0 ? [0] : stored.Split('\n').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();

        if (values.Length > 8) {
            throw new FormatException("Invalid area-hide NumberData.");
        }

        return new() { Data = values.Concat(Enumerable.Repeat(0, 8 - values.Length)).ToList() };
    }
    public static bool TryRead(Item item, out AreaHideValues state)
    {
        state = null!;

        if (!IsAreaHide(item) || item.ExtraData is not IntArrayDataFormat data || data.Data.Count != 8) {
            return false;
        }

        state = new(data.Data.ToImmutableArray());

        return true;
    }
    public static bool Configure(Room room, GameClient client, Item item, IReadOnlyList<string> fields)
    {
        if (fields.Count != 16) {
            return false;
        }

        var values = new int[8];
        var seen = new HashSet<int>();

        for (var i = 0; i < fields.Count; i += 2) {
            var index = Array.IndexOf(Fields, fields[i]);

            if (index < 0 || !seen.Add(index) || !int.TryParse(fields[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[index])) {
                return false;
            }
        }

        if (values[0] is not (0 or 1) || values[3] is < 1 or > ControllerMaxSize || values[4] is < 1 or > ControllerMaxSize
            || values[5] is not (0 or 1) || values[6] is not (0 or 1) || values[7] is not (0 or 1)
            || !room.GetGameMap().ValidTile(values[1], values[2])) {
            return false;
        }

        lock (Gates.GetValue(item, _ => new())) {
            if (!ReferenceEquals(client.GetHabbo().CurrentRoom, room) || !room.CheckRights(client) || !Attached(room, item)) {
                return false;
            }

            return Commit(room, item, values);
        }
    }
    // Smart indexed variables use source Min=0/Max=int.MaxValue, flags Max=1; controller rectangle bounds are separate.
    public static bool Set(Room room, Item item, int index, int value)
    {
        if (index is < 0 or > 7 || value < 0 || index is 0 or 5 or 6 or 7 && value > 1) {
            return false;
        }

        lock (Gates.GetValue(item, _ => new())) {
            if (!Attached(room, item) || !TryRead(item, out var before)) {
                return false;
            }

            var values = before.Values.ToArray();
            values[index] = value;

            return Commit(room, item, values);
        }
    }
    public static void Toggle(Room room, Item item)
    {
        lock (Gates.GetValue(item, _ => new())) {
            if (Attached(room, item) && TryRead(item, out var state)) {
                Set(room, item, 0, state.On ? 0 : 1);
            }
        }
    }
    private static bool Attached(Room room, Item item) => !item.IsTemporary && item.RoomId == room.Id
        && ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item);
    private static bool Commit(Room room, Item item, int[] values)
    {
        if (!room.GetRoomItemHandler().PersistFurnitureData(item, new IntArrayDataFormat { Data = values.ToList() })) {
            return false;
        }

        Announce(room, item);

        return true;
    }
    public static void Announce(Room room, Item item, bool removed = false)
    {
        if (TryRead(item, out var state)) {
            room.SendPacket(new AreaHideComposer(item.Id, state, removed));
        }
    }
    public static void SendSnapshot(GameClient client, IEnumerable<Item> items)
    {
        foreach (var item in items) {
            if (TryRead(item, out var state)) {
                client.Send(new AreaHideComposer(item.Id, state));
            }
        }
    }
}
