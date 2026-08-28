namespace PaceDesktop.Tests;

public class LauncherTests
{
    [Fact]
    public void Fixture_DisablesTheBrowserLaunch()
    {
        // If this setting is ever dropped, running the test suite opens a browser tab per
        // fixture. The assertion exists to keep that from regressing silently.
        using var server = new ServerFixture();

        Assert.Equal("false", server.Configuration["OpenBrowser"]);
    }
}
