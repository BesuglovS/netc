using NetcShared;
using Xunit;

namespace NetcTests;

public class SiteFilterCoreTests
{
    [Theory]
    [InlineData("https://WWW.Example.com/path?x=1", "example.com")]
    [InlineData("*.school.ru", "school.ru")]
    [InlineData("Example.COM:8443", "example.com")]
    [InlineData("sub.example.com", "sub.example.com")]
    [InlineData("", "")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, SiteFilterCore.Normalize(input));

    [Fact]
    public void Matches_Subdomains()
    {
        Assert.True(SiteFilterCore.Matches("example.com", "example.com"));
        Assert.True(SiteFilterCore.Matches("example.com", "a.b.example.com"));
        Assert.False(SiteFilterCore.Matches("example.com", "notexample.com"));
    }

    [Fact]
    public void Blacklist_BlocksListed_AllowsOther()
    {
        var sites = new[] { "vk.com" };
        Assert.True(SiteFilterCore.IsBlocked(SiteFilterMode.Blacklist, sites, "m.vk.com"));
        Assert.False(SiteFilterCore.IsBlocked(SiteFilterMode.Blacklist, sites, "ok.ru"));
    }

    [Fact]
    public void Whitelist_AllowsListed_BlocksOther_ButNotLocal()
    {
        var sites = new[] { "school.ru" };
        Assert.False(SiteFilterCore.IsBlocked(SiteFilterMode.Whitelist, sites, "school.ru"));
        Assert.True(SiteFilterCore.IsBlocked(SiteFilterMode.Whitelist, sites, "youtube.com"));
        // Локальная сеть не фильтруется даже в белом списке.
        Assert.False(SiteFilterCore.IsBlocked(SiteFilterMode.Whitelist, sites, "10.12.6.16"));
        Assert.False(SiteFilterCore.IsBlocked(SiteFilterMode.Whitelist, sites, "r114-01"));
        Assert.False(SiteFilterCore.IsBlocked(SiteFilterMode.Whitelist, sites, "localhost"));
    }

    [Fact]
    public void Off_NeverBlocks() =>
        Assert.False(SiteFilterCore.IsBlocked(SiteFilterMode.Off, new[] { "x.com" }, "x.com"));
}
