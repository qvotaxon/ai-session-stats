using Shouldly;
using SessionInsights.Domain.Sessions;
using Xunit;

namespace SessionInsights.Application.Tests;

public sealed class SessionTitleTests
{
    [Theory]
    [InlineData("<command-name>/clear</command-name>\n<command-message>clear</command-message>\n<command-args></command-args>", "/clear")]
    [InlineData("<command-message>x</command-message><command-name>/model</command-name><command-args>opus</command-args>", "/model opus")]
    [InlineData("Fix the <b>login</b>   bug\nnow", "Fix the login bug now")]
    [InlineData("<local-command-stdout>Set model to opus</local-command-stdout>", "Untitled session")]
    [InlineData("<system-reminder>Caveat: ignore</system-reminder>", "Untitled session")]
    [InlineData("   ", "Untitled session")]
    [InlineData(null, "Untitled session")]
    [InlineData("a < b and c > d", "a < b and c > d")]
    public void Titles_are_cleaned(string? raw, string expected) => Session.TruncateTitle(raw).ShouldBe(expected);

    [Fact]
    public void Cleaned_titles_are_still_truncated() =>
        Session.TruncateTitle("<command-name>" + new string('x', 300) + "</command-name>").Length.ShouldBe(Session.MaxTitleLength);
}
