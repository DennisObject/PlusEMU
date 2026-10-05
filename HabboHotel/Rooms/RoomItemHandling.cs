using System.Collections.Concurrent;
using System.Drawing;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Core;
using Plus.Core.Language;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Interactor;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Items.Wired;

using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Rooms;

[Obsolete("Everything in here is bad and whoever wrote this must've been high on some crack or something")]
public class RoomItemHandling
{
    private readonly ConcurrentDictionary<uint, Item> _floorItems;
    private readonly Dictionary<uint, Item> _temporaryItems = new();
    private int _nextTemporaryId = -1;
    public const int TemporaryItemLimit = 200;

    public bool OwnsTemporary(Item item) => item.IsTemporary
        && _temporaryItems.TryGetValue(item.Id, out var owned) && ReferenceEquals(owned, item);

    // Negative wire IDs belong only to this registry; successful IDs are never reused during this room lifetime.
    public Item? PlaceTemporaryFloorItem(ItemDefinition definition, uint ownerId, int x, int y,
        int rotation, double? height = null, string state = "")
    {
        if (definition.Type != Plus.HabboHotel.Users.Inventory.Furniture.ItemType.Floor
            || _temporaryItems.Count >= TemporaryItemLimit || _nextTemporaryId == int.MinValue)
            return null;
        while (_nextTemporaryId != int.MinValue && GetItem(unchecked((uint)_nextTemporaryId)) != null) _nextTemporaryId--;
        if (_nextTemporaryId == int.MinValue) return null;
        var id = unchecked((uint)_nextTemporaryId);
        var item = new Item { Id = id, IsTemporary = true, RoomId = _room.RoomId,
            OwnerId = ownerId, UserId = unchecked((int)ownerId), Definition = definition,
            ExtraData = FurniExtraData.Load(definition, state, true), Username = _room.OwnerName };
        _temporaryItems.Add(id, item);
        if (!SetFloorItem(null!, item, x, y, rotation, true, false, true, true, height ?? -1))
        {
            _temporaryItems.Remove(id);
            return null;
        }
        _nextTemporaryId--;
        return item;
    }

    public bool RemoveTemporaryFloorItem(Item item)
    {
        if (!OwnsTemporary(item) || !ReferenceEquals(GetItem(item.Id), item)) return false;
        _room.GetWired()?.TryRemove(item.Id);
        if (item.Definition.InteractionType == InteractionType.FootballGate) _room.GetSoccer().UnRegisterGate(item);
        if (item.Definition.InteractionType is InteractionType.Tent or InteractionType.TentSmall) _room.RemoveTent(item.Id);
        RemoveRoomItem(item);
        _temporaryItems.Remove(item.Id);
        return true;
    }

    private readonly ConcurrentDictionary<uint, Item> _movedItems;
    private readonly List<uint> _rollerItemsMoved;
    private readonly List<IServerPacket> _rollerMessages;

    private readonly ConcurrentDictionary<uint, Item> _rollers;
    private readonly List<int> _rollerUsersMoved;
    private readonly Room _room;
    private readonly IRoomItemStore _store;
    private readonly IRoomItemMetadataStore _metadata;
    private readonly IGameClientManager _clients;
    private readonly ILanguageManager _language;
    private readonly IItemInteractorFactory _interactors;
    private readonly IItemTravelStore _travelStore;
    private readonly IRewardTrackManager _rewards;
    private readonly ConcurrentDictionary<uint, Item> _wallItems;
    private int _mRollerCycle;
    private int _mRollerSpeed;

    private ConcurrentQueue<Item> _roomItemUpdateQueue;

    public int HopperCount;

    public RoomItemHandling(Room room, IRoomItemStore store, IRoomItemMetadataStore metadata,
        IGameClientManager clients, ILanguageManager language, IItemInteractorFactory interactors, IItemTravelStore travelStore,
        IRewardTrackManager rewards)
    {
        _room = room;
        _store = store;
        _metadata = metadata;
        _clients = clients;
        _language = language;
        _interactors = interactors;
        _travelStore = travelStore;
        _rewards = rewards;
        HopperCount = 0;
        GotRollers = false;
        _mRollerSpeed = 4;
        _mRollerCycle = 0;
        _movedItems = new();
        _rollers = new();
        _wallItems = new();
        _floorItems = new();
        _rollerItemsMoved = new();
        _rollerUsersMoved = new();
        _rollerMessages = new();
        _roomItemUpdateQueue = new();
    }

    public bool GotRollers { get; set; }

    public ICollection<Item> GetFloor => _floorItems.Values;

    public ICollection<Item> GetWall => _wallItems.Values;

    public IEnumerable<Item> GetWallAndFloor => _floorItems.Values.Concat(_wallItems.Values);

    public void TryAddRoller(uint itemId, Item roller)
    {
        _rollers.TryAdd(itemId, roller);
    }

    public void QueueRoomItemUpdate(Item item)
    {
        _roomItemUpdateQueue.Enqueue(item);
    }

    public void SetSpeed(int p)
    {
        _mRollerSpeed = p;
    }

