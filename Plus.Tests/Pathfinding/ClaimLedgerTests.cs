using System.Runtime.CompilerServices;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class ClaimLedgerTests
{
    [Fact]
    public void MembershipListsTrackStationaryWalkingAndTileCounts()
    {
        var ledger = new ClaimLedger(4, 4);
        var stationary = Actor();
        var walking = Actor();
        var first = ledger.Move(stationary, 1, 1, false, 10);
        var second = ledger.Move(walking, 1, 1, true, 20);
        Assert.Same(second, ledger.Head[1]);
        Assert.Same(first, second.Next);
        Assert.Null(first.Next);
        Assert.Equal(2, ledger.Count[1]);
        Assert.Equal(1, ledger.StationaryCount[1]);
        Assert.Equal(2, ledger.TileCount[1]);
        Assert.Equal(TargetOccupancy.Stationary | TargetOccupancy.Walking, ledger.Snapshot(0).Targets[1]);
        ledger.Remove(walking);
        Assert.Same(first, ledger.Head[1]);
        Assert.Equal(1, ledger.Count[1]);
        Assert.Equal(1, ledger.StationaryCount[1]);
        Assert.Equal(1, ledger.TileCount[1]);
    }

    [Fact]
    public void UpdatingWalkingStateAndMovingMembershipDoNotDuplicateActors()
    {
        var ledger = new ClaimLedger(4, 4);
        var actor = Actor();
        var member = ledger.Move(actor, 1, 1, false, 10);
        Assert.Same(member, ledger.Move(actor, 1, 1, true, 10));
        Assert.Equal(1, ledger.Count[1]);
        Assert.Equal(0, ledger.StationaryCount[1]);
        Assert.Equal(TargetOccupancy.Walking, ledger.Snapshot(0).Targets[1]);
        Assert.Same(member, ledger.Move(actor, 2, 2, false, 10));
        Assert.Null(ledger.Head[1]);
        Assert.Equal(0, ledger.Count[1]);
        Assert.Equal(0, ledger.TileCount[1]);
        Assert.Same(member, ledger.Head[2]);
        Assert.Equal(1, ledger.StationaryCount[2]);
        Assert.Equal(1, ledger.TileCount[2]);
        Assert.Equal(TargetOccupancy.Stationary, ledger.Snapshot(0).Targets[2]);
    }

    [Fact]
    public void RemovingInteriorAndTailMembersPreservesTheIntrusiveList()
    {
        var ledger = new ClaimLedger(4, 4);
        var tail = Actor();
        var middle = Actor();
        var head = Actor();
        var last = ledger.Move(tail, 1, 1, false, 10);
        ledger.Move(middle, 1, 1, false, 20);
        var first = ledger.Move(head, 1, 1, false, 30);
        ledger.Remove(middle);
        Assert.Same(last, first.Next);
        Assert.Equal(2, ledger.Count[1]);
        ledger.Remove(tail);
        Assert.Null(first.Next);
        Assert.Equal(1, ledger.Count[1]);
        ledger.Remove(head);
        ledger.Remove(head);
        Assert.Null(ledger.Head[1]);
        Assert.Equal(0, ledger.Count[1]);
        Assert.Equal(0, ledger.StationaryCount[1]);
        Assert.Equal(0, ledger.TileCount[1]);
    }

    [Fact]
    public void OffGraphActorsBlockTheTileAndMoveBetweenSeparateMembershipLists()
    {
        var ledger = new ClaimLedger(4, 4);
        var actor = Actor();
        var member = ledger.Move(actor, null, 2, false, 10);
        Assert.Same(member, ledger.OffGraphHead[2]);
        Assert.Null(ledger.Head[2]);
        Assert.Equal(0, ledger.Count[2]);
        Assert.Equal(1, ledger.TileCount[2]);
        Assert.Equal(TargetOccupancy.OffGraph, ledger.Snapshot(0).Targets[2]);
        ledger.Move(actor, 2, 2, true, 10);
        Assert.Null(ledger.OffGraphHead[2]);
        Assert.Same(member, ledger.Head[2]);
        Assert.Equal(1, ledger.TileCount[2]);
        Assert.Equal(TargetOccupancy.Walking, ledger.Snapshot(0).Targets[2]);
        ledger.Move(actor, null, 3, true, 10);
        Assert.Null(ledger.Head[2]);
        Assert.Equal(0, ledger.TileCount[2]);
        Assert.Same(member, ledger.OffGraphHead[3]);
        Assert.Equal(TargetOccupancy.OffGraph, ledger.Snapshot(0).Targets[3]);
    }

    [Fact]
    public void RiderGroupExclusionRemovesBothMembersAndTheirClaims()
    {
        var ledger = new ClaimLedger(4, 4);
        var rider = Actor();
        var horse = Actor();
        var other = Actor();
        ledger.Move(rider, 1, 1, true, 10);
        ledger.Move(horse, 1, 1, true, 10);
        ledger.Move(other, 1, 1, false, 20);
        Assert.True(ledger.TryClaim(rider, 2, ClaimKind.Shared, TargetOccupancy.None));
        Assert.True(ledger.TryClaim(horse, 2, ClaimKind.Shared, TargetOccupancy.None));
        var ownView = ledger.Snapshot(10);
        Assert.Equal(TargetOccupancy.Stationary, ownView.Targets[1]);
        Assert.Equal(TargetOccupancy.None, ownView.Targets[2]);
        var otherView = ledger.Snapshot(20);
        Assert.Equal(TargetOccupancy.Walking, otherView.Targets[1]);
        Assert.Equal(TargetOccupancy.SharedClaim, otherView.Targets[2]);
        Assert.Equal(3, ledger.Count[1]);
        Assert.Equal(3, ledger.TileCount[1]);
    }

    [Theory]
    [InlineData(ClaimKind.Exclusive, TargetOccupancy.ExclusiveClaim)]
    [InlineData(ClaimKind.Goal, TargetOccupancy.GoalClaim)]
    [InlineData(ClaimKind.Shared, TargetOccupancy.SharedClaim)]
    [InlineData(ClaimKind.Roller, TargetOccupancy.RollerClaim)]
    public void EveryClaimKindAppearsInOccupancyAndExcludesItsOwnGroup(ClaimKind kind, TargetOccupancy bit)
    {
        var ledger = new ClaimLedger(4, 4);
        var actor = Actor();
        ledger.Move(actor, 0, 0, true, 10);
        Assert.True(ledger.TryClaim(actor, 2, kind, TargetOccupancy.None));
        Assert.Equal(bit, ledger.Snapshot(0).Targets[2]);
        Assert.Equal(TargetOccupancy.None, ledger.Snapshot(10).Targets[2]);
        ledger.Release(actor);
        ledger.Release(actor);
        Assert.Equal(TargetOccupancy.None, ledger.Snapshot(0).Targets[2]);
    }

    [Theory]
    [InlineData(ClaimKind.Exclusive)]
    [InlineData(ClaimKind.Goal)]
    [InlineData(ClaimKind.Shared)]
    [InlineData(ClaimKind.Roller)]
    public void BlockingExecutionAndRollerRequestsRejectEveryForeignClaim(ClaimKind existing)
    {
        var (ledger, first, second) = TwoActors();
        Assert.True(ledger.TryClaim(first, 2, existing, TargetOccupancy.None));
        var normal = ClaimMatrix.BlockingMask(new(), NavFlags.Transit, StepPurpose.Transit, OccupancyView.Execution);
        var roller = ClaimMatrix.BlockingMask(new(), NavFlags.Transit, StepPurpose.Roller, OccupancyView.Execution);
        Assert.False(ledger.TryClaim(second, 2, ClaimKind.Exclusive, normal));
        Assert.False(ledger.TryClaim(second, 2, ClaimKind.Roller, roller));
        Assert.True(ledger.TryClaim(first, 2, ClaimKind.Exclusive, normal));
    }

    [Theory]
    [InlineData(ClaimKind.Exclusive, true)]
    [InlineData(ClaimKind.Goal, true)]
    [InlineData(ClaimKind.Shared, true)]
    [InlineData(ClaimKind.Roller, false)]
    public void WalkthroughTransitAllowsSharedClaimsExceptAgainstRollers(ClaimKind existing, bool accepted)
    {
        var (ledger, first, second) = TwoActors();
        Assert.True(ledger.TryClaim(first, 2, existing, TargetOccupancy.None));
        var mask = ClaimMatrix.BlockingMask(new()
        {
            Walkthrough = true
        }, NavFlags.Transit,
            StepPurpose.Transit, OccupancyView.Execution);
        Assert.Equal(accepted, ledger.TryClaim(second, 2, ClaimKind.Shared, mask));
    }

    [Theory]
    [InlineData(ClaimKind.Exclusive, true)]
    [InlineData(ClaimKind.Goal, false)]
    [InlineData(ClaimKind.Shared, true)]
    [InlineData(ClaimKind.Roller, false)]
    public void WalkthroughGoalClaimsRespectGoalAndRollerExclusivity(ClaimKind existing, bool accepted)
    {
        var (ledger, first, second) = TwoActors();
        Assert.True(ledger.TryClaim(first, 2, existing, TargetOccupancy.None));
        var mask = ClaimMatrix.BlockingMask(new()
        {
            Walkthrough = true
        }, NavFlags.Transit,
            StepPurpose.Goal, OccupancyView.Execution);
        Assert.Equal(accepted, ledger.TryClaim(second, 2, ClaimKind.Goal, mask));
    }

    [Theory]
    [InlineData(false, OccupancyView.Execution, false)]
    [InlineData(false, OccupancyView.Planning, false)]
    [InlineData(true, OccupancyView.Execution, false)]
    [InlineData(true, OccupancyView.Planning, true)]
    public void MemberStateAndViewDetermineGoalConflicts(bool walking, OccupancyView view, bool accepted)
    {
        var (ledger, first, second) = TwoActors();
        ledger.Move(first, 2, 2, walking, 10);
        var mask = ClaimMatrix.BlockingMask(new(), NavFlags.Transit, StepPurpose.Goal, view);
        Assert.Equal(accepted, ledger.TryClaim(second, 2, ClaimKind.Exclusive, mask));
    }

    [Fact]
    public void BatchReleaseKeepsRollerClaimsUntilTheUserPhaseEnds()
    {
        var (ledger, first, second) = TwoActors();
        ledger.TryClaim(first, 1, ClaimKind.Exclusive, TargetOccupancy.None);
        ledger.TryClaim(first, 2, ClaimKind.Roller, TargetOccupancy.None);
        ledger.TryClaim(second, 3, ClaimKind.Goal, TargetOccupancy.None);
        ledger.ReleaseBatch(first);
        ledger.ReleaseBatch(first);
        var snapshot = ledger.Snapshot(0);
        Assert.Equal(TargetOccupancy.Walking, snapshot.Targets[1]);
        Assert.Equal(TargetOccupancy.RollerClaim, snapshot.Targets[2]);
        Assert.Equal(TargetOccupancy.GoalClaim, snapshot.Targets[3]);
        ledger.ReleaseRollers();
        ledger.ReleaseRollers();
        Assert.Equal(TargetOccupancy.None, ledger.Snapshot(0).Targets[2]);
        Assert.Equal(TargetOccupancy.GoalClaim, ledger.Snapshot(0).Targets[3]);
    }

    [Fact]
    public void RemoveReleasesAllActorClaimsButKeepsForeignClaimsAndMembership()
    {
        var (ledger, first, second) = TwoActors();
        ledger.TryClaim(first, 2, ClaimKind.Roller, TargetOccupancy.None);
        ledger.TryClaim(first, 3, ClaimKind.Exclusive, TargetOccupancy.None);
        ledger.TryClaim(second, 2, ClaimKind.Shared, TargetOccupancy.None);
        ledger.Remove(first);
        ledger.Remove(first);
        var snapshot = ledger.Snapshot(0);
        Assert.Equal(TargetOccupancy.None, snapshot.Targets[0]);
        Assert.Equal(TargetOccupancy.Walking, snapshot.Targets[1]);
        Assert.Equal(TargetOccupancy.SharedClaim, snapshot.Targets[2]);
        Assert.Equal(TargetOccupancy.None, snapshot.Targets[3]);
        Assert.Equal(new[] { 0, 1, 0, 0 }, ledger.TileCount);
    }

    [Fact]
    public void ExclusiveClaimWinnerFollowsOwnerAcquisitionOrder()
    {
        var (ledger, first, second) = TwoActors();
        var mask = ClaimMatrix.BlockingMask(new(), NavFlags.Transit, StepPurpose.Transit, OccupancyView.Execution);
        Assert.True(ledger.TryClaim(first, 2, ClaimKind.Exclusive, mask));
        Assert.True(ledger.TryClaim(first, 2, ClaimKind.Exclusive, mask));
        Assert.False(ledger.TryClaim(second, 2, ClaimKind.Exclusive, mask));
        ledger.ReleaseBatch(first);
        Assert.True(ledger.TryClaim(second, 2, ClaimKind.Exclusive, mask));
        Assert.False(ledger.TryClaim(first, 2, ClaimKind.Exclusive, mask));
        ledger.ReleaseBatch(second);
        Assert.Equal(TargetOccupancy.None, ledger.Snapshot(0).Targets[2]);
    }

    private static (ClaimLedger Ledger, RoomUser First, RoomUser Second) TwoActors()
    {
        var ledger = new ClaimLedger(4, 4);
        var first = Actor();
        var second = Actor();
        ledger.Move(first, 0, 0, true, 10);
        ledger.Move(second, 1, 1, true, 20);

        return (ledger, first, second);
    }

    private static RoomUser Actor() => (RoomUser)RuntimeHelpers.GetUninitializedObject(typeof(RoomUser));
}
