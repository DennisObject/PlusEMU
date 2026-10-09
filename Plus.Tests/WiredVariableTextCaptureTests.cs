using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableTextCaptureTests
{
    [Fact]
    public void NumericAndTextCapturesRequireWholeValidValuesBeforeMatch()
    {
        WiredVariableCapturer[] capturers = [new(10, "score", null), new(11, "team", new Dictionary<int, string> { [2] = "blue team" })];
        Assert.True(WiredVariableTextCapture.TryMatch("set #score# for #team#", "SET -12 for blue team", 1, capturers, out var values));
        Assert.Equal(-12, values[10]);
        Assert.Equal(2, values[11]);
        Assert.True(WiredVariableTextCapture.TryMatch("set #score# for #team#", "set 2147483648 for blue team", 1, capturers, out values));
        Assert.Equal(2147483648L, values[10]);
        Assert.False(WiredVariableTextCapture.TryMatch("set #score# for #team#", "set 9223372036854775808 for blue team", 1, capturers, out values));
        Assert.Empty(values);
        Assert.False(WiredVariableTextCapture.TryMatch("#score#", "3 words", 1, capturers, out _));
        Assert.True(WiredVariableTextCapture.TryMatch("", "+42", 2, [capturers[0]], out values));
        Assert.Equal(42, values[10]);
    }
    [Fact]
    public void AdjacentCaptureUsesBoundedValidPartitionsAndRepeatedNamesFollowStackOrdering()
    {
        WiredVariableCapturer[] capturers = [new(10, "a", new Dictionary<int, string> { [1] = "red" }), new(11, "b", null)];
        Assert.True(WiredVariableTextCapture.TryMatch("#a##b#", "red123", 1, capturers, out var values));
        Assert.Equal(1, values[10]);
        Assert.Equal(123, values[11]);
        Assert.False(WiredVariableTextCapture.TryMatch("#a##b#", "redbad", 1, capturers, out _));
        Assert.True(WiredVariableTextCapture.TryMatch("#n#", "42", 1, [new(1, "n", null), new(2, "N", null)], out values));
        Assert.False(values.ContainsKey(1));
        Assert.Equal(42, values[2]);
        Assert.False(WiredVariableTextCapture.TryMatch("#n#", new string('1', 1001), 1, [new(1, "n", null)], out _));
    }
}
