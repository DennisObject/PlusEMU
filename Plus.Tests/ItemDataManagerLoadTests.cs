using System.Data;
using Microsoft.Extensions.Logging;
using Plus.HabboHotel.Items;
using Xunit;

namespace Plus.Tests;

public class ItemDataManagerLoadTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void FurnitureBooleanCellsSupportNativeAndLegacyValues(object value, bool expected) =>
        Assert.Equal(expected, FurnitureNumbers.BooleanFromCell(value));

    [Fact]
    public void ABadRowIsLoggedAndSkippedWithoutWaitingForTheConsole()
    {
        var logger = new RecordingLogger();
        var manager = new ItemDataManager(logger, null!);
        var table = new DataTable();
        foreach (var column in new[] { "id", "sprite_id", "item_name", "public_name", "type", "width", "length", "stack_height", "can_stack", "is_walkable",
                     "can_sit", "allow_recycle", "allow_trade", "allow_marketplace_sell", "allow_gift", "allow_inventory_stack", "interaction_type", "wired_id",
                     "behaviour_data", "interaction_modes_count", "vending_ids", "height_adjustable", "effect_id", "is_rare", "extra_rot" })
            table.Columns.Add(column, typeof(string));
        table.Rows.Add("1", "13", "shelves_norja", "Bookcase", "s", "1", "1", "1", "1", "0", "0", "1", "1", "1", "1", "1", "vendingmachine", "0", "0", "1", "1003,1004", "0", "0", "0", "0");
        table.Rows.Add("2", "14", "broken", "Broken", "s", "1", "1", "1", "1", "0", "0", "1", "1", "1", "1", "1", "default", "0", "0", "1", "1,abc", "0", "0", "0", "0");

        manager.Load(table);

        Assert.Equal([1u], manager.Items.Keys);
        Assert.Equal([1003, 1004], manager.Items[1].VendingIds);
        Assert.Equal(1u, manager.Gifts[13]);
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("#2", error.Message);
    }

    private sealed class RecordingLogger : ILogger<ItemDataManager>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