    public string? WallPositionCheck(string wallPosition)
    {
        //:w=3,2 l=9,63 l
        try
        {
            if (wallPosition.Contains(Convert.ToChar(13))) return null;
            if (wallPosition.Contains(Convert.ToChar(9))) return null;
            var posD = wallPosition.Split(' ');
            if (posD[2] != "l" && posD[2] != "r")
                return null;
            var widD = posD[0].Substring(3).Split(',');
            var widthX = int.Parse(widD[0]);
            var widthY = int.Parse(widD[1]);
            if (widthX < -1000 || widthY < -1 || widthX > 700 || widthY > 700)
                return null;
            var lenD = posD[1].Substring(2).Split(',');
            var lengthX = int.Parse(lenD[0]);
            var lengthY = int.Parse(lenD[1]);
            if (lengthX < -1 || lengthY < -1000 || lengthX > 700 || lengthY > 700)
                return null;
            return $":w={widthX},{widthY} l={lengthX},{lengthY} {posD[2]}";
        }
        catch
        {
            return null;
        }
    }

    public void LoadFurniture(IReadOnlyList<Item> items)
    {
        var detached = new HashSet<Item>(ReferenceEqualityComparer.Instance);
        if (_floorItems.Count > 0)
        {
            foreach (var previous in _floorItems.Values)
            {
                _room.GetGameMap().Navigation?.Inputs.Remove(previous);
                _room.GetWired()?.DetachRoomItem(previous);
                previous.Detach(_room);
                detached.Add(previous);
            }
            _floorItems.Clear();
        }
        if (_wallItems.Count > 0)
        {
            foreach (var previous in _wallItems.Values) previous.Detach(_room);
            _wallItems.Clear();
        }
        foreach (var previous in _temporaryItems.Values)
            if (detached.Add(previous)) previous.Detach(_room);
        _temporaryItems.Clear();
        foreach (var item in items.ToList())
        {
            if (item == null)
                continue;
            if (item.UserId == 0)
            {
                _store.AssignOwner(item.Id, _room.OwnerId);
            }
            if (MagicTileHeight.IsMagicTile(item.Definition.InteractionType))
            {
                MagicTileHeight.Sync(item);
                UpdateItem(item);
            }
            if (item.IsFloorItem)
            {
                if (!_room.GetGameMap().ValidTile(item.GetX, item.GetY))
                {
                    _store.ClearRoom(item.Id);
                    var client = _clients.GetClientByUserId(item.UserId);
                    if (client != null)
                    {
                        client.GetHabbo().Inventory.Furniture.AddItem(item.ToInventoryItem());
                        client.Send(new FurniListUpdateComposer());
                    }
                    continue;
                }
                AdmitFloorItem(item);
            }
            else if (item.IsWallItem)
            {
                if (string.IsNullOrWhiteSpace(item.WallCoordinates))
                {
                    _store.SaveWallPosition(item.Id, ":w=0,2 l=11,53 l");
                    item.WallCoordinates = ":w=0,2 l=11,53 l";
                }
                try
                {
                    item.WallCoordinates = WallPositionCheck($":{item.WallCoordinates.Split(':')[1]}");
                }
                catch
                {
                    _store.SaveWallPosition(item.Id, ":w=0,2 l=11,53 l");
                    item.WallCoordinates = ":w=0,2 l=11,53 l";
                }
                if (!_wallItems.ContainsKey(item.Id))
                {
                    item.Attach(_room, _interactors, _travelStore, _rewards);
                    _wallItems.TryAdd(item.Id, item);
                }
            }
        }
        foreach (var item in _floorItems.Values.ToList())
        {
            _room.GetWired()?.AttachRoomItem(item);
            if (item.IsRoller)
                GotRollers = true;
            else if (item.Definition.InteractionType == InteractionType.Moodlight)
            {
                if (_room.MoodlightData == null)
                    _room.MoodlightData = LoadMoodlight(item.Id);
            }
            else if (item.Definition.InteractionType == InteractionType.Toner)
            {
                if (_room.TonerData == null)
                    _room.TonerData = LoadToner(item.Id);
            }
            else if (item.IsWired)
            {
                if (_room == null)
                    continue;
                if (_room.GetWired() == null)
                    continue;
                _room.GetWired().LoadWiredBox(item);
            }
            else if (item.Definition.InteractionType == InteractionType.Hopper)
                HopperCount++;
        }
    }

    private Plus.HabboHotel.Items.Data.Moodlight.MoodlightData? LoadMoodlight(uint itemId) =>
        _metadata.LoadMoodlight(itemId) is { } record ? new Plus.HabboHotel.Items.Data.Moodlight.MoodlightData(itemId, record) : null;

    private Plus.HabboHotel.Items.Data.Toner.TonerData? LoadToner(uint itemId) =>
        _metadata.LoadToner(itemId) is { } record ? new Plus.HabboHotel.Items.Data.Toner.TonerData(itemId, record) : null;

    public Item? GetItem(uint pId)
    {
        if (_floorItems != null && _floorItems.ContainsKey(pId))
        {
            Item? item = null;
            if (_floorItems.TryGetValue(pId, out item))
                return item;
        }
        else if (_wallItems != null && _wallItems.ContainsKey(pId))
        {
            Item? item = null;
            if (_wallItems.TryGetValue(pId, out item))
                return item;
        }
        return null;
    }

