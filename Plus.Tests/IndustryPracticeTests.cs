using System.Globalization;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Core.FigureData;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.Utilities;
using Xunit;

namespace Plus.Tests;

public class IndustryPracticeTests
{
    [Fact]
    public void FigureFilterRejectsCharactersOutsideTheAllowedSet()
    {
        Assert.Equal("abc-12.z", PlusEnvironment.FilterFigure("abc-12.z"));
        Assert.Equal(IFigureDataManager.DefaultFigure, PlusEnvironment.FilterFigure("ABC"));
        Assert.Equal(IFigureDataManager.DefaultFigure, PlusEnvironment.FilterFigure("a b"));
        Assert.Equal(IFigureDataManager.DefaultFigure, PlusEnvironment.FilterFigure("ü"));
    }

    [Fact]
    public void NameFilterMatchesLettersDigitsDotAndDashOnly()
    {
        Assert.True(StringCharFilter.IsValid("Bob-1.2"));
        Assert.True(StringCharFilter.IsValidAlphaNumeric("Bob12"));
        Assert.False(StringCharFilter.IsValid("Bob 1"));
        Assert.False(StringCharFilter.IsValidAlphaNumeric("Bob-1"));
        Assert.False(StringCharFilter.IsValid(""));
        Assert.False(StringCharFilter.IsValidAlphaNumeric(' '));
        Assert.True(StringCharFilter.IsValidAlphaNumeric('Z'));
    }

    [Fact]
    public void ChatEscapeRemovesControlCharactersAndCanKeepBreaks()
    {
        Assert.Equal("hi there", StringCharFilter.Escape("hi\u0007 there"));
        Assert.Equal("hi there", StringCharFilter.Escape("hi\nthere"));
        Assert.Equal("hi\nthere", StringCharFilter.Escape("hi\nthere", allowBreaks: true));
        Assert.Equal("", StringCharFilter.Escape("   "));
    }

    [Fact]
    public void FloorHeightStaysDottedWhenTheProcessCultureUsesAComma()
    {
        var item = new Item
        {
            Id = 4,
            UserId = 2,
            GetZ = 1.5,
            Definition = new ItemDefinition
            {
                SpriteId = 3,
                Height = 0.25,
                Modes = 1,
                InteractionType = InteractionType.None,
                ItemName = "",
                PublicName = "",
                VendingIds = new List<int>(),
                AdjustableHeights = new List<double>()
            }
        };
        var packet = new RecordingPacket();

        UsingCulture("de-DE", () => packet.Serialize(RoomItemSnapshot.Capture(item)));

        Assert.Equal("1.5", packet.Writes[5]);
        Assert.Equal("0.25", packet.Writes[6]);
    }

    [Fact]
    public void FurnitureNumbersIgnoreTheProcessCulture()
    {
        UsingCulture("de-DE", () =>
        {
            Assert.Equal(1.5, FurnitureNumbers.Parse("1.5"));
            Assert.Equal(10, FurnitureNumbers.ParseInt("10"));
            Assert.Equal(0.5, FurnitureNumbers.FromCell("0.5"));
        });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(499, false)]
    [InlineData(500, true)]
    public void RoomCycleStartsWhenHalfASecondHasPassed(int elapsedMs, bool due)
    {
        var last = DateTimeOffset.Parse("2026-10-01T12:00:00+00:00");
        Assert.Equal(due, RoomCycle.IsDue(last, last.AddMilliseconds(elapsedMs)));
    }

    [Fact]
    public void RoomCycleResetsLagWhenThePreviousPassFinished()
    {
        var tick = RoomCycle.Next(workInProgress: false, lag: 4);
        Assert.True(tick.Start);
        Assert.Equal(0, tick.Lag);
        Assert.False(tick.Crashed);
    }

    [Fact]
    public void RoomCycleCrashesOnTheThirtiethMissedPass()
    {
        var stillWorking = RoomCycle.Next(workInProgress: true, lag: 28);
        var crashed = RoomCycle.Next(workInProgress: true, lag: 29);

        Assert.False(stillWorking.Crashed);
        Assert.Equal(29, stillWorking.Lag);
        Assert.True(crashed.Crashed);
        Assert.Equal(30, crashed.Lag);
        Assert.False(crashed.Start);
    }

    [Fact]
    public void TryCrackCountsEachHitOnceUnderOverlap()
    {
        var data = new CrackableDataFormat();
        data.Store("closed\n0\n40");

        Parallel.For(0, 80, _ => data.TryCrack());

        Assert.Equal(40u, data.Hits);
        Assert.Equal(40u, data.Target);
    }

    private static void UsingCulture(string name, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private sealed class RecordingPacket : Plus.HabboHotel.GameClients.IOutgoingPacket
    {
        public List<object> Writes { get; } = new();
        public int MessageId
        {
            get; set;
        }
        public ReadOnlyMemory<byte> Buffer => ReadOnlyMemory<byte>.Empty;
        public void WriteByte(byte value) => Writes.Add(value);
        public void WriteShort(short value) => Writes.Add(value);
        public void WriteInt(int value) => Writes.Add(value);
        public void WriteInteger(int value) => Writes.Add(value);
        public void WriteUInt(uint value) => Writes.Add(value);
        public void WriteUInteger(uint value) => Writes.Add(value);
        public void WriteBool(bool value) => Writes.Add(value);
        public void WriteBoolean(bool value) => Writes.Add(value);
        public void WriteString(string value) => Writes.Add(value);
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
