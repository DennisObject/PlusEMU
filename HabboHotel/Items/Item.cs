using System.Drawing;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Notifications;
using Plus.Core;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Interactor;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Games.Freeze;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items;

public class Item
{
    private object? _navSync;
    internal object NavSync => LazyInitializer.EnsureInitialized(ref _navSync);
    internal bool HasNavigationLock => _navSync != null;
    // Enabled before room admission; attachment may follow or be removed later.
    // PlacementSync may nest NavSync. NavSync never nests PlacementSync or callbacks.
    private bool _navigationSynchronized;
    internal void EnableNavigationSynchronization() => Volatile.Write(ref _navigationSynchronized, true);
    private NavInputs? _navigationInputs;
    internal NavInputs? NavigationInputs
    {
        get => Volatile.Read(ref _navigationInputs);
        set => Volatile.Write(ref _navigationInputs, value);
    }

    private void PublishIfAttached(bool stateOnly = false)
    {
        if (!Volatile.Read(ref _navigationSynchronized)) return;
        lock (NavSync)
            if (NavigationInputs is { } inputs && (!stateOnly || NavItemRecord.StateRelevant(Definition)))
                inputs.PublishCurrent(this);
    }
    internal int NavMutationDepth;
    private long _stateGeneration;
    // Bumped when the stored state value changes and by every UpdateState; lets queued approach intents notice state changes that never reach a nav record.
    internal long StateGeneration => Volatile.Read(ref _stateGeneration);

    public uint Id { get; set; }
    public bool IsTemporary { get; internal init; }
    public uint OwnerId { get; set; }
    public uint RoomId { get; set; }
    public ItemDefinition Definition { get; set; }
    private IFurniObjectData _extraData = FurniObjectData.Empty;
    public IFurniObjectData ExtraData
    {
        get => _extraData;
        set
        {
            if (!Volatile.Read(ref _navigationSynchronized)) { StoreExtraData(value); return; }
            lock (NavSync)
            {
                StoreExtraData(value);
                if (NavigationInputs is { } inputs && NavItemRecord.StateRelevant(Definition)) inputs.PublishCurrent(this);
            }
        }
    }
    public uint UniqueNumber { get; set; }
    public uint UniqueSeries { get; set; }
    public string WallCoordinates = string.Empty;