    public void RemoveFurniture(GameClient session, uint id)
    {
        var item = GetItem(id);
        if (item == null || item.IsTemporary)
            return;
        // Before anything else changes: if the saved wired settings cannot be dropped, the box stays placed.
        _room.GetWired()?.ResetRoomItems([item]);
        if (item.Definition.InteractionType == InteractionType.FootballGate)
            _room.GetSoccer().UnRegisterGate(item);
        if (item.Definition.InteractionType != InteractionType.Gift)
            item.Interactor.OnRemove(session, item);
        if (item.Definition.InteractionType == InteractionType.GuildGate)
        {
            item.UpdateCounter = 0;
            item.UpdateNeeded = false;
        }
        RemoveRoomItem(item);
    }

    private void RemoveRoomItem(Item item)
    {
        var inputs = _room.GetGameMap().Navigation?.Inputs;
        if (inputs != null && item.IsFloorItem)
        {
            lock (item.NavSync)
            {
                if (!_floorItems.TryRemove(new KeyValuePair<uint, Item>(item.Id, item))) return;
                inputs.Remove(item);
            }
        }
        if (item.IsFloorItem)
            _room.SendPacket(new ObjectRemoveComposer(item.Id, item.IsTemporary, item.UserId));
        else if (item.IsWallItem)
            _room.SendPacket(new ItemRemoveComposer(item.Id, item.UserId));

        //TODO: Recode this specific part
        if (item.IsWallItem)
            _wallItems.TryRemove(item.Id, out item);
        else
        {
            _room.GetWired()?.DetachRoomItem(item);
            if (inputs == null) _floorItems.TryRemove(item.Id, out item);
            //mFloorItems.OnCycle();
            _room.GetGameMap().RemoveFromMap(item);
        }
        RemoveItem(item);
        _room.GetGameMap().GenerateMaps();
        _room.GetGameMap().FlushPlacementUpdates();
        _room.GetRoomUserManager().UpdateUserStatusses();
        item.Detach(_room);
    }

    private List<IServerPacket> CycleRollers()
    {
        if (_room.GetGameMap().Navigation?.UsesExecutor == true) return CycleV2Rollers();
        if (!GotRollers)
            return new();
        if (_mRollerCycle >= _mRollerSpeed || _mRollerSpeed == 0)
        {
            _rollerItemsMoved.Clear();
            _rollerUsersMoved.Clear();
            _rollerMessages.Clear();
            List<Item> itemsOnRoller;
            List<Item> itemsOnNext;
            foreach (var roller in _rollers.Values.ToList())
            {
                if (roller == null)
                    continue;
                var nextSquare = roller.SquareInFront;
                itemsOnRoller = _room.GetGameMap().GetRoomItemForSquare(roller.GetX, roller.GetY, roller.GetZ);
                itemsOnNext = _room.GetGameMap().GetAllRoomItemForSquare(nextSquare.X, nextSquare.Y).ToList();
                if (itemsOnRoller.Count > 10)
                    itemsOnRoller = _room.GetGameMap().GetRoomItemForSquare(roller.GetX, roller.GetY, roller.GetZ).Take(10).ToList();
                var nextSquareIsRoller = itemsOnNext.Count(x => x.Definition.InteractionType == InteractionType.Roller) > 0;
                var nextRollerClear = true;
                var nextZ = 0.0;
                var nextRoller = false;
                foreach (var item in itemsOnNext.ToList())
                {
                    if (item.IsRoller)
                    {
                        if (item.TotalHeight > nextZ)
                            nextZ = item.TotalHeight;
                        nextRoller = true;
                    }
                }
                if (nextRoller)
                {
                    foreach (var item in itemsOnNext.ToList())
                    {
                        if (item.TotalHeight > nextZ)
                            nextRollerClear = false;
                    }
                }
                if (itemsOnRoller.Count > 0)
                {
                    foreach (var rItem in itemsOnRoller.ToList())
                    {
                        if (rItem == null)
                            continue;
                        if (!_rollerItemsMoved.Contains(rItem.Id) && _room.GetGameMap().CanRollItemHere(nextSquare.X, nextSquare.Y) && nextRollerClear && roller.GetZ < rItem.GetZ &&
                            _room.GetRoomUserManager().GetUserForSquare(nextSquare.X, nextSquare.Y) == null)
                        {
                            if (!nextSquareIsRoller)
                                nextZ = rItem.GetZ - roller.Definition.Height;
                            else
                                nextZ = rItem.GetZ;
                            _rollerMessages.Add(UpdateItemOnRoller(rItem, nextSquare, roller.Id, nextZ));
                            _rollerItemsMoved.Add(rItem.Id);
                        }
                    }
                }
                var rollerUser = _room.GetGameMap().GetRoomUsers(roller.Coordinate).FirstOrDefault();
                if (rollerUser != null && !rollerUser.IsWalking && nextRollerClear &&
                    _room.GetGameMap().IsValidStep(new(roller.GetX, roller.GetY), new(nextSquare.X, nextSquare.Y), true, false, true) &&
                    _room.GetGameMap().CanRollItemHere(nextSquare.X, nextSquare.Y) && _room.GetGameMap().GetFloorStatus(nextSquare) != 0)
                {
                    if (!_rollerUsersMoved.Contains(rollerUser.HabboId))
                    {
                        if (!nextSquareIsRoller)
                            nextZ = rollerUser.Z - roller.Definition.Height;
                        else
                            nextZ = rollerUser.Z;
                        rollerUser.IsRolling = true;
                        rollerUser.RollerDelay = 1;
                        _rollerMessages.Add(UpdateUserOnRoller(rollerUser, nextSquare, roller.Id, nextZ));
                        _rollerUsersMoved.Add(rollerUser.HabboId);
                    }
                }
            }
            _mRollerCycle = 0;
            return _rollerMessages;
        }
        _mRollerCycle++;
        return new();
    }

