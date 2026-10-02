namespace Plus.HabboHotel.Items.Wired.Variables;

public sealed partial class WiredRoomVariables
{
    // The room adapter can provide its configured wired timezone; UTC is the upstream default.
    public Func<TimeZoneInfo> TimeZone { get; set; } = () => TimeZoneInfo.Utc;
    private WiredVariableMetadataBox? DerivedMetadataOn(uint definitionId) =>
        new[] { "wf_xtra_var_lvlup_system", "wf_var_quest", "wf_var_quest_chain" }.Select(name => MetadataOn(definitionId, name))
            .OfType<WiredVariableMetadataBox>().OrderBy(box => box.Item.GetZ).ThenBy(box => box.Item.Id).FirstOrDefault();

    private IEnumerable<WiredVariableDescription> DerivedDescriptions(WiredVariableDescription source)
    {
        var definition = source.Definition;
        if (definition.Target == WiredVariableTarget.Context) yield break;
        if (source.HasValue && DerivedMetadataOn(definition.ItemId) is { } level)
            foreach (var sub in Enumerable.Range(0, level.DerivedKeys.Length).Where(level.HasDerived))
                if (SyntheticId(definition.Target, definition.ItemId, sub, false) is { } id && _room.GetRoomItemHandler().GetItem(id) is null)
                    yield return new(definition with { ItemId = id, Name = definition.Name + "." + level.DerivedKeys[sub] }, true, true) { IsDerived = true };
        if (MetadataOn(definition.ItemId, "wf_xtra_var_time_util")?.TimeUtilities is { } time && (time.Mode == 0 ? source.HasValue : source.CanReadTimestamps))
            foreach (var sub in time.Selected)
                if (SyntheticId(definition.Target, definition.ItemId, sub, true) is { } id && _room.GetRoomItemHandler().GetItem(id) is null)
                    yield return new(definition with { ItemId = id, Name = definition.Name + "." + WiredVariableTimeUtilities.Key(sub) }, true, true) { IsDerived = true };
    }

    private WiredVariableDerivation? ResolveDerived(WiredVariableReference reference)
    {
        if (!WiredVariableModule.TryDefinitionId(reference.Token, out var id) || reference.Target == WiredVariableTarget.Context
            || !TryDecode(reference.Target, id, out var baseId, out var sub, out var isTime)
            || !_definitions.TryGetValue(baseId, out var definition) || !definition.HasPersistedConfiguration
            || _room.GetRoomItemHandler().GetItem(id) is not null) return null;
        var source = new WiredVariableReference(reference.Target, $"custom:{baseId}");
        if (isTime)
        {
            var time = MetadataOn(baseId, "wf_xtra_var_time_util")?.TimeUtilities;
            if (time is null || !time.Has(sub)) return null;
            var zone = TimeZone();
            return new(source, value => time.Read(value, sub, zone) is { } result ? new(result, 0, 0) : null, time.Mode == 0, time.Mode != 0);
        }
        var level = DerivedMetadataOn(baseId);
        return level is not null && level.HasDerived(sub)
            ? new(source, value => new(level.ReadDerived(value.Value, sub), 0, 0)) : null;
    }

    public static uint? SyntheticId(WiredVariableTarget target, uint baseId, int sub, bool time)
    {
        if (target is not (WiredVariableTarget.User or WiredVariableTarget.Furni or WiredVariableTarget.Global)
            || baseId is 0 or >= 6250000 || (time ? sub is < 1 or > 26 : sub is < 0 or > 7)) return null;
        return Offset(target, time) + baseId * (time ? 32u : 16u) + (uint)(time ? sub : sub + 1);
    }
    private static uint Offset(WiredVariableTarget target, bool time) => (target, time) switch
    {
        (WiredVariableTarget.Furni, false) => 800000000, (WiredVariableTarget.Global, false) => 900000000,
        (WiredVariableTarget.Furni, true) => 1700000000, (WiredVariableTarget.Global, true) => 1900000000,
        (_, true) => 1500000000, _ => 700000000
    };
    private static bool TryDecode(WiredVariableTarget target, uint id, out uint baseId, out int sub, out bool time)
    {
        time = id >= 1500000000; baseId = 0; sub = 0;
        var offset = Offset(target, time); if (id < offset) return false;
        var local = id - offset; var stride = time ? 32u : 16u;
        baseId = local / stride; sub = (int)(local % stride) - (time ? 0 : 1);
        return SyntheticId(target, baseId, sub, time) == id;
    }
}
