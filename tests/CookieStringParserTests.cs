using System.Linq;
using music.Services;
using Xunit;

namespace music.Tests;

public class CookieStringParserTests
{
    [Fact]
    public void SetCookieAttributesAreNotTreatedAsCookies()
    {
        const string cookie =
            "MUSIC_U=abc123; Max-Age=1296000; Expires=Wed, 09 Jun 2027 10:18:14 GMT; Path=/; NMTID=xyz";
        var pairs = CookieStringParser.Parse(cookie).ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal("abc123", pairs["MUSIC_U"]);
        Assert.Equal("xyz", pairs["NMTID"]);
        Assert.False(pairs.ContainsKey("Max-Age"));
        Assert.False(pairs.ContainsKey("Expires"));
        Assert.False(pairs.ContainsKey("Path"));
    }

    [Fact]
    public void CommaSeparatedSetCookieFragmentsAreParsed()
    {
        const string cookie = "MUSIC_U=abc, NMTID=xyz; Path=/, __csrf=token";
        var pairs = CookieStringParser.Parse(cookie).ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal("abc", pairs["MUSIC_U"]);
        Assert.Equal("xyz", pairs["NMTID"]);
        Assert.Equal("token", pairs["__csrf"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Path=/; Max-Age=1")]
    [InlineData("no-equals-here")]
    [InlineData("EMPTY=; OTHER=value")]
    public void InvalidFragmentsAreIgnored(string cookie)
    {
        var pairs = CookieStringParser.Parse(cookie).ToList();

        Assert.DoesNotContain(pairs, pair => pair.Key is "Path" or "Max-Age" or "EMPTY");
        Assert.DoesNotContain(pairs, pair => pair.Value.Length == 0);
    }
}