    // V2 runs whenever rollers are registered (it does not depend on the legacy GotRollers flag) and
    // the §14.9 planner sends each group's slides itself, before that group's hooks.
    private List<IServerPacket> CycleV2Rollers()
    {
        if (_rollers.IsEmpty) return new();
        if (_mRollerCycle < _mRollerSpeed && _mRollerSpeed != 0)
        {
            _mRollerCycle++;
            return new();
        }
        _room.GetGameMap().Navigation!.Executor.Rollers.Run(_rollers.Values.ToList());
        _mRollerCycle = 0;
        return new();
    }

    public IServerPacket UpdateItemOnRoller(Item pItem, Point nextCoord, uint pRolledId, double nextZ)
    {
        var mMessage = new SlideObjectBundleComposer(pItem.GetX, pItem.GetY, pItem.GetZ, nextCoord.X, nextCoord.Y, nextZ, pRolledId, 0, pItem.Id);
        SetFloorItem(pItem, nextCoord.X, nextCoord.Y, nextZ);
        return mMessage;
    }

    public IServerPacket UpdateUserOnRoller(RoomUser pUser, Point pNextCoord, uint pRollerId, double nextZ)
    {
        var mMessage = new SlideObjectBundleComposer(pUser.X, pUser.Y, pUser.Z, pNextCoord.X, pNextCoord.Y, nextZ, pRollerId, pUser.VirtualId, 0);
        _room.GetGameMap().UpdateUserMovement(new(pUser.X, pUser.Y), new(pNextCoord.X, pNextCoord.Y), pUser);
        _room.GetGameMap().GameMap[pUser.X, pUser.Y] = 1;
        pUser.X = pNextCoord.X;
        pUser.Y = pNextCoord.Y;
        pUser.Z = nextZ;
        _room.GetGameMap().GameMap[pUser.X, pUser.Y] = 0;
        if (pUser != null && pUser.GetClient() != null && pUser.GetClient().GetHabbo() != null)
        {
            var items = _room.GetGameMap().GetRoomItemForSquare(pNextCoord.X, pNextCoord.Y);
            foreach (var item in items.ToList())
            {
                if (item == null)
                    continue;
                _room.GetWired().TriggerEvent(WiredBoxType.TriggerWalkOnFurni, pUser.GetClient().GetHabbo(), item);
            }
            var roller = _room.GetRoomItemHandler().GetItem(pRollerId);
            if (roller != null) _room.GetWired().TriggerEvent(WiredBoxType.TriggerWalkOffFurni, pUser.GetClient().GetHabbo(), roller);
        }
        return mMessage;
    }

    private void SaveFurniture()
    {
        try
        {
            if (_movedItems.Count > 0)
            {
                _store.SaveMoved(_movedItems.Values.Where(item => !item.IsTemporary).Select(item => new RoomItemSave(
                    item.Id, item.GetX, item.GetY, item.GetZ, item.Rotation, item.ExtraData?.Serialize(), item.WallCoordinates,
                    item.IsWallItem && (!item.Definition.ItemName.Contains("wallpaper_single") || !item.Definition.ItemName.Contains("floor_single") ||
                        !item.Definition.ItemName.Contains("landscape_single")))).ToArray());
            }
        }
        catch (Exception e)
        {
            ExceptionLogger.LogCriticalException(e);
        }
    }

    public bool SetFloorItem(GameClient session, Item item, int newX, int newY, int newRot, bool newItem, bool onRoller, bool sendMessage, bool updateRoomUserStatuses = false, double height = -1, Plus.HabboHotel.Items.Wired.Modern.WiredCollisionPolicy? wiredCollision = null) =>
        PlaceFloor(session, item, newX, newY, newRot, newItem, onRoller, sendMessage, updateRoomUserStatuses, height, wiredCollision, null);

    // Prepared data is persisted inside the placement lock after every denial, then attached and published through the same path.
    public bool SetFloorItemData(GameClient session, Item item, Plus.HabboHotel.Items.DataFormat.IFurniObjectData data, Action persist) =>
        PlaceFloor(session, item, item.GetX, item.GetY, item.Rotation, false, false, true, false, -1, null, (data, persist));

