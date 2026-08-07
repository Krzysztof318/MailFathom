// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers that mailbox mutations publish under the name something actually collects.</summary>
/// <remarks>
/// A name of this feature's own would be subscribed by nothing, because the host subscribes the declared name and only
/// that one. The instruments would still be created and the spans still started, so the gap reads as a mailbox that
/// made no changes rather than as telemetry nobody wired up. Asserting against the declaration is what turns a name
/// invented here into a failing test.
/// </remarks>
public sealed class MailboxMutationTelemetryTests
{
    [Fact]
    public void MeterName_IsTheNameTheHostSubscribes()
    {
        // Act
        var published = MailboxMutationTelemetry.MeterName;

        // Assert
        Assert.Equal(MailFathomTelemetry.Name, published);
    }

    [Fact]
    public void ActivitySourceName_IsTheNameTheHostSubscribes()
    {
        // Act
        var published = MailboxMutationTelemetry.ActivitySourceName;

        // Assert
        Assert.Equal(MailFathomTelemetry.Name, published);
    }
}
