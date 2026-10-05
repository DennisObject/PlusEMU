using System.Collections.Immutable;

namespace Plus.HabboHotel.Users.Inventory.Bots;

public sealed record BotInventorySnapshot(int Id, string Name, string Motto, string Gender, string Figure)
{
    public static ImmutableArray<BotInventorySnapshot> Capture(IEnumerable<Bot> bots) => bots
        .Select(bot => new BotInventorySnapshot(bot.Id, bot.Name, bot.Motto, bot.Gender, bot.Figure))
        .ToImmutableArray();
}
