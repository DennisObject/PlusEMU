namespace Plus.HabboHotel.Habbicons;

public enum HabbiconAction { Buy, BuyCollection, Claim, Favorite, Unfavorite }

public static class HabbiconState
{
    public const int NotOwned = 0, Claimable = 1, Owned = 2, Favorite = 3, Unavailable = 4, Reward = 5;
}

public sealed record HabbiconItem(int Id, string Name, int CollectionId, int State, int Credits, int Points, int PointsType)
{
    public bool Owned => State is HabbiconState.Owned or HabbiconState.Favorite;
    public bool Collected => State >= HabbiconState.Claimable && State <= HabbiconState.Favorite;
    public bool Purchasable => State == HabbiconState.NotOwned && (Credits > 0 || Points > 0);
}

public sealed record HabbiconCollection(int Id, string Name, bool Completed, int RewardId, int RewardState,
    int Credits, int Points, int PointsType, IReadOnlyList<HabbiconItem> Items);

public sealed record HabbiconSnapshot(IReadOnlyList<HabbiconCollection> Collections,
    IReadOnlyDictionary<int, HabbiconItem> Items, IReadOnlyList<int> Recent, IReadOnlyList<int> Unseen)
{
    public HabbiconItem RequireItem(int id) => Items.TryGetValue(id, out var item) ? item : throw new HabbiconRejected(1);
}

public sealed record HabbiconBalances(int Credits, int Duckets, int Diamonds);
public sealed record HabbiconChange(HabbiconSnapshot Snapshot, IReadOnlyList<HabbiconItem> Changed, HabbiconBalances? Balances);

public sealed class HabbiconRejected(int code) : InvalidOperationException($"Habbicon action rejected: {code}")
{
    public int Code { get; } = code;
}
