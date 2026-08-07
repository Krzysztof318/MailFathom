// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.Observability;
using Xunit;

namespace MailFathom.Common.UnitTests.Observability;

/// <summary>Covers the name MailFathom publishes telemetry under, which is a contract with whatever collects it.</summary>
/// <remarks>
/// The name reaches an operator's dashboard filter and an alert rule, so changing it silently stops collecting what
/// somebody is watching. This pins the published string for that reason rather than to restate the declaration.
/// </remarks>
public sealed class MailFathomTelemetryTests
{
    [Fact]
    public void Name_IsTheNameTheHostIsExpectedToSubscribe()
    {
        // Act
        var declared = MailFathomTelemetry.Name;

        // Assert
        Assert.Equal("MailFathom", declared);
    }
}
