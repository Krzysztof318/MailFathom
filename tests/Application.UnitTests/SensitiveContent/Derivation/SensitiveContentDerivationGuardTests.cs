// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Derivation;

/// <summary>Covers the one thing every derived write calls before it copies mail text into a store of its own.</summary>
public sealed class SensitiveContentDerivationGuardTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GuardAsync_ADetectedValue_IsReplacedBeforeTheTextIsStored()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);

        // Act
        var stored = await derivation.Guard.GuardAsync(
            $"the key is {Marker} and it works",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the key is [redacted:CloudKey] and it works", stored);
    }

    /// <summary>A row's stamp is what makes a later configuration change answerable rather than silent.</summary>
    [Fact]
    public void Stamp_ASwitchedOnScanner_NamesTheConfigurationARowIsWrittenUnder()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);

        // Act
        var stamp = derivation.Guard.Stamp;

        // Assert
        Assert.True(derivation.Guard.IsActive);
        Assert.NotNull(stamp);
        Assert.Equal(SensitiveContentDerivationStamp.Length, stamp.Value.Value.Length);
    }

    /// <summary>An opt-in nobody took must leave a derived row byte-identical to the one it produced before.</summary>
    [Fact]
    public async Task GuardAsync_ADeploymentThatScansNothing_StoresTheTextUnchangedAndStampsNothing()
    {
        // Arrange
        var guard = ScanningSensitiveContentDerivation.Inactive();

        // Act
        var stored = await guard.GuardAsync($"the key is {Marker}", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(guard.IsActive);
        Assert.Null(guard.Stamp);
        Assert.Equal($"the key is {Marker}", stored);
    }

    /// <summary>A derived write that fell back to storing the text unscanned would leave the leak in the index.</summary>
    [Fact]
    public async Task GuardAsync_ADetectorThatCannotAnswer_RefusesTheDerivedWrite()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Unavailable(this.timeProvider);

        // Act
        var refusal = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            derivation.Guard.GuardAsync("whatever the message said", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, refusal.Scanner);
        Assert.DoesNotContain("whatever the message said", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A refusal an operator cannot see is a mailbox quietly failing to gain any derived data at all.</summary>
    [Fact]
    public async Task GuardAsync_ADetectorThatCannotAnswer_ReportsTheRefusalAgainstItsScanner()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Unavailable(this.timeProvider);

        // Act
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            derivation.Guard.GuardAsync("whatever the message said", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, Assert.Single(derivation.Telemetry.Refused));
        Assert.Empty(derivation.Telemetry.Derived);
    }

    /// <summary>What the scan adds to filling a mailbox is the figure an operator paces a rebuild by.</summary>
    [Fact]
    public async Task GuardAsync_ADetectedValue_ReportsTheFindingAndWhatTheScanCost()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);
        derivation.Scanner.WhileScanning = () => this.timeProvider.Advance(TimeSpan.FromMilliseconds(250));

        // Act
        await derivation.Guard.GuardAsync($"{Marker} and {Marker}", TestContext.Current.CancellationToken);

        // Assert
        var derived = Assert.Single(derivation.Telemetry.Derived);
        Assert.Equal(2, derived.Redacted.Findings.Count);
        Assert.All(
            derived.Redacted.Findings,
            finding => Assert.Equal(MarkerSensitiveContentScanner.Category, finding.Category));

        // The scan is the whole of what the guard adds to a derivation, so the figure it reports is that interval and
        // nothing around it. Asserting the value rather than that it is non-negative is the difference between this
        // covering the instrument and covering nothing: a guard that stopped timing would report zero and still pass.
        Assert.Equal(TimeSpan.FromMilliseconds(250), derived.Elapsed);
    }

    /// <summary>A stamp on a row promises the text beside it went through a redaction, so neither travels alone.</summary>
    [Fact]
    public void Constructor_AStampWithoutARedactor_IsRefused()
    {
        // Arrange
        var stamp = SensitiveContentDerivationStamp.Create(new string('a', SensitiveContentDerivationStamp.Length));

        // Act
        var refusal = Assert.Throws<ArgumentException>(() => new SensitiveContentDerivationGuard(
            redactor: null,
            stamp,
            new RecordingSensitiveContentDerivationTelemetry(),
            this.timeProvider));

        // Assert
        Assert.Equal("stamp", refusal.ParamName);
    }
}
