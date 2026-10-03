using System.Collections.Concurrent;
using System.Drawing;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Core;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;

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
        item.BindTemporaryRoom(_room);
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
    private readonly ConcurrentDictionary<uint, Item> _wallItems;
    private int _mRollerCycle;
    private int _mRollerSpeed;

    private ConcurrentQueue<Item> _roomItemUpdateQueue;

    public int HopperCount;

    public RoomItemHandling(Room room)
    {
        _room = room;
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

    public void LoadFurniture()
    {
        if (_floorItems.Count > 0)
        {
            foreach (var previous in _floorItems.Values) _room.GetWired()?.DetachRoomItem(previous);
            _floorItems.Clear();
        }
        if (_wallItems.Count > 0)
            _wallItems.Clear();
        _temporaryItems.Clear();
        var items = ItemLoader.GetItemsForRoom(_room.Id, _room);
        foreach (var item in items.ToList())
        {
            if (item == null)
                continue;
            if (item.UserId == 0)
            {
                using var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor();
                dbClient.SetQuery("UPDATE `items` SET `user_id` = @UserId WHERE `id` = @ItemId LIMIT 1");
                dbClient.AddParameter("ItemId", item.Id);
                dbClient.AddParameter("UserId", _room.OwnerId);
                dbClient.RunQuery();
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
                    using (var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor())
                    {
                        dbClient.RunQuery($"UPDATE `items` SET `room_id` = '0' WHERE `id` = '{item.Id}' LIMIT 1");
                    }
                    var client = PlusEnvironment.Game.ClientManager.GetClientByUserId(item.UserId);
                    if (client != null)
                    {
                        client.GetHabbo().Inventory.Furniture.AddItem(item.ToInventoryItem());
                        client.Send(new FurniListUpdateComposer());
                    }
                    continue;
                }
                if (!_floorItems.ContainsKey(item.Id))
                    _floorItems.TryAdd(item.Id, item);
            }
            else if (item.IsWallItem)
            {
                if (string.IsNullOrWhiteSpace(item.WallCoordinates))
                {
                    using (var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor())
                    {
                        dbClient.SetQuery($"UPDATE `items` SET `wall_pos` = @WallPosition WHERE `id` = '{item.Id}' LIMIT 1");
                        dbClient.AddParameter("WallPosition", ":w=0,2 l=11,53 l");
                        dbClient.RunQuery();
                    }
                    item.WallCoordinates = ":w=0,2 l=11,53 l";
                }
                try
                {
                    item.WallCoordinates = WallPositionCheck($":{item.WallCoordinates.Split(':')[1]}");
                }
                catch
                {
                    using (var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor())
                    {
                        dbClient.SetQuery($"UPDATE `items` SET `wall_pos` = @WallPosition WHERE `id` = '{item.Id}' LIMIT 1");
                        dbClient.AddParameter("WallPosition", ":w=0,2 l=11,53 l");
                        dbClient.RunQuery();
                    }
                    item.WallCoordinates = ":w=0,2 l=11,53 l";
                }
                if (!_wallItems.ContainsKey(item.Id))
                    _wallItems.TryAdd(item.Id, item);
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
                    _room.MoodlightData = new(item.Id);
            }
            else if (item.Definition.InteractionType == InteractionType.Toner)
            {
                if (_room.TonerData == null)
                    _room.TonerData = new(item.Id);
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

    public Item GetItem(uint pId)
    {
        if (_floorItems != null && _floorItems.ContainsKey(pId))
        {
            Item item = null;
            if (_floorItems.TryGetValue(pId, out item))
                return item;
        }
        else if (_wallItems != null && _wallItems.ContainsKey(pId))
        {
            Item item = null;
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
        if (item.IsFloorItem)
            _room.SendPacket(new ObjectRemoveComposer(item, item.UserId));
        else if (item.IsWallItem)
            _room.SendPacket(new ItemRemoveComposer(item, item.UserId));

        //TODO: Recode this specific part
        if (item.IsWallItem)
            _wallItems.TryRemove(item.Id, out item);
        else
        {
            _room.GetWired()?.DetachRoomItem(item);
            _floorItems.TryRemove(item.Id, out item);
            //mFloorItems.OnCycle();
            _room.GetGameMap().RemoveFromMap(item);
        }
        RemoveItem(item);
        _room.GetGameMap().GenerateMaps();
        _room.GetGameMap().FlushPlacementUpdates();
        _room.GetRoomUserManager().UpdateUserStatusses();
    }

    private List<IServerPacket> CycleRollers()
    {
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
                using var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor();
                foreach (var item in _movedItems.Values.ToList())
                {
                    if (item.IsTemporary) continue;
                    var serialized = item.ExtraData?.Serialize();
                    if (!string.IsNullOrEmpty(serialized))
                    {
                        dbClient.SetQuery($"UPDATE `items` SET `extra_data` = @edata{item.Id} WHERE `id` = '{item.Id}' LIMIT 1");
                        dbClient.AddParameter($"edata{item.Id}", serialized);
                        dbClient.RunQuery();
                    }
                    if (item.IsWallItem && (!item.Definition.ItemName.Contains("wallpaper_single") || !item.Definition.ItemName.Contains("floor_single") ||
                                            !item.Definition.ItemName.Contains("landscape_single")))
                    {
                        dbClient.SetQuery($"UPDATE `items` SET `wall_pos` = @wallPos WHERE `id` = '{item.Id}' LIMIT 1");
                        dbClient.AddParameter("wallPos", item.WallCoordinates);
                        dbClient.RunQuery();
                    }
                    dbClient.RunQuery($"UPDATE `items` SET `x` = '{item.GetX}', `y` = '{item.GetY}', `z` = '{item.GetZ}', `rot` = '{item.Rotation}' WHERE `id` = '{item.Id}' LIMIT 1");
                }
            }
        }
        catch (Exception e)
        {
            ExceptionLogger.LogCriticalException(e);
        }
    }

    public bool SetFloorItem(GameClient session, Item item, int newX, int newY, int newRot, bool newItem, bool onRoller, bool sendMessage, bool updateRoomUserStatuses = false, double height = -1, Plus.HabboHotel.Items.Wired.Modern.WiredCollisionPolicy? wiredCollision = null)
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
                    if (item.IsFloorItem) duplicate = !_floorItems.TryAdd(item.Id, item);
                    else if (item.IsWallItem) duplicate = !_wallItems.TryAdd(item.Id, item);
                }
                if (!duplicate)
                {
                    if (!newItem) map.RemoveFromMap(item, false);
                    item.Rotation = newRot;
                    item.SetState(newX, newY, newZ, affectedTiles);
                    if (newItem) item.RoomId = _room.RoomId;
                    map.AddItemToMap(item, false, newItem);
                }
            }
        }
        if (duplicate)
        {
            if (session != null)
                session.SendNotification(PlusEnvironment.LanguageManager.TryGetValue("room.item.already_placed"));
            return true;
        }
        // Effects, Wired hooks, networking and persistence run only after the map commit.
        if (!newItem) map.RemoveItemEffects(item);
        map.AddItemEffects(item);
        if (!onRoller && session != null) item.Interactor.OnPlace(session, item);
        if (sendMessage)
        {
            if (newItem) _room.SendObject(item);
            else if (!onRoller) _room.SendPacket(new ObjectUpdateComposer(item));
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
        using var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor();
        dbClient.RunQuery($"UPDATE `items` SET `room_id` = '{_room.RoomId}', `x` = '{item.GetX}', `y` = '{item.GetY}', `z` = '{item.GetZ}', `rot` = '{item.Rotation}' WHERE `id` = '{item.Id}' LIMIT 1");
        return true;
    }


    public List<Item> GetFurniObjects(int x, int y) => _room.GetGameMap().GetCoordinatedItems(new(x, y));

    public bool SetFloorItem(Item item, int newX, int newY, double newZ)
    {
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
                _room.TonerData = new(item.Id);
        UpdateItem(item);
        map.FlushPlacementUpdates();
        if (item.Definition.InteractionType == InteractionType.WalkMagicTile)
            _room.GetRoomUserManager().UpdateUserStatusses();
        return true;
    }

    public bool SetWallItem(GameClient session, Item item)
    {
        if (!item.IsWallItem || _wallItems.ContainsKey(item.Id))
            return false;
        if (_floorItems.ContainsKey(item.Id))
        {
            session.SendNotification(PlusEnvironment.LanguageManager.TryGetValue("room.item.already_placed"));
            return true;
        }
        item.RoomId = _room.RoomId;
        item.Interactor.OnPlace(session, item);
        if (item.Definition.InteractionType == InteractionType.Moodlight)
        {
            if (_room.MoodlightData == null)
            {
                _room.MoodlightData = new(item.Id);
                item.LegacyDataString = _room.MoodlightData.GenerateExtraData();
            }
        }
        using (var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor())
        {
            dbClient.SetQuery(
                $"UPDATE `items` SET `room_id` = '{_room.RoomId}', `x` = '{item.GetX}', `y` = '{item.GetY}', `z` = '{item.GetZ}', `rot` = '{item.Rotation}', `wall_pos` = @WallPos WHERE `id` = '{item.Id}' LIMIT 1");
            dbClient.AddParameter("WallPos", item.WallCoordinates);
            dbClient.RunQuery();
        }
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
        if (GotRollers)
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

    public List<Item> RemoveItems(GameClient session)
    {
        var items = new List<Item>();
        foreach (var item in GetWallAndFloor.ToList())
        {
            if (item == null || item.IsTemporary || item.UserId != session.GetHabbo().Id)
                continue;
            if (item.IsFloorItem)
            {
                _floorItems.TryRemove(item.Id, out var I);
                // TODO @80O: Items refactor
                session.GetHabbo().Inventory.Furniture.AddItem(I.ToInventoryItem());
                _room.SendPacket(new ObjectRemoveComposer(item, item.UserId));
            }
            else if (item.IsWallItem)
            {
                _wallItems.TryRemove(item.Id, out var I);
                // TODO @80O: Items refactor
                session.GetHabbo().Inventory.Furniture.AddItem(I.ToInventoryItem());
                _room.SendPacket(new ItemRemoveComposer(item, item.UserId));
            }
            session.Send(new FurniListAddComposer(item.ToInventoryItem()));
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