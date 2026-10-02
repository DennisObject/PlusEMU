using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Xunit;
using static Plus.Tests.WiredSelectorTests;

namespace Plus.Tests;

public sealed class WiredAddonTests
{
    private static WiredAddonInputs Input(long now = 0) => new(World(), Inputs(), now);

    [Fact]
    public void PolicyAddonsDoNotConsumeActionHistoryUntilTheEnginePicksAndResetRestartsIt()
    {
        var unseen = new WiredAddonModule("wf_xtra_unseen", Config());
        var first = new WiredAddonPolicy();
        unseen.Apply(Input(), first);
        var second = new WiredAddonPolicy();
        unseen.Apply(Input(), second);
        Assert.Equal(1u, Assert.Single(second.ActionPicker!.Pick([1, 2])));
        Assert.Equal(2u, Assert.Single(first.ActionPicker!.Pick([1, 2])));
        unseen.Reset();
        Assert.Equal(1u, Assert.Single(first.ActionPicker.Pick([1, 2])));
        new WiredAddonModule("wf_xtra_exec_in_order", Config()).Apply(Input(), first);
        new WiredAddonModule("wf_xtra_mov_no_animation", Config()).Apply(Input(), first);
        Assert.True(first.ExecuteInOrder);
        Assert.True(first.DisableAnimation);
    }

    [Fact]
    public void FurnitureTextNamesPreserveTheSavedSelectionOrderAndCustomSeparator()
    {
        var policy = new WiredAddonPolicy();
        new WiredAddonModule("wf_xtra_text_output_furni_name", Config([2, 100], [3, 2], "items\t;")).Apply(Input(), policy);
        Assert.Equal("Lamp;High chair", policy.FormatText(Input(), "$(items)"));
    }

    [Fact]
    public void ExecutionLimitUsesSlidingMillisecondsAndResetsOnLifecycleChange()
    {
        var addon = new WiredAddonModule("wf_xtra_execution_limit", Config([2, 1250]));
        Assert.Equal(1500, addon.Configuration.IntParams[1]);
        Assert.True(addon.Apply(Input(0), new()));
        Assert.True(addon.Apply(Input(500), new()));
        Assert.False(addon.Apply(Input(1499), new()));
        Assert.True(addon.Apply(Input(1500), new()));
        Assert.False(addon.Apply(Input(1999), new()));
        Assert.True(addon.Apply(Input(2000), new()));
        addon.Reset();
        Assert.True(addon.Apply(Input(2000), new()));
        addon.Configure(Config([1, 1000]));
        Assert.True(addon.Apply(Input(2000), new()));
        Assert.False(addon.Apply(Input(2001), new()));
    }

    [Fact]
    public void RandomPickerAvoidsRecentFiringIdsAndUnseenUsesIdsAcrossReordering()
    {
        var random = new WiredRandomActionPicker(1, 2, new Random(7));
        var first = Assert.Single(random.Pick([1, 2, 3]));
        var second = Assert.Single(random.Pick([1, 2, 3]));
        var third = Assert.Single(random.Pick([1, 2, 3]));
        Assert.Equal(3, new[] { first, second, third }.Distinct().Count());
        Assert.Single(random.Pick([1]));
        var unseen = new WiredUnseenActionPicker();
        Assert.Equal(10u, Assert.Single(unseen.Pick([10, 20])));
        Assert.Equal(20u, Assert.Single(unseen.Pick([20, 10, 30])));
    }

    [Fact]
    public void UnseenResetAndCycleOperateOnTheCurrentCandidates()
    {
        var unseen = new WiredUnseenActionPicker();
        Assert.Equal(10u, Assert.Single(unseen.Pick([10, 20])));
        Assert.Equal(20u, Assert.Single(unseen.Pick([20, 10])));
        Assert.Equal(20u, Assert.Single(unseen.Pick([20, 10])));
        unseen.Reset();
        Assert.Equal(10u, Assert.Single(unseen.Pick([10, 20])));
    }

    [Fact]
    public void QuantityFiltersPreserveUnconfiguredSelectionsAndUseSmallestPositiveLimit()
    {
        var selection = new WiredSelectedIds();
        selection.FurniIds.UnionWith([1, 2, 3]);
        selection.UserIds.Add(1);
        var policy = new WiredAddonPolicy();
        new WiredAddonModule("wf_xtra_filter_furni", Config([0])).Apply(Input(), policy);
        Assert.Equal(3, policy.FilterSelection(selection, new Random(2)).FurniIds.Count);
        new WiredAddonModule("wf_xtra_filter_furni", Config([2])).Apply(Input(), policy);
        new WiredAddonModule("wf_xtra_filter_furni", Config([1])).Apply(Input(), policy);
        var filtered = policy.FilterSelection(selection, new Random(2));
        Assert.Single(filtered.FurniIds);
        Assert.Single(filtered.UserIds);
        Assert.Equal(3, selection.FurniIds.Count);
    }

    [Fact]
    public void TextFormattersUseActionTimeSourcesAndRespectOrderAndExplicitIntegers()
    {
        var policy = new WiredAddonPolicy();
        new WiredAddonModule("wf_xtra_text_output_username", Config([2, 200], text: "$(names)\t / ")).Apply(Input(), policy);
        var current = Input();
        current.Selection.SelectorPool.UserIds.UnionWith([1, 4]);
        Assert.Equal("Ada / Ana!", policy.FormatText(current, "$(names)!"));
        policy.TextFormatters.Add((_, text) => text.Replace("Ada", "Reader"));
        Assert.Equal("Reader / Ana", policy.FormatText(current, "$(names)"));
        Assert.Equal(50, new WiredAddonModule("wf_xtra_anim_time", Config([0], text: "1000")).Configuration.IntParams[0]);
        Assert.Equal(1000, new WiredAddonModule("wf_xtra_anim_time", Config(text: "1000")).Configuration.IntParams[0]);
    }

