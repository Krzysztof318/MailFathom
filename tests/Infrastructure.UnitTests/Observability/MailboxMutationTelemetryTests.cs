// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers that mailbox mutations publish under a name something actually collects.</summary>
/// <remarks>
/// A bespoke name here would be subscribed by nothing, because the host subscribes the declared set and only that set.
/// The instruments would still be created and the spans still started, so the gap reads as a mailbox that made no
/// changes rather than as telemetry nobody wired up. Asserting against the declaration is what turns a name invented
/// for this feature into a failing test.
/// </remarks>
public sealed class MailboxMutationTelemetryTests
{
    [Fact]
    public void MeterName_IsANameTheHostSubscribes()
    {
        // Act
        var declared = MailFathomTelemetry.All;

        // Assert
        Assert.Contains(MailboxMutationTelemetry.MeterName, declared);
    }

    [Fact]
    public void ActivitySourceName_IsANameTheHostSubscribes()
    {
        // Act
        var declared = MailFathomTelemetry.All;

        // Assert
        Assert.Contains(MailboxMutationTelemetry.ActivitySourceName, declared);
    }

    /// <summary>
    /// Mailbox work is one subsystem, so a mutation belongs to the name that already describes it rather than to one
    /// of its own. Which mutation ran is a tag; splitting it into a second name is what would make an operator's
    /// filter depend on knowing the order features were instrumented in.
    /// </summary>
    [Fact]
    public void TheMutationNames_AreTheMailSubsystemsName()
    {
        // Act
        var names = new[] { MailboxMutationTelemetry.MeterName, MailboxMutationTelemetry.ActivitySourceName };

        // Assert
        Assert.All(names, name => Assert.Equal(MailFathomTelemetry.Mail, name));
    }
}
