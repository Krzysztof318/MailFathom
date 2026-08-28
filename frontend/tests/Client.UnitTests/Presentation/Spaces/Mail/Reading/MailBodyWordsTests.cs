// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Mail;
using MailFathom.Client.Presentation.Spaces.Mail.Reading;

namespace MailFathom.Client.UnitTests.Presentation.Spaces.Mail.Reading;

/// <summary>The sentences the reading pane composes, and which of them a link's verdict is answered with.</summary>
/// <remarks>
/// A warning that states a finding nobody made is worse than no warning: a reader who checks the two spellings against
/// the target, sees they agree, and was told they disagree learns to distrust the bar — and the bar is then worth
/// nothing on the message where the finding is real. So the verdict picks the sentence, and each of the three grounds
/// the bar opens on says what it actually found.
/// </remarks>
public sealed class MailBodyWordsTests
{
    /// <summary>A mismatch the deployment established is the one case the mismatch sentence is stated for.</summary>
    [Fact]
    public void WarningAbout_ALinkWhoseTextNamesAnotherHost_StatesTheMismatch()
    {
        // Arrange
        var link = new MailBodyLink(
            "https://payments.test/",
            "payments.test",
            AsciiHost: null,
            MailBodyLinkDeception.DisplayedHostDiffers);

        // Act
        var warning = Words().WarningAbout(link);

        // Assert
        Assert.Equal("mismatch", warning);
    }

    /// <summary>A host in two spellings with no mismatch found says that, rather than claiming one was.</summary>
    [Theory]
    [InlineData(MailBodyLinkDeception.None)]
    [InlineData(MailBodyLinkDeception.NotApplicable)]
    public void WarningAbout_AHostWrittenInTwoSpellingsAndNoMismatchFound_StatesTheSpelling(
        MailBodyLinkDeception deception)
    {
        // Arrange
        var link = new MailBodyLink("https://xn--pyments-8va.test/", "pаyments.test", "xn--pyments-8va.test", deception);

        // Act
        var warning = Words().WarningAbout(link);

        // Assert
        Assert.Equal("homograph", warning);
    }

    /// <summary>A verdict this build cannot read says so, because nothing about the link was actually checked here.</summary>
    [Fact]
    public void WarningAbout_AVerdictThisBuildCannotRead_SaysNothingWasChecked()
    {
        // Arrange
        var link = new MailBodyLink(
            "https://payments.test/",
            "payments.test",
            AsciiHost: null,
            MailBodyLinkDeception.Unrecognized);

        // Act
        var warning = Words().WarningAbout(link);

        // Assert
        Assert.Equal("unjudged", warning);
    }

    /// <summary>A mismatch and a second spelling together state the mismatch, which is the stronger of the two findings.</summary>
    [Fact]
    public void WarningAbout_AMismatchOnAHostWrittenInTwoSpellings_StatesTheMismatch()
    {
        // Arrange
        var link = new MailBodyLink(
            "https://xn--pyments-8va.test/",
            "pаyments.test",
            "xn--pyments-8va.test",
            MailBodyLinkDeception.DisplayedHostDiffers);

        // Act
        var warning = Words().WarningAbout(link);

        // Assert
        Assert.Equal("mismatch", warning);
    }

    /// <summary>Sentences named after what each says, so a wrong choice reads as the wrong finding rather than as text.</summary>
    private static MailBodyWords Words() => new(
        "unsupported",
        "undrawn",
        "title",
        "displayed",
        "target",
        "punycode",
        "mismatch",
        "homograph",
        "unjudged",
        "open",
        "cancel");
}