    [Theory]
    [InlineData(0, 2, 2, true)]
    [InlineData(1, 0, 2, false)]
    [InlineData(2, 0, 2, false)]
    [InlineData(2, 1, 2, true)]
    [InlineData(2, 2, 2, false)]
    [InlineData(3, 0, 2, true)]
    [InlineData(4, 0, 2, true)]
    [InlineData(5, 1, 2, true)]
    [InlineData(6, 2, 2, true)]
    public void ConditionModesCountLogicalRequirements(int mode, int matched, int total, bool expected) =>
        Assert.Equal(expected, WiredConditionPolicyEvaluator.Matches((WiredConditionEvaluation)mode, matched, total, 1));

    [Fact]
    public void MovementScopesAreResolvedFromTheirSeparateSourceFields()
    {
        var policy = new WiredAddonPolicy();
        new WiredAddonModule("wf_xtra_mov_carry_users", Config([1, 201])).Apply(Input(), policy);
        new WiredAddonModule("wf_xtra_mov_physics", Config([1, 1, 1, 1, 100, 201, 201], [2])).Apply(Input(), policy);
        Assert.Equal(new[] { 2 }, policy.Carry!.UserIds);
        Assert.Equal(new uint[] { 2 }, policy.Physics!.ThroughFurni);
        Assert.Equal(new uint[] { 3 }, policy.Physics.BlockingFurni);
        Assert.Equal(new[] { 2 }, policy.Physics.ThroughUsers);
        Assert.False(WiredMovementPolicy.IsBlocked(policy.Physics, [2], [2], true, true));
        Assert.True(WiredMovementPolicy.IsBlocked(policy.Physics, [3], [], false, false));
        Assert.True(WiredMovementPolicy.IsBlocked(policy.Physics, [], [1], false, true));
    }

    [Fact]
    public void JumpAndProjectileVariableAbsenceHaveDifferentFallbacksAndClampValues()
    {
        var input = Input() with { ReadVariable = _ => null };
        var policy = new WiredAddonPolicy { Curve = new(7, 100, 15) };
        new WiredAddonModule("wf_xtra_mov_curve", Config([7, 100, 80, 1], text: "custom:2")).Apply(input, policy);
        Assert.Equal(15, policy.Curve.Strength);
        var projectile = new int[24];
        projectile[14] = 2;
        projectile[15] = 1;
        new WiredAddonModule("wf_xtra_rotate_to_dir", Config(projectile, [1], "\tcustom:2")).Apply(input, policy);
        Assert.Equal(WiredProjectileDistance.Normal, policy.Projectile!.Distance);
        new WiredAddonModule("wf_xtra_mov_curve", Config([7, 100, 80, 1], text: "custom:2"))
            .Apply(input with { ReadVariable = _ => 999999 }, policy);
        Assert.Equal(1000, policy.Curve.Strength);
    }

    [Fact]
    public void MovementResolverScopesRotationCurveAndDistanceToPickedProjectiles()
    {
        var policy = new WiredAddonPolicy
        {
            AnimationTimeMs = 750, Curve = new(7, 100, 80),
            Projectile = new(new HashSet<uint> { 1 }, 0, 2, -50, WiredProjectileDistance.Fixed, 7),
            Physics = new(true, new HashSet<uint>(), new HashSet<int>(), new HashSet<uint>())
        };
        var one = WiredMovementPolicy.Resolve(policy, World().Furni[0], 3, 3, 10);
        Assert.Equal(5, one.Rotation);
        Assert.Equal(0, one.Height);
        Assert.Equal(80, one.CurveStrength);
        Assert.Equal(5, one.AnimationDistanceOffset);
        var two = WiredMovementPolicy.Resolve(policy, World().Furni[1], 3, 3, 10);
        Assert.Null(two.Rotation);
        Assert.Equal(80, two.CurveStrength);
        Assert.Equal(0, two.AnimationDistanceOffset);
        Assert.Equal(10, WiredMovementPolicy.Resolve(policy, World().Furni[0], 3, 3, 10, explicitHeight: true).Height);
        policy.Curve = null;
        var projectileOnly = WiredMovementPolicy.Resolve(policy, World().Furni[0], 100, 3, 10);
        Assert.Equal(-50, projectileOnly.CurveStrength);
        Assert.Equal(7, projectileOnly.CurveType);
        Assert.Equal(-92, projectileOnly.AnimationDistanceOffset);
        Assert.Equal(new[] { 1 }, WiredMovementPolicy.CarriedUsers(new(false, new HashSet<int> { 1, 4 }), World().Furni[0], World(), (id, _) => id == 1).Select(x => x.Id));
    }

    [Theory]
    [InlineData(0, 1, -1, 1)]
    [InlineData(1, 1, -10, 0)]
    [InlineData(2, 1, 1, 4)]
    [InlineData(3, 1, 1, 2)]
    public void ProjectileDirectionSystemsUseHabboAxesAndTieRules(int system, long x, long y, int expected) =>
        Assert.Equal(expected, WiredMovementPolicy.Direction(system, x, y));
}
