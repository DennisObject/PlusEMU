using Plus.Communication.Packets.Outgoing.Rooms.Furni.Moodlight;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Data.Moodlight;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items;

public sealed record MoodlightPresetUpdate(int Preset, string ColorCode, int Intensity, int BackgroundMode);

public interface IMoodlightService
{
    void ShowConfig(Room room, GameClient session);
    void Toggle(Room room, GameClient session);
    void UpdatePreset(Room room, GameClient session, MoodlightPresetUpdate request);
}

/// <summary>Moodlight rights, validation, persistence and publication; every change is persisted before live state or the client sees it.</summary>
public sealed class MoodlightService(IRoomItemMetadataStore store) : IMoodlightService
{
    public void ShowConfig(Room room, GameClient session)
    {
        if (!Authorized(room, session)) return;
        if (room.MoodlightData == null)
        {
            // Every wall moodlight is loaded in list order, so the last one wins, as the original scan did.
            foreach (var item in room.GetRoomItemHandler().GetWall.ToList())
            {
                if (!item.IsTemporary && item.Definition.InteractionType == InteractionType.Moodlight && Load(item.Id) is { } loaded)
                    room.MoodlightData = loaded;
            }
        }
        if (room.MoodlightData == null || ActiveMoodlight(room) == null) return;
        session.Send(new MoodlightConfigComposer(MoodlightConfigSnapshot.Capture(room.MoodlightData)));
    }

    public void Toggle(Room room, GameClient session)
    {
        if (!Authorized(room, session) || room.MoodlightData == null) return;
        var item = ActiveMoodlight(room);
        if (item == null) return;
        var data = room.MoodlightData;
        var enabled = !data.Enabled;
        var prepared = MoodlightData.Serialize(enabled, data.CurrentPreset, data.GetPreset(data.CurrentPreset));
        store.SetMoodlightEnabled(item.Id, room.Id, enabled);
        data.Enabled = enabled;
        item.LegacyDataString = prepared;
        item.UpdateState(false, true);
    }

    public void UpdatePreset(Room room, GameClient session, MoodlightPresetUpdate request)
    {
        if (!Authorized(room, session) || room.MoodlightData == null) return;
        var item = ActiveMoodlight(room);
        if (item == null) return;
        // Presets are 1..3 only; colours and intensities outside the accepted sets are refused before anything is written.
        if (request.Preset is < 1 or > 3) return;
        if (!MoodlightData.IsValidColor(request.ColorCode) || !MoodlightData.IsValidIntensity(request.Intensity)) return;
        var selected = new MoodlightPreset(request.ColorCode, request.Intensity, request.BackgroundMode >= 2);
        var data = room.MoodlightData;
        if (data.Presets.Count < request.Preset || data.Presets[request.Preset - 1] is not { } target) return;
        var prepared = MoodlightData.Serialize(true, request.Preset, selected);
        store.UpdateMoodlightPreset(item.Id, room.Id, request.Preset,
            $"{selected.ColorCode},{selected.ColorIntensity},{(selected.BackgroundOnly ? 1 : 0)}");
        data.Enabled = true;
        data.CurrentPreset = request.Preset;
        target.ColorCode = selected.ColorCode;
        target.ColorIntensity = selected.ColorIntensity;
        target.BackgroundOnly = selected.BackgroundOnly;
        item.LegacyDataString = prepared;
        item.UpdateState(false, true);
    }

    private MoodlightData? Load(uint itemId) => store.LoadMoodlight(itemId) is { } record ? new MoodlightData(itemId, record) : null;

    private static bool Authorized(Room room, GameClient session) =>
        ReferenceEquals(session.GetHabbo().CurrentRoom, room) && room.CheckRights(session, true);

    private static Item? ActiveMoodlight(Room room)
    {
        var item = room.GetRoomItemHandler().GetItem(room.MoodlightData!.ItemId);
        return item == null || item.IsTemporary || item.Definition.InteractionType != InteractionType.Moodlight ? null : item;
    }
}
