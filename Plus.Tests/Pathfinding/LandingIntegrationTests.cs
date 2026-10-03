using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void CommitServiceRunsEffectLandingWhileMvIsStillVisible()
    {
        var item = Add(10, 1, 1, type: InteractionType.Effect);
        item.Definition.EffectId = 17; InitializeNativeState(item);
        var actor = ExecutorActor(0, 1); InitializeClientEffects();
        actor.MoveTo(1, 1); ExecutorTick();
        Assert.Equal(0, _client.GetHabbo().Effects.CurrentEffect);
        ExecutorTick();
        Assert.Equal(17, _client.GetHabbo().Effects.CurrentEffect);
        Assert.Equal("1", item.LegacyDataString);
        Assert.False(actor.HasStatus("mv"));
    }

    [Fact]
    public void AnnounceServiceAppliesTheTargetFloorEffect()
    {
        var actor = ExecutorActor(0, 1); InitializeClientEffects();
        _room.GetGameMap().EffectMap[1, 1] = 3;
        _client.GetHabbo().Gender = "M";
        actor.MoveTo(1, 1); ExecutorTick();
        Assert.True(actor.HasStatus("mv"));
        Assert.Equal(38, _client.GetHabbo().Effects.CurrentEffect);
    }
    [Fact]
    public void AnnounceServiceAppliesTheTargetSwimEffect()
    {
        var actor = ExecutorActor(0, 1); InitializeClientEffects();
        _room.GetGameMap().EffectMap[1, 1] = 1;
        actor.MoveTo(1, 1); ExecutorTick();
        Assert.True(actor.HasStatus("mv"));
        Assert.Equal(29, _client.GetHabbo().Effects.CurrentEffect);
    }
    private void InitializeClientEffects()
    {
        var habbo = _client.GetHabbo();
        habbo.Client = _client;
        habbo.Effects.Init(habbo);
    }
}
