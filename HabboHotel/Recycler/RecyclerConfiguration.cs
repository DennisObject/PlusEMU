using System.Collections.Immutable;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Recycler;

public sealed record RecyclerPrize(int Id, uint ItemId);
public sealed record RecyclerLevel(int Id, int Chance, ImmutableArray<RecyclerPrize> Prizes);
public sealed record RecyclerConfiguration(bool Enabled, int Slots, int CooldownSeconds, ImmutableArray<RecyclerLevel> Levels)
{
    public static readonly RecyclerConfiguration Closed = new(false, 5, 0, []);

    public bool Matches(RecyclerConfiguration other) => Enabled == other.Enabled && Slots == other.Slots
        && CooldownSeconds == other.CooldownSeconds && Levels.Length == other.Levels.Length
        && Levels.Zip(other.Levels).All(pair => pair.First.Id == pair.Second.Id && pair.First.Chance == pair.Second.Chance
            && pair.First.Prizes.SequenceEqual(pair.Second.Prizes));
}

public readonly record struct RecyclerInput(uint Id, uint ItemId);
public sealed record RecycledBox(uint Id, string ExtraData);
public sealed record RecyclerProduct(string Code, string Type, int SpriteId);
public sealed record RecyclerPrizeList(int Level, int Chance, ImmutableArray<RecyclerProduct> Products);

[Singleton]
public interface IRecyclerRandom
{
    int Next(int exclusiveMaximum);
}

public sealed class RecyclerRandom : IRecyclerRandom
{
    public int Next(int exclusiveMaximum) => Random.Shared.Next(exclusiveMaximum);
}
