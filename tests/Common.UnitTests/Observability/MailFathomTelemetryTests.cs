// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Common.Observability;
using Xunit;

namespace MailFathom.Common.UnitTests.Observability;

/// <summary>Covers the names MailFathom publishes telemetry under, which are a contract with whatever collects them.</summary>
/// <remarks>
/// A source name reaches an operator's dashboard filter and an alert rule, so renaming one silently stops collecting
/// what somebody is watching. These assertions pin the published strings for that reason rather than to restate the
/// declaration.
/// </remarks>
public sealed class MailFathomTelemetryTests
{
    [Fact]
    public void All_ListsEveryNameTheHostIsExpectedToSubscribe()
    {
        // Arrange
        string[] expected = ["MailFathom.Mail", "MailFathom.Mcp", "MailFathom.Persistence", "MailFathom.Extraction"];

        // Act
        var declared = MailFathomTelemetry.All;

        // Assert
        Assert.Equal(expected, declared);
    }

    /// <summary>
    /// A name declared here and left out of <see cref="MailFathomTelemetry.All" /> is subscribed by nothing, so the
    /// subsystem publishing to it emits into a stream no exporter reads and the code still looks instrumented. Reading
    /// the declaration is the only way to make that a failing test rather than a silent gap, which is why this is the
    /// one assertion here that reflects.
    /// </summary>
    [Fact]
    public void All_ListsEveryNameTheTypeDeclares()
    {
        // Arrange
        var declared = typeof(MailFathomTelemetry)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.Name != nameof(MailFathomTelemetry.NamePrefix))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        // Act
        var subscribed = MailFathomTelemetry.All.Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(declared, subscribed);
    }

    /// <summary>
    /// One prefix is what lets a collector select everything this process owns and nothing a library emits, so a name
    /// that dropped it would be collected only by someone who already knew it existed.
    /// </summary>
    [Fact]
    public void All_EveryDeclaredName_CarriesTheSharedPrefix()
    {
        // Arrange
        var prefix = MailFathomTelemetry.NamePrefix + ".";

        // Act
        var withoutThePrefix = MailFathomTelemetry.All
            .Where(name => !name.StartsWith(prefix, StringComparison.Ordinal));

        // Assert
        Assert.Empty(withoutThePrefix);
    }

    /// <summary>
    /// Two subsystems sharing a name are indistinguishable once their signals arrive, and nothing downstream can undo
    /// the merge.
    /// </summary>
    [Fact]
    public void All_TheDeclaredNames_AreDistinct()
    {
        // Act
        var distinct = MailFathomTelemetry.All.Distinct(StringComparer.Ordinal);

        // Assert
        Assert.Equal(MailFathomTelemetry.All, distinct);
    }

    [Fact]
    public void All_EveryDeclaredName_NamesASubsystemBeyondThePrefix()
    {
        // Arrange
        var prefix = MailFathomTelemetry.NamePrefix + ".";

        // Act
        var subsystems = MailFathomTelemetry.All.Select(name => name[prefix.Length..]);

        // Assert
        Assert.All(subsystems, subsystem => Assert.False(string.IsNullOrWhiteSpace(subsystem)));
    }
}