    public string LegacyDataString {
        get
        {
            if (ExtraData is LegacyDataFormat data)
                return data.Data;
            return string.Empty;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        set
        {
            if (!Volatile.Read(ref _navigationSynchronized))
            {
                if (_extraData is LegacyDataFormat data) { var changed = data.Data != value; data.Data = value; if (changed) MarkInteractionStateChanged(); }
                return;
            }
            SetNavigationState(value);
        }
    }



    private void SetNavigationState(string value)
    {
        LegacyDataFormat? changed = null;
        lock (NavSync)
        {
            if (_extraData is LegacyDataFormat data)
            {
                var different = data.Data != value;
                data.StoreWithoutNotification(value);
                if (different) MarkInteractionStateChanged();
                changed = data;
            }
            if (NavigationInputs is { } inputs && NavItemRecord.StateRelevant(Definition)) inputs.PublishCurrent(this);
        }
        changed?.NotifyDataUpdated();
    }

    // v2 gate sequencer: the same write, but the notification is left to the caller (after its locks are released).
    internal LegacyDataFormat? StoreStateQuietly(string value)
    {
        if (!Volatile.Read(ref _navigationSynchronized))
        {
            if (_extraData is not LegacyDataFormat plain) return null;
            var different = plain.Data != value;
            plain.StoreWithoutNotification(value);
            if (different) MarkInteractionStateChanged();
            return plain;
        }
        LegacyDataFormat? changed = null;
        lock (NavSync)
        {
            if (_extraData is LegacyDataFormat data)
            {
                var different = data.Data != value;
                data.StoreWithoutNotification(value);
                if (different) MarkInteractionStateChanged();
                changed = data;
            }
            if (NavigationInputs is { } inputs && NavItemRecord.StateRelevant(Definition)) inputs.PublishCurrent(this);
        }
        return changed;
    }

    private void StoreExtraData(IFurniObjectData value)
    {
        var replaced = !ReferenceEquals(_extraData, value);
        _extraData = value;
        if (replaced) MarkInteractionStateChanged();
    }

    // Store first, then bump: a click that captured the old generation can only be invalidated, never wrongly kept.
    private void MarkInteractionStateChanged() => Interlocked.Increment(ref _stateGeneration);

    /// TODO @80O: Cleanup shit below
    private Room? _room;
    private bool _updateNeeded;
    [Obsolete]
    public int BaseItem;
    public string Figure = string.Empty;
    public FreezePowerUp FreezePowerUp;
    public string Gender;
    private int _groupId;
    public int GroupId
    {
        get => _groupId;
        set
        {
            if (!Volatile.Read(ref _navigationSynchronized)) { _groupId = value; return; }
            lock (NavSync)
            {
                if (_groupId == value) return;
                _groupId = value;
                NavigationInputs?.PublishCurrent(this);
            }
        }
    }
    public int InteractingBallUser;
    public int InteractingUser;
    public int InteractingUser2;
    public byte InteractionCount;
    public byte InteractionCountHelper;
    public bool MagicRemove = false;
    public bool PendingReset = false;
    private int _rotation;
    public int Rotation
    {
        get => _rotation;
        set
        {
            if (!Volatile.Read(ref _navigationSynchronized)) { _rotation = value; return; }
            lock (NavSync)
            {
                if (_rotation == value) return;
                _rotation = value;
                NavigationInputs?.PublishCurrent(this);
            }
        }
    }

    public Team Team;
    public int UpdateCounter;
    public int UserId;
    public string Username = string.Empty;


    public int Value;

    public Dictionary<int, ThreeDCoord> GetAffectedTiles { get; private set; } = new();

    private int _getX;
    public int GetX
    {
        get => _getX;
        set
        {
            if (!Volatile.Read(ref _navigationSynchronized)) { _getX = value; return; }
            lock (NavSync)
            {
                if (_getX == value) return;
                _getX = value;
                NavigationInputs?.PublishCurrent(this);
            }
        }
    }

    private int _getY;
    public int GetY
    {
        get => _getY;
        set
        {
            if (!Volatile.Read(ref _navigationSynchronized)) { _getY = value; return; }
            lock (NavSync)
            {
                if (_getY == value) return;
                _getY = value;
                NavigationInputs?.PublishCurrent(this);
            }
        }
    }

    private double _getZ;
    public double GetZ
    {
        get => _getZ;
        set
        {
            if (!Volatile.Read(ref _navigationSynchronized)) { _getZ = value; return; }
            lock (NavSync)
            {
                if (_getZ == value) return;
                _getZ = value;
                NavigationInputs?.PublishCurrent(this);
            }
        }
    }

    public bool UpdateNeeded
    {
        get => _updateNeeded;
        set
        {
            if (value && GetRoom() != null)
                GetRoom().GetRoomItemHandler().QueueRoomItemUpdate(this);
            _updateNeeded = value;
        }
    }
    
    [Obsolete("Will be removed in near future. @80O")]
    public bool IsRoller { get; }

    [Obsolete("Will be removed in near future. @80O")]
    public Point Coordinate => new(GetX, GetY);

    [Obsolete("Will be removed in near future. @80O")]
    public List<Point> GetCoords
    {
        get
        {
            var toReturn = new List<Point>
            {
                Coordinate
            };
            foreach (var tile in GetAffectedTiles.Values) toReturn.Add(new(tile.X, tile.Y));
            return toReturn;
        }
    }

    public double TotalHeight
    {
        get
        {
            var curHeight = 0.0;
            if (Definition.AdjustableHeights?.Count > 1)
            {
                if (int.TryParse(LegacyDataString, out var num2) && Definition.AdjustableHeights.Count - 1 >= num2)
                    curHeight = GetZ + Definition.AdjustableHeights[num2];
            }
            if (curHeight <= 0.0)
                curHeight = GetZ + Definition.Height;
            return curHeight;
        }
    }

    public bool IsWallItem => Definition.Type == ItemType.Wall;

    public bool IsFloorItem => Definition.Type == ItemType.Floor;

    public Point SquareInFront
    {
        get
        {
            var sq = new Point(GetX, GetY);
            if (Rotation == 0)
                sq.Y--;
            else if (Rotation == 2)
                sq.X++;
            else if (Rotation == 4)
                sq.Y++;
            else if (Rotation == 6) sq.X--;
            return sq;
        }
    }

    public Point SquareBehind
    {
        get
        {
            var sq = new Point(GetX, GetY);
            if (Rotation == 0)
                sq.Y++;
            else if (Rotation == 2)
                sq.X--;
            else if (Rotation == 4)
                sq.Y--;
            else if (Rotation == 6) sq.X++;
            return sq;
        }
    }

    public Point SquareLeft
    {
        get
        {
            var sq = new Point(GetX, GetY);
            if (Rotation == 0)
                sq.X++;
            else if (Rotation == 2)
                sq.Y--;
            else if (Rotation == 4)
                sq.X--;
            else if (Rotation == 6) sq.Y++;
            return sq;
        }
    }

    public Point SquareRight
    {
        get
        {
            var sq = new Point(GetX, GetY);
            if (Rotation == 0)
                sq.X--;
            else if (Rotation == 2)
                sq.Y++;
            else if (Rotation == 4)
                sq.X++;
            else if (Rotation == 6) sq.Y--;
            return sq;
        }
    }

    public IFurniInteractor Interactor
    {
        get
        {
            if (IsWired) return new InteractorWired();
            switch (Definition.InteractionType)
            {
                case InteractionType.Gate:
                    return new InteractorGate();
                case InteractionType.Teleport:
                    return new InteractorTeleport();
                case InteractionType.Hopper:
                    return new InteractorHopper();
                case InteractionType.Bottle:
                    return new InteractorSpinningBottle();
                case InteractionType.Dice:
                    return new InteractorDice();
                case InteractionType.HabboWheel:
                    return new InteractorHabboWheel();
                case InteractionType.LoveShuffler:
                    return new InteractorLoveShuffler();
                case InteractionType.OneWayGate:
                    return new InteractorOneWayGate();
                case InteractionType.Alert:
                    return new InteractorAlert();
                case InteractionType.VendingMachine:
                    return new InteractorVendor();
                case InteractionType.Scoreboard:
                    return new InteractorScoreboard();
                case InteractionType.PuzzleBox:
                    return new InteractorPuzzleBox();
                case InteractionType.Mannequin:
                    return new InteractorMannequin();
                case InteractionType.Banzaicounter:
                    return new InteractorBanzaiTimer();
                case InteractionType.Freezetimer:
                    return new InteractorFreezeTimer();
                case InteractionType.FreezeTileBlock:
                case InteractionType.FreezeTile:
                    return new InteractorFreezeTile();
                case InteractionType.Footballcounterblue:
                case InteractionType.Footballcountergreen:
                case InteractionType.Footballcounterred:
                case InteractionType.Footballcounteryellow:
                    return new InteractorScoreCounter();
                case InteractionType.Banzaiscoreblue:
                case InteractionType.Banzaiscoregreen:
                case InteractionType.Banzaiscorered:
                case InteractionType.Banzaiscoreyellow:
                    return new InteractorBanzaiScoreCounter();
                case InteractionType.WfFloorSwitch1:
                case InteractionType.WfFloorSwitch2:
                    return new InteractorSwitch();
                case InteractionType.Lovelock:
                    return new InteractorLoveLock();
                case InteractionType.Cannon:
                    return new InteractorCannon();
                case InteractionType.Counter:
                    return new InteractorCounter();
                case InteractionType.CrackableEgg:
                    return new InteractorCrackable();
                case InteractionType.Skateboard:
                    return new InteractorSkateboard();
                case InteractionType.None:
                default:
                    return new InteractorGenericSwitch();
            }
        }
    }

    public bool IsWired
    {
        get
        {
            if (Definition.WiredDescriptor != null)
                return true;
            switch (Definition.InteractionType)
            {
                case InteractionType.WiredSelector:
                case InteractionType.WiredAddon:
                case InteractionType.WiredVariable:
                case InteractionType.WiredEffect:
                case InteractionType.WiredTrigger:
                case InteractionType.WiredCondition:
                    return true;
            }
            return false;
        }
    }

    public List<Point> GetSides()
    {
        var sides = new List<Point>
        {
            SquareBehind,
            SquareInFront,
            SquareLeft,
            SquareRight,
            Coordinate
        };
        return sides;
    }

    private long _movementGeneration;
    internal long MovementGeneration => Interlocked.Read(ref _movementGeneration);

    public void SetState(int pX, int pY, double pZ, Dictionary<int, ThreeDCoord> tiles)
        => SetPlacementState(pX, pY, pZ, tiles, null);

    internal void SetPlacementState(int pX, int pY, double pZ, Dictionary<int, ThreeDCoord> tiles, int? rotation)
    {
        if (!Volatile.Read(ref _navigationSynchronized))
        {
            WritePlacement(pX, pY, pZ, tiles, rotation);
            return;
        }
        lock (NavSync)
        {
            WritePlacement(pX, pY, pZ, tiles, rotation);
            NavigationInputs?.PublishCurrent(this);
        }
    }

    // Field writes only: no room, Wired, map, database or network callbacks under NavSync.
    private void WritePlacement(int pX, int pY, double pZ, Dictionary<int, ThreeDCoord> tiles, int? rotation)
    {
        if (_getX != pX || _getY != pY || !double.IsInfinity(pZ) && _getZ != pZ)
            Interlocked.Increment(ref _movementGeneration);
        _getX = pX; _getY = pY;
        if (!double.IsInfinity(pZ)) _getZ = pZ;
        if (rotation is { } direction) _rotation = direction;
        GetAffectedTiles = tiles;
        MagicTileHeight.Sync(this);
    }

    // v2: a refused automatic close keeps its update request, so the gate never stays open for good.
    private void CloseAutomatically(int retryCycles)
    {
        if (GateTransitionService.Apply(this, "0", GateCloseReason.Automatic, persist: false) == GateTransition.Refused)
            RequestUpdate(retryCycles, false);
    }

    public void ProcessUpdates()
    {
        try
        {
            UpdateCounter--;
            if (UpdateCounter <= 0)
            {
                UpdateNeeded = false;
                UpdateCounter = 0;
                RoomUser? user = null;
                RoomUser? user2 = null;
                switch (Definition.InteractionType)
                {
                    case InteractionType.GuildGate:
                    {
                        if (LegacyDataString == "1")
                        {
                            if (GateTransitionService.For(this) != null)
                                CloseAutomatically(2);
                            else if (GetRoom().GetRoomUserManager().GetUserForSquare(GetX, GetY) == null)
                            {
                                LegacyDataString = "0";
                                UpdateState(false, true);
                            }
                            else
                                RequestUpdate(2, false);
                        }
                        break;
                    }
                    case InteractionType.Effect:
                    {
                        if (LegacyDataString == "1")
                        {
                            if (GetRoom().GetRoomUserManager().GetUserForSquare(GetX, GetY) == null)
                            {
                                LegacyDataString = "0";
                                UpdateState(false, true);
                            }
                            else
                                RequestUpdate(2, false);
                        }
                        break;
                    }
                    case InteractionType.OneWayGate:
                        user = null;
                        if (InteractingUser > 0) user = GetRoom().GetRoomUserManager().GetRoomUserByHabbo(InteractingUser);
                        if (user != null && user.X == GetX && user.Y == GetY)
                        {
                            LegacyDataString = "1";
                            if (GetRoom().UsesV2Movement) user.RequestInteractionStep(GetRoom(), SquareBehind);
                            else user.MoveTo(SquareBehind);
                            user.InteractingGate = false;
                            user.GateId = 0;
                            RequestUpdate(1, false);
                            UpdateState(false, true);
                        }
                        else if (user != null && user.Coordinate == SquareBehind)
                        {
                            user.UnlockWalking();
                            LegacyDataString = "0";
                            InteractingUser = 0;
                            user.InteractingGate = false;
                            user.GateId = 0;
                            UpdateState(false, true);
                        }
                        else if (LegacyDataString == "1")
                        {
                            LegacyDataString = "0";
                            UpdateState(false, true);
                        }
                        if (user == null) InteractingUser = 0;
                        break;
                    case InteractionType.GateVip:
                        user = null;
                        if (InteractingUser > 0) user = GetRoom().GetRoomUserManager().GetRoomUserByHabbo(InteractingUser);
                        var newY = 0;
                        var newX = 0;
                        if (user != null && user.X == GetX && user.Y == GetY)
                        {
                            if (user.RotBody == 4)
                                newY = 1;
                            else if (user.RotBody == 0)
                                newY = -1;
                            else if (user.RotBody == 6)
                                newX = -1;
                            else if (user.RotBody == 2) newX = 1;
                            user.MoveTo(user.X + newX, user.Y + newY);
                            RequestUpdate(1, false);
                        }
                        else if (user != null && (user.Coordinate == SquareBehind || user.Coordinate == SquareInFront))
                        {
                            user.UnlockWalking();
                            if (GateTransitionService.For(this) != null)
                            {
                                InteractingUser = 0;
                                CloseAutomatically(1);
                            }
                            else
                            {
                                LegacyDataString = "0";
                                InteractingUser = 0;
                                UpdateState(false, true);
                            }
                        }
                        else if (LegacyDataString == "1")
                        {
                            if (GateTransitionService.For(this) != null)
                                CloseAutomatically(1);
                            else
                            {
                                LegacyDataString = "0";
                                UpdateState(false, true);
                            }
                        }
                        if (user == null) InteractingUser = 0;
                        break;
                    case InteractionType.Hopper:
                    {
                        user = null;
                        user2 = null;
                        var showHopperEffect = false;
                        var keepDoorOpen = false;
                        var pause = 0;

                        // Do we have a primary user that wants to go somewhere?
                        if (InteractingUser > 0)
                        {
                            user = GetRoom().GetRoomUserManager().GetRoomUserByHabbo(InteractingUser);

                            // Is this user okay?
                            if (user != null)
                            {
                                // Is he in the tele?
                                if (user.Coordinate == Coordinate)
                                {
                                    //Remove the user from the square
                                    user.AllowOverride = false;
                                    if (user.TeleDelay == 0)
                                    {
                                        var roomHopId = ItemHopperFinder.GetAHopper(user.RoomId); // TODO @80O: Remove cast
                                        var nextHopperId = ItemHopperFinder.GetHopperId(roomHopId);
                                        if (!user.IsBot && user.GetClient() != null &&
                                            user.GetClient().GetHabbo() != null)
                                        {
                                            user.GetClient().GetHabbo().IsHopping = true;
                                            user.GetClient().GetHabbo().HopperId = nextHopperId;
                                            if (GetRoom().UsesV2Movement) GetRoom().GetGameMap().Navigation!.Remove(user);
                                            user.GetClient().GetHabbo().PrepareRoom(roomHopId, "");
                                            //User.GetClient().SendMessage(new RoomForwardComposer(RoomHopId));
                                            InteractingUser = 0;
                                        }
                                    }
                                    else
                                    {
                                        user.TeleDelay--;
                                        showHopperEffect = true;
                                    }
                                }
                                // Is he in front of the tele?
                                else if (user.Coordinate == SquareInFront)
                                {
                                    if (GetRoom().UsesV2Movement) user.AllowOverride = false;
                                    else user.AllowOverride = true;
                                    keepDoorOpen = true;

                                    // Lock his walking. We're taking control over him. Allow overriding so he can get in the tele.
                                    if (user.IsWalking && (user.GoalX != GetX || user.GoalY != GetY)) user.ClearMovement(true);
                                    user.CanWalk = false;
                                    if (GetRoom().UsesV2Movement) user.AllowOverride = false;
                                    else user.AllowOverride = true;

                                    // Move into the tele
                                    if (GetRoom().UsesV2Movement) user.RequestInteractionStep(GetRoom(), Coordinate, true);
                                    else user.MoveTo(Coordinate.X, Coordinate.Y, true);
                                }
                                // Not even near, do nothing and move on for the next user.
                                else
                                    InteractingUser = 0;
                            }
                            else
                            {
                                // Invalid user, do nothing and move on for the next user.
                                InteractingUser = 0;
                            }
                        }
                        if (InteractingUser2 > 0)
                        {
                            user2 = GetRoom().GetRoomUserManager().GetRoomUserByHabbo(InteractingUser2);

                            // Is this user okay?
                            if (user2 != null)
                            {
                                // If so, open the door, unlock the user's walking, and try to push him out in the right direction. We're done with him!
                                keepDoorOpen = true;
                                user2.UnlockWalking();
                                if (GetRoom().UsesV2Movement) user2.RequestInteractionStep(GetRoom(), SquareInFront);
                                else user2.MoveTo(SquareInFront);
                            }

                            // This is a one time thing, whether the user's valid or not.
                            InteractingUser2 = 0;
                        }

                        // Set the new item state, by priority
                        if (keepDoorOpen)
                        {
                            if (LegacyDataString != "1")
                            {
                                LegacyDataString = "1";
                                UpdateState(false, true);
                            }
                        }
                        else if (showHopperEffect)
                        {
                            if (LegacyDataString != "2")
                            {
                                LegacyDataString = "2";
                                UpdateState(false, true);
                            }
                        }
                        else
                        {
                            if (LegacyDataString != "0")
                            {
                                if (pause == 0)
                                {
                                    LegacyDataString = "0";
                                    UpdateState(false, true);
                                    pause = 2;
                                }
                                else
                                    pause--;
                            }
                        }

                        // We're constantly going!
                        RequestUpdate(1, false);
                        break;
                    }
                    case InteractionType.Teleport:
                    {
                        user = null;
                        user2 = null;
                        var keepDoorOpen = false;
                        var showTeleEffect = false;

                        // Do we have a primary user that wants to go somewhere?
                        if (InteractingUser > 0)
                        {
                            user = GetRoom().GetRoomUserManager().GetRoomUserByHabbo(InteractingUser);

                            // Is this user okay?
                            if (user != null)
                            {
                                // Is he in the tele?
                                if (user.Coordinate == Coordinate)
                                {
                                    //Remove the user from the square
                                    user.AllowOverride = false;
                                    if (ItemTeleporterFinder.IsTeleLinked(Id, GetRoom()))
                                    {
                                        showTeleEffect = true;
                                        if (true)
                                        {
                                            // Woop! No more delay.
                                            var teleId = ItemTeleporterFinder.GetLinkedTele(Id);
                                            var roomId = ItemTeleporterFinder.GetTeleRoomId(teleId, GetRoom());

                                            // Do we need to tele to the same room or gtf to another?
                                            if (roomId == RoomId)
                                            {
                                                var item = GetRoom().GetRoomItemHandler().GetItem(teleId);
                                                if (item == null)
                                                    user.UnlockWalking();
                                                else
                                                {
                                                    // Set pos
                                                    user.SetPos(item.GetX, item.GetY, item.GetZ);
                                                    user.SetRot(item.Rotation, false);
                                                    if (!user.IsBot)
                                                        RewardTrackManager.Current?.Progress(user.GetClient(), RewardTrackActions.Teleport);

                                                    // Force tele effect update (dirty)
                                                    item.LegacyDataString = "2";
                                                    item.UpdateState(false, true);

                                                    // Set secondary interacting user
                                                    item.InteractingUser2 = InteractingUser;
                                                    GetRoom().GetGameMap().RemoveUserFromMap(user, new(GetX, GetY));
                                                    InteractingUser = 0;
                                                }
                                            }
                                            else
                                            {
                                                if (user.TeleDelay == 0)
                                                {
                                                    // Let's run the teleport delegate to take futher care of this.. WHY DARIO?!
                                                    if (!user.IsBot && user != null && user.GetClient() != null &&
                                                        user.GetClient().GetHabbo() != null)
                                                    {
                                                        user.GetClient().GetHabbo().IsTeleporting = true;
                                                        user.GetClient().GetHabbo().TeleportingRoomId = roomId;
                                                        user.GetClient().GetHabbo().TeleporterId = teleId;
                                                        if (GetRoom().UsesV2Movement) GetRoom().GetGameMap().Navigation!.Remove(user);
                                                        user.GetClient().GetHabbo().PrepareRoom(roomId, "");
                                                        //User.GetClient().SendMessage(new RoomForwardComposer(RoomId));
                                                        InteractingUser = 0;
                                                    }
                                                }
                                                else
                                                {
                                                    user.TeleDelay--;
                                                    showTeleEffect = true;
                                                }
                                                //PlusEnvironment.GetGame().GetRoomManager().AddTeleAction(new TeleUserData(User.GetClient().GetMessageHandler(), User.GetClient().GetHabbo(), RoomId, TeleId));
                                            }
                                            GetRoom().GetGameMap().GenerateMaps();
                                            // We're done with this tele. We have another one to bother.
                                        }
                                    }
                                    else
                                    {
                                        // This tele is not linked, so let's gtfo.
                                        user.UnlockWalking();
                                        InteractingUser = 0;
                                    }
                                }
                                // Is he in front of the tele?
                                else if (user.Coordinate == SquareInFront)
                                {
                                    if (GetRoom().UsesV2Movement) user.AllowOverride = false;
                                    else user.AllowOverride = true;
                                    // Open the door
                                    keepDoorOpen = true;

                                    // Lock his walking. We're taking control over him. Allow overriding so he can get in the tele.
                                    if (user.IsWalking && (user.GoalX != GetX || user.GoalY != GetY)) user.ClearMovement(true);
                                    user.CanWalk = false;
                                    if (GetRoom().UsesV2Movement) user.AllowOverride = false;
                                    else user.AllowOverride = true;

                                    // Move into the tele
                                    if (GetRoom().UsesV2Movement) user.RequestInteractionStep(GetRoom(), Coordinate, true);
                                    else user.MoveTo(Coordinate.X, Coordinate.Y, true);
                                }
                                // Not even near, do nothing and move on for the next user.
                                else
                                    InteractingUser = 0;
                            }
                            else
                            {
                                // Invalid user, do nothing and move on for the next user.
                                InteractingUser = 0;
                            }
                        }

                        // Do we have a secondary user that wants to get out of the tele?
                        if (InteractingUser2 > 0)
                        {
                            user2 = GetRoom().GetRoomUserManager().GetRoomUserByHabbo(InteractingUser2);

                            // Is this user okay?
                            if (user2 != null)
                            {
                                // If so, open the door, unlock the user's walking, and try to push him out in the right direction. We're done with him!
                                keepDoorOpen = true;
                                user2.UnlockWalking();
                                if (GetRoom().UsesV2Movement) user2.RequestInteractionStep(GetRoom(), SquareInFront);
                                else user2.MoveTo(SquareInFront);
                            }

                            // This is a one time thing, whether the user's valid or not.
                            InteractingUser2 = 0;
                        }

                        // Set the new item state, by priority
                        if (showTeleEffect)
                        {
                            if (LegacyDataString != "2")
                            {
                                LegacyDataString = "2";
                                UpdateState(false, true);
                            }
                        }
                        else if (keepDoorOpen)
                        {
                            if (LegacyDataString != "1")
                            {
                                LegacyDataString = "1";
                                UpdateState(false, true);
                            }
                        }
                        else
                        {
                            if (LegacyDataString != "0")
                            {
                                LegacyDataString = "0";
                                UpdateState(false, true);
                            }
                        }

                        // We're constantly going!
                        RequestUpdate(1, false);
                        break;
                    }
                    case InteractionType.Bottle:
                        LegacyDataString = Random.Shared.Next(0, 8).ToString();
                        UpdateState();
                        break;
                    case InteractionType.Dice:
                    {
                        var numbers = new[] { "1", "2", "3", "4", "5", "6" };
                        if (LegacyDataString == "-1")
                            LegacyDataString = RandomizeStrings(numbers)[0];
                        UpdateState();
                    }
                        break;
                    case InteractionType.HabboWheel:
                        LegacyDataString = Random.Shared.Next(1, 10).ToString();
                        UpdateState();
                        break;
                    case InteractionType.LoveShuffler:
                        if (LegacyDataString == "0")
                        {
                            LegacyDataString = Random.Shared.Next(1, 5).ToString();
                            RequestUpdate(20, false);
                        }
                        else if (LegacyDataString != "-1") LegacyDataString = "-1";
                        UpdateState(false, true);
                        break;
                    case InteractionType.Alert:
                        if (LegacyDataString == "1")
                        {
                            LegacyDataString = "0";
                            UpdateState(false, true);
                        }
                        break;
                    case InteractionType.VendingMachine:
                        if (LegacyDataString == "1")
                        {
                            user = GetRoom().GetRoomUserManager().GetRoomUserByHabbo(InteractingUser);
                            if (user == null)
                                break;
                            user.UnlockWalking();
                            if (Definition.VendingIds.Count > 0)
                            {
                                var randomDrink = Definition.VendingIds[Random.Shared.Next(0, Definition.VendingIds.Count)];
                                user.CarryItem(randomDrink);
                            }
                            InteractingUser = 0;
                            LegacyDataString = "0";
                            UpdateState(false, true);
                        }
                        break;
                    case InteractionType.Scoreboard:
                    {
                        if (string.IsNullOrEmpty(LegacyDataString))
                            break;
                        var seconds = 0;
                        try
                        {
                            seconds = int.Parse(LegacyDataString);
                        }
                        catch { }
                        if (seconds > 0)
                        {
                            if (InteractionCountHelper == 1)
                            {
                                seconds--;
                                InteractionCountHelper = 0;
                                LegacyDataString = seconds.ToString();
                                UpdateState();
                            }
                            else
                                InteractionCountHelper++;
                            UpdateCounter = 1;
                        }
                        else
                            UpdateCounter = 0;
                        break;
                    }
                    case InteractionType.Banzaicounter:
                    {
                        if (string.IsNullOrEmpty(LegacyDataString))
                            break;
                        var seconds = 0;
                        try
                        {
                            seconds = int.Parse(LegacyDataString);
                        }
                        catch { }
                        if (seconds > 0)
                        {
                            if (InteractionCountHelper == 1)
                            {
                                seconds--;
                                InteractionCountHelper = 0;
                                if (GetRoom().GetBanzai().IsBanzaiActive)
                                {
                                    LegacyDataString = seconds.ToString();
                                    UpdateState();
                                }
                                else
                                    break;
                            }
                            else
                                InteractionCountHelper++;
                            UpdateCounter = 1;
                        }
                        else
                        {
                            UpdateCounter = 0;
                            GetRoom().GetBanzai().BanzaiEnd();
                        }
                        break;
                    }
                    case InteractionType.Banzaitele:
                    {
                        LegacyDataString = string.Empty;
                        UpdateState();
                        break;
                    }
                    case InteractionType.Banzaifloor:
                    {
                        if (Value == 3)
                        {
                            if (InteractionCountHelper == 1)
                            {
                                InteractionCountHelper = 0;
                                switch (Team)
                                {
                                    case Team.Blue:
                                    {
                                        LegacyDataString = "11";
                                        break;
                                    }
                                    case Team.Green:
                                    {
                                        LegacyDataString = "8";
                                        break;
                                    }
                                    case Team.Red:
                                    {
                                        LegacyDataString = "5";
                                        break;
                                    }
                                    case Team.Yellow:
                                    {
                                        LegacyDataString = "14";
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                LegacyDataString = "";
                                InteractionCountHelper++;
                            }
                            UpdateState();
                            InteractionCount++;
                            if (InteractionCount < 16)
                                UpdateCounter = 1;
                            else
                                UpdateCounter = 0;
                        }
                        break;
                    }
                    case InteractionType.Banzaipuck:
                    {
                        if (InteractionCount > 4)
                        {
                            InteractionCount++;
                            UpdateCounter = 1;
                        }
                        else
                        {
                            InteractionCount = 0;
                            UpdateCounter = 0;
                        }
                        break;
                    }
                    case InteractionType.FreezeTile:
                    {
                        if (InteractingUser > 0)
                        {
                            LegacyDataString = "11000";
                            UpdateState(false, true);
                            GetRoom().GetFreeze().OnFreezeTiles(this, FreezePowerUp);
                            InteractingUser = 0;
                            InteractionCountHelper = 0;
                        }
                        break;
                    }
                    case InteractionType.Counter:
                    {
                        if (WiredCounterController.Recognizes(this)) break;
                        if (string.IsNullOrEmpty(LegacyDataString))
                            break;
                        var seconds = 0;
                        try
                        {
                            seconds = int.Parse(LegacyDataString);
                        }
                        catch { }
                        if (seconds > 0)
                        {
                            if (InteractionCountHelper == 1)
                            {
                                seconds--;
                                InteractionCountHelper = 0;
                                if (GetRoom().GetSoccer().GameIsStarted)
                                {
                                    LegacyDataString = seconds.ToString();
                                    UpdateState();
                                }
                                else
                                    break;
                            }
                            else
                                InteractionCountHelper++;
                            UpdateCounter = 1;
                        }
                        else
                        {
                            UpdateNeeded = false;
                            GetRoom().GetSoccer().StopGame();
                        }
                        break;
                    }
                    case InteractionType.Freezetimer:
                    {
                        if (string.IsNullOrEmpty(LegacyDataString))
                            break;
                        var seconds = 0;
                        try
                        {
                            seconds = int.Parse(LegacyDataString);
                        }
                        catch { }
                        if (seconds > 0)
                        {
                            if (InteractionCountHelper == 1)
                            {
                                seconds--;
                                InteractionCountHelper = 0;
                                if (GetRoom().GetFreeze().GameIsStarted)
                                {
                                    LegacyDataString = seconds.ToString();
                                    UpdateState();
                                }
                                else
                                    break;
                            }
                            else
                                InteractionCountHelper++;
                            UpdateCounter = 1;
                        }
                        else
                        {
                            UpdateNeeded = false;
                            GetRoom().GetFreeze().StopGame();
                        }
                        break;
                    }
                    case InteractionType.PressurePad:
                    {
                        LegacyDataString = "1";
                        UpdateState();
                        break;
                    }
                    case InteractionType.WiredSelector:
                    case InteractionType.WiredAddon:
                    case InteractionType.WiredVariable:
                    case InteractionType.WiredEffect:
                    case InteractionType.WiredTrigger:
                    case InteractionType.WiredCondition:
                    {
                        if (LegacyDataString == "1")
                        {
                            LegacyDataString = "0";
                            UpdateState(false, true);
                        }
                    }
                        break;
                    case InteractionType.Cannon:
                    {
                        if (LegacyDataString != "1")
                            break;
                        var targetStart = Coordinate;
                        var targetSquares = new List<Point>();
                        switch (Rotation)
                        {
                            case 0:
                            {
                                targetStart = new(GetX - 1, GetY);
                                if (!targetSquares.Contains(targetStart))
                                    targetSquares.Add(targetStart);
                                for (var I = 1; I <= 3; I++)
                                {
                                    var targetSquare = new Point(targetStart.X - I, targetStart.Y);
                                    if (!targetSquares.Contains(targetSquare))
                                        targetSquares.Add(targetSquare);
                                }
                                break;
                            }
                            case 2:
                            {
                                targetStart = new(GetX, GetY - 1);
                                if (!targetSquares.Contains(targetStart))
                                    targetSquares.Add(targetStart);
                                for (var I = 1; I <= 3; I++)
                                {
                                    var targetSquare = new Point(targetStart.X, targetStart.Y - I);
                                    if (!targetSquares.Contains(targetSquare))
                                        targetSquares.Add(targetSquare);
                                }
                                break;
                            }
                            case 4:
                            {
                                targetStart = new(GetX + 2, GetY);
                                if (!targetSquares.Contains(targetStart))
                                    targetSquares.Add(targetStart);
                                for (var I = 1; I <= 3; I++)
                                {
                                    var targetSquare = new Point(targetStart.X + I, targetStart.Y);
                                    if (!targetSquares.Contains(targetSquare))
                                        targetSquares.Add(targetSquare);
                                }
                                break;
                            }
                            case 6:
                            {
                                targetStart = new(GetX, GetY + 2);
                                if (!targetSquares.Contains(targetStart))
                                    targetSquares.Add(targetStart);
                                for (var I = 1; I <= 3; I++)
                                {
                                    var targetSquare = new Point(targetStart.X, targetStart.Y + I);
                                    if (!targetSquares.Contains(targetSquare))
                                        targetSquares.Add(targetSquare);
                                }
                                break;
                            }
                        }
                        if (targetSquares.Count > 0)
                        {
                            foreach (var square in targetSquares.ToList())
                            {
                                var affectedUsers = _room.GetGameMap().GetRoomUsers(square).ToList();
                                if (affectedUsers == null || affectedUsers.Count == 0)
                                    continue;
                                foreach (var target in affectedUsers)
                                {
                                    if (target == null || target.IsBot || target.IsPet)
                                        continue;
                                    if (target.GetClient() == null || target.GetClient().GetHabbo() == null)
                                        continue;
                                    if (_room.CheckRights(target.GetClient(), true))
                                        continue;
                                    target.ApplyEffect(4);
                                    target.GetClient().Send(new RoomNotificationComposer("Kicked from room", "You were hit by a cannonball!", "room_kick_cannonball", ""));
                                    target.ApplyEffect(0);
                                    _room.GetRoomUserManager().RemoveUserFromRoom(target.GetClient(), true);
                                }
                            }
                        }
                        LegacyDataString = "2";
                        UpdateState(false, true);
                    }
                        break;
                }
            }
        }
        catch (Exception e)
        {
            ExceptionLogger.LogException(e);
        }
    }

    public static string[] RandomizeStrings(string[] arr)
    {
        var list = new List<KeyValuePair<int, string>>();
        // Add all strings from array
        // Add new random int each time
        foreach (var s in arr) list.Add(new(Random.Shared.Next(), s));
        // Sort the list by the random number
        var sorted = from item in list
            orderby item.Key
            select item;
        // Allocate new string array
        var result = new string[arr.Length];
        // Copy values to array
        var index = 0;
        foreach (var pair in sorted)
        {
            result[index] = pair.Value;
            index++;
        }
        // Return copied array
        return result;
    }

    public void RequestUpdate(int cycles, bool setUpdate)
    {
        UpdateCounter = cycles;
        if (setUpdate)
            UpdateNeeded = true;
    }

    public void UpdateState()
    {
        UpdateState(true, true);
    }

    public void UpdateState(bool inDb, bool inRoom)
    {
        if (GetRoom() == null)
            return;
        MagicTileHeight.Sync(this);
        PublishIfAttached(true);
        MarkInteractionStateChanged();
        GetRoom().GetGameMap()?.Navigation?.ItemStateChanged(Id);
        if (inDb)
            GetRoom().GetRoomItemHandler().UpdateItem(this);
        if (IsFloorItem)
            GetRoom().GetGameMap()?.NotifyPlacementState(this);
        if (inRoom)
        {
            if (IsFloorItem)
                GetRoom().SendPacket(new ObjectUpdateComposer(this));
            else
                GetRoom().SendPacket(new ItemUpdateComposer(this));
        }
    }

    internal void BindTemporaryRoom(Room room)
    {
        if (!IsTemporary || RoomId != room.RoomId) throw new InvalidOperationException("Only a temporary item in this room can be bound.");
        _room = room;
    }

    [Obsolete]
    public Room? GetRoom()
    {
        if (_room != null)
            return _room;
        if (PlusEnvironment.Game.RoomManager.TryGetRoom(RoomId, out var room))
            return room;
        return null;
    }

    public void UserFurniCollision(RoomUser user)
    {
        if (user == null || user.GetClient() == null || user.GetClient().GetHabbo() == null)
            return;
        GetRoom().GetWired().TriggerEvent(WiredBoxType.TriggerUserFurniCollision, user.GetClient().GetHabbo(), this);
    }

    public void UserWalksOnFurni(RoomUser user)
    {
        if (user == null || user.GetClient() == null || user.GetClient().GetHabbo() == null)
            return;
        if (Definition.InteractionType == InteractionType.Tent || Definition.InteractionType == InteractionType.TentSmall) GetRoom().AddUserToTent(Id, user);
        GetRoom().GetWired().TriggerEvent(WiredBoxType.TriggerWalkOnFurni, user.GetClient().GetHabbo(), this);
        user.LastItem = this;
    }

    public void UserWalksOffFurni(RoomUser user)
    {
        if (user == null || user.GetClient() == null || user.GetClient().GetHabbo() == null)
            return;
        if (Definition.InteractionType == InteractionType.Tent || Definition.InteractionType == InteractionType.TentSmall)
            GetRoom().RemoveUserFromTent(Id, user);
        GetRoom().GetWired().TriggerEvent(WiredBoxType.TriggerWalkOffFurni, user.GetClient().GetHabbo(), this);
    }

    public void Destroy()
    {
        NavigationInputs?.Remove(this);
        _room = null;
        Definition = null;
        GetAffectedTiles.Clear();
    }
}