using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public class SecureTokenTests
{
    [Fact]
    public void GeneratesUrlSafe256BitTokensThatFitTheAuthTicketColumn()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => SecureToken.Generate()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        Assert.All(tokens, token =>
        {
            Assert.Equal(43, token.Length);
            Assert.Matches("^[A-Za-z0-9_-]+$", token);
        });
    }

    [Fact]
    public void HashMatchesMySqlSha2Hex()
    {
        // SELECT SHA2('abc', 256)
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SecureToken.Hash("abc"));
        Assert.NotEqual(SecureToken.Hash("abc"), SecureToken.Hash("abd"));
    }
}
