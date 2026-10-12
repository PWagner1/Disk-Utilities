using DiskUtilities.Shared.Localization;

namespace DiskUtilities.Tests;

public class SuiteResourcesTests
{
    [Fact]
    public void Get_ReturnsEmbeddedEnglishStrings()
    {
        SuiteResources.EnsureLoaded("en");
        Assert.Equal("Disk Utilities", SuiteResources.Get("Suite.Title"));
        Assert.Equal("Launch", SuiteResources.Get("Suite.Launch"));
        Assert.Equal("missing.key", SuiteResources.Get("missing.key"));
        Assert.Equal("fallback", SuiteResources.Get("missing.key", "fallback"));
    }
}
