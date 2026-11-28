using SessionInsights.Application.Common;
using Shouldly;
using Xunit;

namespace SessionInsights.Application.Tests;

public sealed class SessionInsightsOptionsTests
{
    [Theory]
    [InlineData("msdc-harness", "msdc-plugins", true)]
    [InlineData("acme.harness_2", "acme-plugins", true)]
    [InlineData("", "msdc-plugins", false)]
    [InlineData("msdc-harness", "--help", false)]
    [InlineData("msdc harness", "msdc-plugins", false)]
    [InlineData("msdc-harness", "a@b", false)]
    public void Harness_names_must_be_plain_identifiers(string plugin, string marketplace, bool valid)
    {
        var options = new SessionInsightsOptions { HarnessPluginName = plugin, HarnessMarketplace = marketplace };

        options.HasValidHarnessNames().ShouldBe(valid);
    }
}