    private bool PlaceFloor(GameClient session, Item item, int newX, int newY, int newRot, bool newItem, bool onRoller, bool sendMessage, bool updateRoomUserStatuses, double height, Plus.HabboHotel.Items.Wired.Modern.WiredCollisionPolicy? wiredCollision, (Plus.HabboHotel.Items.DataFormat.IFurniObjectData Data, Action Persist)? commit)
    {
        if (item.IsTemporary && (!OwnsTemporary(item) || session != null
            || !Plus.HabboHotel.Items.Wired.Modern.WiredRoomOperations.CanPlaceItem(_room, item, newX, newY, newRot,
                height == -1 ? null : height, collision: wiredCollision))) return false;
        bool HasBlockingUsers(int x, int y) => wiredCollision == null
            ? _room.GetGameMap().SquareHasUsers(x, y)
            : wiredCollision.BlocksUsers(_room.GetGameMap().GetRoomUsers(new(x, y)));
        var magic = MagicTileHeight.IsMagicTile(item.Definition.InteractionType);
        if (newItem)
        {
            if (item.IsWired)
            {
                if (item.Definition.WiredType == WiredBoxType.EffectRegenerateMaps &&
                    _room.GetRoomItemHandler().GetFloor.Count(x => x.Definition.WiredType == WiredBoxType.EffectRegenerateMaps) > 0)
                    return false;
            }
        }
        var map = _room.GetGameMap();
        if (newItem && item.IsFloorItem && map.Navigation != null) item.EnableNavigationSynchronization();
        var duplicate = false;
        lock (map.PlacementSync)
        {
            duplicate = newItem && _floorItems.ContainsKey(item.Id);
            if (!duplicate)
            {
                var itemsOnTile = GetFurniObjects(newX, newY);
                if (item.Definition.InteractionType == InteractionType.Roller && itemsOnTile.Count(x => x.Definition.InteractionType == InteractionType.Roller && x.Id != item.Id) > 0)
                    return false;
                var affectedTiles = Gamemap.GetAffectedTiles(item.Definition.Length, item.Definition.Width, newX, newY, newRot);
                var footprint = affectedTiles.Values.Select(tile => new Point(tile.X, tile.Y)).Append(new Point(newX, newY)).Distinct().ToArray();
                foreach (var tile in footprint)
                {
                    if (!map.ValidTile(tile.X, tile.Y)) return false;
                    if (magic && tile.X == map.Model.DoorX && tile.Y == map.Model.DoorY) return false;
                    var placement = map.ResolvePlacement(tile.X, tile.Y, item.Id, wiredCollision);
                    if (wiredCollision != null && map.GetCoordinatedItems(tile).Any(other => other.Id != item.Id
                        && (wiredCollision.BlockingFurni.Contains(other.Id)
                            || !magic && !placement.HasHelper && wiredCollision.BlocksFurni(other)))) return false;
                    if (!magic)
                    {
                        if (HasBlockingUsers(tile.X, tile.Y) && !item.Definition.IsSeat && !placement.HasHelper) return false;
                        if (height == -1 && !onRoller && !placement.CanStack) return false;

                    }
                }
                var newZ = height == -1
                    ? footprint.Max(tile => map.ResolvePlacement(tile.X, tile.Y, item.Id, wiredCollision).PlacementZ)
                    : height;
                if (!magic && height == -1 && !newItem && item.Rotation != newRot
                    && item.GetX == newX && item.GetY == newY
                    && !footprint.Any(tile => map.ResolvePlacement(tile.X, tile.Y, item.Id, wiredCollision).HasHelper))
                    newZ = Math.Max(newZ, item.GetZ);
                if (magic)
                {
                    var floorZ = footprint.Max(tile => (double)map.Model.SqFloorHeight[tile.X, tile.Y]);
                    newZ = MagicTileHeight.Clamp(newZ, floorZ);
                }
                if (newRot != 0 && newRot != 2 && newRot != 4 && newRot != 6 && newRot != 8 && !item.Definition.ExtraRot)
                    newRot = 0;
                if (newItem)
                {
                    // Initialize private geometry before membership or navigation publication.
                    item.SetPlacementState(newX, newY, newZ, affectedTiles, newRot);
                    item.RoomId = _room.RoomId;
                    if (item.IsFloorItem) duplicate = !AdmitFloorItem(item);
                    else if (item.IsWallItem) duplicate = !_wallItems.TryAdd(item.Id, item);
                }
                // Prepared data is written after every denial above and before any geometry, data, model or packet change below.
                if (commit is { } prepared && !duplicate)
                {
                    prepared.Persist();
                    item.ExtraData = prepared.Data;
                }
                if (!duplicate)
                {
                    if (!newItem)
                    {
                        map.RemoveFromMap(item, false);
                        item.SetPlacementState(newX, newY, newZ, affectedTiles, newRot);
                    }
                    map.AddItemToMap(item, false, newItem);
                }
            }
        }
        if (duplicate)
        {
            if (session != null)
                session.SendNotification(_language.TryGetValue("room.item.already_placed"));
            return true;
        }
        // Effects, Wired hooks, networking and persistence run only after the map commit.
        if (!newItem) map.RemoveItemEffects(item);
        map.AddItemEffects(item);
        if (!onRoller && session != null) item.Interactor.OnPlace(session, item);
        if (sendMessage)
        {
            if (newItem) _room.SendObject(item);
            else if (!onRoller) _room.SendPacket(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)));
        }
        UpdateItem(item);
        map.FlushPlacementUpdates();
        if (magic) updateRoomUserStatuses = true;
        if (newItem && item.IsFloorItem) _room.GetWired()?.AttachRoomItem(item);
        if (item.Definition.IsSeat)
            updateRoomUserStatuses = true;
        if (updateRoomUserStatuses)
            _room.GetRoomUserManager().UpdateUserStatusses();
        if (item.Definition.InteractionType == InteractionType.Tent || item.Definition.InteractionType == InteractionType.TentSmall)
        {
            _room.RemoveTent(item.Id);
            _room.AddTent(item.Id);
        }
        if (OwnsTemporary(item)) return true;
        _store.PlaceFloor(item.Id, _room.RoomId, item.GetX, item.GetY, item.GetZ, item.Rotation);
        return true;
    }


    public List<Item> GetFurniObjects(int x, int y) => _room.GetGameMap().GetCoordinatedItems(new(x, y));

    public bool SetFloorItem(Item item, int newX, int newY, double newZ)
    {
        if (_room != null && UsesV2Movement) return SetV2FloorItem(item, newX, newY, newZ);
        if (_room == null || item.IsTemporary && (!OwnsTemporary(item)
            || !Plus.HabboHotel.Items.Wired.Modern.WiredRoomOperations.CanMoveItem(_room, item, newX, newY, item.Rotation, newZ)))
            return false;
        var map = _room.GetGameMap();
        lock (map.PlacementSync)
        {
            if (MagicTileHeight.IsMagicTile(item.Definition.InteractionType))
            {
                var footprint = Gamemap.GetAffectedTiles(item.Definition.Length, item.Definition.Width, newX, newY, item.Rotation)
                    .Values.Select(tile => new Point(tile.X, tile.Y)).Append(new Point(newX, newY)).Distinct().ToArray();
                if (footprint.Any(tile => !map.ValidTile(tile.X, tile.Y)
                    || tile.X == map.Model.DoorX && tile.Y == map.Model.DoorY
                        && (newX != item.GetX || newY != item.GetY))) return false;
                newZ = MagicTileHeight.Clamp(newZ, footprint.Max(tile => (double)map.Model.SqFloorHeight[tile.X, tile.Y]));
            }
            map.RemoveFromMap(item, false);
            item.SetState(newX, newY, newZ, Gamemap.GetAffectedTiles(item.Definition.Length, item.Definition.Width, newX, newY, item.Rotation));
            map.AddItemToMap(item, false);
        }
        map.RemoveItemEffects(item);
        map.AddItemEffects(item);
        if (item.Definition.InteractionType == InteractionType.Toner)
            if (_room.TonerData == null)
                _room.TonerData = LoadToner(item.Id);
        UpdateItem(item);
        map.FlushPlacementUpdates();
        if (item.Definition.InteractionType == InteractionType.WalkMagicTile)
            _room.GetRoomUserManager().UpdateUserStatusses();
        return true;
    }

    private bool UsesV2Movement => _room.GetGameMap().Navigation?.UsesExecutor == true;

    // V2 in-place moves: validation, height resolution and the positional commit see one placement
    // state, so a placement that lands while this move waits for the lock is always revalidated.
    private bool SetV2FloorItem(Item item, int newX, int newY, double newZ)
    {
        FloorMove[] move;
        lock (_room.GetGameMap().PlacementSync)
        {
            if (!CanMoveFloorItem(item, newX, newY, newZ)) return false;
            move = [new(item, newX, newY, ResolveFloorZ(item, newX, newY, newZ))];
            CommitFloorMoves(move);
        }
        SettleFloorMoves(move);
        return true;
    }

    // V2 roller groups and v2 in-place moves split the setter above into preflight, one positional commit
    // and one settle step. These mirror its rules exactly; the legacy setter body stays the original.
    internal bool CanMoveFloorItem(Item item, int newX, int newY, double newZ,
        Plus.HabboHotel.Items.Wired.Modern.WiredCollisionPolicy? collision = null)
    {
        if (item.IsTemporary && (!OwnsTemporary(item)
            || !Plus.HabboHotel.Items.Wired.Modern.WiredRoomOperations.CanMoveItem(_room, item, newX, newY, item.Rotation, newZ, collision: collision)))
            return false;
        if (!MagicTileHeight.IsMagicTile(item.Definition.InteractionType)) return true;
        var map = _room.GetGameMap();
        return !MoveFootprint(item, newX, newY).Any(tile => !map.ValidTile(tile.X, tile.Y)
            || tile.X == map.Model.DoorX && tile.Y == map.Model.DoorY && (newX != item.GetX || newY != item.GetY));
    }

    // The in-place setter's height adjustment: helpers are clamped to their footprint's floor.
    internal double ResolveFloorZ(Item item, int newX, int newY, double newZ)
    {
        if (!MagicTileHeight.IsMagicTile(item.Definition.InteractionType)) return newZ;
        var map = _room.GetGameMap();
        var footprint = MoveFootprint(item, newX, newY).Where(tile => map.ValidTile(tile.X, tile.Y)).ToArray();
        return footprint.Length == 0 ? newZ
            : MagicTileHeight.Clamp(newZ, footprint.Max(tile => (double)map.Model.SqFloorHeight[tile.X, tile.Y]));
    }

    // Map and position writes only. Callers hold PlacementSync from their CanMoveFloorItem preflight on.
    internal void CommitFloorMoves(IReadOnlyList<FloorMove> moves)
    {
        var map = _room.GetGameMap();
        lock (map.PlacementSync)
        {
            foreach (var move in moves) map.RemoveFromMap(move.Item, false);
            foreach (var move in moves)
            {
                var item = move.Item;
                item.SetState(move.X, move.Y, move.Z, Gamemap.GetAffectedTiles(item.Definition.Length, item.Definition.Width, move.X, move.Y, item.Rotation));
                map.AddItemToMap(item, false);
            }
        }
    }

    // Deferred effects, persistence, placement flush and posture refresh, once for the whole set.
    internal void SettleFloorMoves(IReadOnlyList<FloorMove> moves)
    {
        if (moves.Count == 0) return;
        var map = _room.GetGameMap();
        foreach (var item in moves.Select(move => move.Item))
        {
            map.RemoveItemEffects(item);
            map.AddItemEffects(item);
            if (item.Definition.InteractionType == InteractionType.Toner && _room.TonerData == null)
                _room.TonerData = LoadToner(item.Id);
            UpdateItem(item);
        }
        map.FlushPlacementUpdates();
        if (moves.Any(move => move.Item.Definition.InteractionType == InteractionType.WalkMagicTile))
            _room.GetRoomUserManager().UpdateUserStatusses();
    }

    private static IEnumerable<Point> MoveFootprint(Item item, int x, int y)
        => Gamemap.GetAffectedTiles(item.Definition.Length, item.Definition.Width, x, y, item.Rotation)
            .Values.Select(tile => new Point(tile.X, tile.Y)).Append(new Point(x, y)).Distinct();

    public bool SetWallItem(GameClient session, Item item)
    {
        if (!item.IsWallItem || _wallItems.ContainsKey(item.Id))
            return false;
        if (_floorItems.ContainsKey(item.Id))
        {
            session.SendNotification(_language.TryGetValue("room.item.already_placed"));
            return true;
        }
        item.RoomId = _room.RoomId;
        item.Attach(_room, _interactors, _travelStore, _rewards);
        try
        {
            item.Interactor.OnPlace(session, item);
        }
        catch
        {
            item.Detach(_room);
            throw;
        }
        if (item.Definition.InteractionType == InteractionType.Moodlight)
        {
            if (_room.MoodlightData == null)
            {
                _room.MoodlightData = LoadMoodlight(item.Id);
                if (_room.MoodlightData != null)
                    item.LegacyDataString = _room.MoodlightData.GenerateExtraData();
            }
        }
        _store.PlaceWall(item.Id, _room.RoomId, item.GetX, item.GetY, item.GetZ, item.Rotation, item.WallCoordinates);
        _wallItems.TryAdd(item.Id, item);
        _room.SendObject(item);
        return true;
    }

    public void UpdateItem(Item item)
    {
        if (item == null || item.IsTemporary)
            return;
        if (!_movedItems.ContainsKey(item.Id))
            _movedItems.TryAdd(item.Id, item);
    }


    public void RemoveItem(Item item)
    {
        if (item == null)
            return;
        if (_movedItems.ContainsKey(item.Id))
            _movedItems.TryRemove(item.Id, out item);
        if (_rollers.ContainsKey(item.Id))
            _rollers.TryRemove(item.Id, out item);
    }

    public void OnCycle()
    {
        if (GotRollers || _room.GetGameMap().Navigation?.UsesExecutor == true && !_rollers.IsEmpty)
        {
            try
            {
                _room.SendPacket(CycleRollers());
            }
            catch //(Exception e)
            {
                // Logging.LogThreadException(e.ToString(), "rollers for room with ID " + room.RoomId);
                GotRollers = false;
            }
        }
        if (_roomItemUpdateQueue.Count > 0)
        {
            var addItems = new List<Item>();
            while (_roomItemUpdateQueue.Count > 0)
            {
                var item = (Item)null;
                if (_roomItemUpdateQueue.TryDequeue(out item))
                {
                    item.ProcessUpdates();
                    if (item.UpdateCounter > 0)
                        addItems.Add(item);
                }
            }
            foreach (var item in addItems.ToList())
            {
                if (item == null)
                    continue;
                _roomItemUpdateQueue.Enqueue(item);
            }
        }
    }

    // Admission serializes membership and record publication with pickup. Only
    // that transaction shares NavSync; callbacks do not.
    internal bool AdmitFloorItem(Item item)
    {
        if (_floorItems.TryGetValue(item.Id, out var admitted))
            return ReferenceEquals(admitted, item) && ReferenceEquals(item.GetRoom(), _room);
        item.Attach(_room, _interactors, _travelStore, _rewards);
        var inputs = _room.GetGameMap().Navigation?.Inputs;
        if (inputs == null)
        {
            if (_floorItems.TryAdd(item.Id, item)) return true;
            if (_floorItems.TryGetValue(item.Id, out admitted) && ReferenceEquals(admitted, item)) return true;
            item.Detach(_room);
            return false;
        }
        item.EnableNavigationSynchronization();
        lock (item.NavSync)
        {
            if (!_floorItems.TryAdd(item.Id, item))
            {
                if (_floorItems.TryGetValue(item.Id, out admitted) && ReferenceEquals(admitted, item)) return true;
                item.Detach(_room);
                return false;
            }
            inputs.Attach(item);
            return true;
        }
    }

    public List<Item> RemoveItems(GameClient session)
    {
        var items = new List<Item>();
        var owned = GetWallAndFloor.Where(item => item != null && !item.IsTemporary && item.UserId == session.GetHabbo().Id).ToList();
        // All boxes at once, before any item moves: a failure leaves every one placed with its settings.
        _room.GetWired()?.ResetRoomItems(owned);
        foreach (var item in owned)
        {
            if (item.IsFloorItem)
            {
                Item I;
                var inputs = _room.GetGameMap().Navigation?.Inputs;
                if (inputs == null) _floorItems.TryRemove(item.Id, out I);
                else
                {
                    lock (item.NavSync)
                    {
                        if (!_floorItems.TryRemove(new KeyValuePair<uint, Item>(item.Id, item))) continue;
                        inputs.Remove(item);
                        I = item;
                    }
                }
                _room.GetWired()?.DetachRoomItem(item);
                // TODO @80O: Items refactor
                session.GetHabbo().Inventory.Furniture.AddItem(I.ToInventoryItem());
                _room.SendPacket(new ObjectRemoveComposer(item.Id, item.IsTemporary, item.UserId));
            }
            else if (item.IsWallItem)
            {
                _wallItems.TryRemove(item.Id, out var I);
                // TODO @80O: Items refactor
                session.GetHabbo().Inventory.Furniture.AddItem(I.ToInventoryItem());
                _room.SendPacket(new ItemRemoveComposer(item.Id, item.UserId));
            }
            session.Send(new FurniListAddComposer(InventoryItemSnapshot.Capture(item.ToInventoryItem())));
            item.Detach(_room);
        }
        _rollers.Clear();
        _room.GetGameMap().GenerateMaps();
        _room.GetGameMap().FlushPlacementUpdates();
        return items;
    }


    public bool CheckPosItem(Item item, int newX, int newY, int newRot)
    {
        try
        {
            var dictionary = Gamemap.GetAffectedTiles(item.Definition.Length, item.Definition.Width, newX, newY, newRot);
            if (!_room.GetGameMap().ValidTile(newX, newY))
                return false;
            foreach (var coord in dictionary.Values)
            {
                if (_room.GetGameMap().Model.DoorX == coord.X && _room.GetGameMap().Model.DoorY == coord.Y)
                    return false;
            }
            if (_room.GetGameMap().Model.DoorX == newX && _room.GetGameMap().Model.DoorY == newY)
                return false;
            foreach (var coord in dictionary.Values)
            {
                if (!_room.GetGameMap().ValidTile(coord.X, coord.Y))
                    return false;
            }
            double num = _room.GetGameMap().Model.SqFloorHeight[newX, newY];
            if (item.Rotation == newRot && item.GetX == newX && item.GetY == newY && item.GetZ != num)
                return false;
            if (_room.GetGameMap().Model.SqState[newX, newY] != SquareState.Open)
                return false;
            foreach (var coord in dictionary.Values)
            {
                if (_room.GetGameMap().Model.SqState[coord.X, coord.Y] != SquareState.Open)
                    return false;
            }
            if (!item.Definition.IsSeat)
            {
                if (_room.GetGameMap().SquareHasUsers(newX, newY))
                    return false;
                foreach (var coord in dictionary.Values)
                {
                    if (_room.GetGameMap().SquareHasUsers(coord.X, coord.Y))
                        return false;
                }
            }
            var furniObjects = GetFurniObjects(newX, newY);
            var collection = new List<Item>();
            var list3 = new List<Item>();
            foreach (var coord in dictionary.Values)
            {
                var list4 = GetFurniObjects(coord.X, coord.Y);
                if (list4 != null)
                    collection.AddRange(list4);
            }
            if (furniObjects == null)
                furniObjects = new();
            list3.AddRange(furniObjects);
            list3.AddRange(collection);
            foreach (var i in list3)
            {
                if (i.Id != item.Id && !i.Definition.Stackable)
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }


    public ICollection<Item> GetRollers() => _rollers.Values;

    public void Dispose()
    {
        SaveFurniture();
        foreach (var item in GetWallAndFloor.ToList())
        {
            if (item == null)
                continue;
            if (item.IsFloorItem) _room.GetWired()?.DetachRoomItem(item);
            item.Destroy();
        }
        _movedItems.Clear();
        _rollers.Clear();
        _wallItems.Clear();
        _floorItems.Clear();
        _temporaryItems.Clear();
        _rollerItemsMoved.Clear();
        _rollerUsersMoved.Clear();
        _rollerMessages.Clear();
        _roomItemUpdateQueue = null;
    }
}

internal readonly record struct FloorMove(Item Item, int X, int Y, double Z);
