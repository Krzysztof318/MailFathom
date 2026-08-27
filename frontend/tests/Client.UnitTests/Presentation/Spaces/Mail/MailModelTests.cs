// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Presentation.Spaces.Mail;
using MailFathom.Client.Session;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Spaces.Mail;

/// <summary>The Mail space, which reads whether it may be offered at all and leaves the mailboxes to the tree.</summary>
public sealed class MailModelTests
{
    /// <summary>
    /// The space reads whether it may be offered from the session rather than from a request the deployment refused,
    /// which is what keeps a credential that may not read mail off a screen that would have failed on its own terms.
    /// </summary>
    [Fact]
    public async Task WithholdsMail_AGrantNotCarryingReading_SaysSoRatherThanLeavingTheOfferToBeInverted()
    {
        // Arrange
        using var withheld = SessionOffering("mailfathom.mail.ask");
        await using var withheldModel = new MailModel(withheld);

        using var offered = SessionOffering("mailfathom.mail.read");
        await using var offeredModel = new MailModel(offered);

        // Act, Assert
        Assert.True(await withheldModel.WithholdsMail);
        Assert.False(await offeredModel.WithholdsMail);
    }

    /// <summary>A space that could be built without the session would be one that cannot say whether it may be shown.</summary>
    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailModel(null!));
    }

    private static StubClientSession SessionOffering(params string[] permissions) =>
        new(SessionStanding.Of(new DeploymentSession("MailFathom", "0.8.0", permissions)));
}
