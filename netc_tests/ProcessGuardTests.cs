using NetcShared;
using Xunit;

namespace NetcTests;

public class ProcessGuardTests
{
    [Theory]
    [InlineData("explorer")]
    [InlineData("EXPLORER")]
    [InlineData("NetcServer.Agent")]
    [InlineData("netcserver.watchdog")]
    [InlineData("svchost")]
    [InlineData("lsass")]
    public void Protected_Names(string name) => Assert.True(ProcessGuard.IsProtected(name));

    [Theory]
    [InlineData("notepad")]
    [InlineData("chrome")]
    [InlineData("")]
    [InlineData(null)]
    public void NotProtected_Names(string? name) => Assert.False(ProcessGuard.IsProtected(name));
}
