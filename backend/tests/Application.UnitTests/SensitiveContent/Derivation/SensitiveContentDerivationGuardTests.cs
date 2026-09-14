// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Derivation;

/// <summary>Covers the one thing every derived write calls before it copies mail text into a store of its own.</summary>
public sealed class SensitiveContentDerivationGuardTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly MailAccountId AnotherAccount = MailAccountId.Create("secondary");

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GuardAsync_ADetectedValue_IsReplacedBeforeTheTextIsStored()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);

        // Act
        var stored = await derivation.Guard.GuardAsync(
            ScanningSensitiveContentDerivation.Account,
            $"the key is {Marker} and it works",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the key is [redacted:CloudKey] and it works", stored);
    }

    /// <summary>
    /// A derived row belongs to one account, so what redacted it and what it is stamped with are that account's. The
    /// account that asked for nothing has its text stored as it was read and its rows carry no stamp, which is what a
    /// later walk reads to decide which mailbox a posture change made stale.
    /// </summary>
    [Fact]
    public async Task GuardAsync_TwoAccountsOfOneDeployment_RedactsAndStampsEachUnderItsOwnPosture()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);

        var guard = new SensitiveContentDerivationGuard(
            FixedSensitiveContentPostures.Of(
                SensitiveContentPosture.ScanningNothing,
                (FixedSensitiveContentPostures.SoleAccount, derivation.Postures.ForAccount(FixedSensitiveContentPostures.SoleAccount))),
            new RecordingSensitiveContentDerivationTelemetry(),
            this.timeProvider);

        // Act
        var scanned = await guard.GuardAsync(
            FixedSensitiveContentPostures.SoleAccount,
            $"the key is {Marker}",
            TestContext.Current.CancellationToken);
        var unscanned = await guard.GuardAsync(
            AnotherAccount,
            $"the key is {Marker}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the key is [redacted:CloudKey]", scanned);
        Assert.Equal($"the key is {Marker}", unscanned);
        Assert.NotNull(guard.StampFor(FixedSensitiveContentPostures.SoleAccount));
        Assert.Null(guard.StampFor(AnotherAccount));
    }

    /// <summary>The walk that re-derives stale rows reads every account from here, each beside its own stamp.</summary>
    [Fact]
    public void Current_ADeploymentServingTwoAccounts_ReportsBothOfThemBesideWhatTheirRowsAreWrittenUnder()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);

        var guard = new SensitiveContentDerivationGuard(
            FixedSensitiveContentPostures.Of(
                SensitiveContentPosture.ScanningNothing,
                (FixedSensitiveContentPostures.SoleAccount, derivation.Postures.ForAccount(FixedSensitiveContentPostures.SoleAccount)),
                (AnotherAccount, SensitiveContentPosture.ScanningNothing)),
            new RecordingSensitiveContentDerivationTelemetry(),
            this.timeProvider);

        // Act
        var current = guard.Current;

        // Assert
        Assert.Equal(
            [FixedSensitiveContentPostures.SoleAccount, AnotherAccount],
            current.Select(posture => posture.Account));
        Assert.NotNull(current[0].Posture.Stamp);
        Assert.Null(current[1].Posture.Stamp);
    }

    /// <summary>A row's stamp is what makes a later configuration change answerable rather than silent.</summary>
    [Fact]
    public void Stamp_ASwitchedOnScanner_NamesTheConfigurationARowIsWrittenUnder()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);

        // Act
        var stamp = derivation.Guard.StampFor(ScanningSensitiveContentDerivation.Account);

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
        var stored = await guard.GuardAsync(
            ScanningSensitiveContentDerivation.Account,
            $"the key is {Marker}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(guard.IsActive);
        Assert.Null(guard.StampFor(ScanningSensitiveContentDerivation.Account));
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
            derivation.Guard.GuardAsync(
                ScanningSensitiveContentDerivation.Account,
                "whatever the message said",
                TestContext.Current.CancellationToken));

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
            derivation.Guard.GuardAsync(
                ScanningSensitiveContentDerivation.Account,
                "whatever the message said",
                TestContext.Current.CancellationToken));

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
        await derivation.Guard.GuardAsync(
            ScanningSensitiveContentDerivation.Account,
            $"{Marker} and {Marker}",
            TestContext.Current.CancellationToken);

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
    public void StampFor_AnAccountNothingScans_IsAbsentBesideTheRedactionThatIsAbsentToo()
    {
        // Arrange
        var guard = ScanningSensitiveContentDerivation.Inactive();

        // Act
        var stamp = guard.StampFor(ScanningSensitiveContentDerivation.Account);

        // Assert
        Assert.Null(stamp);
        Assert.False(guard.IsActive);
        Assert.Empty(guard.Current);
    }

    /// <summary>The guard is composed from the postures alone, so a deployment cannot hand it one without the other.</summary>
    [Fact]
    public void Constructor_WithoutItsCollaborators_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new SensitiveContentDerivationGuard(
            null!,
            new RecordingSensitiveContentDerivationTelemetry(),
            this.timeProvider));
        Assert.Throws<ArgumentNullException>(() => new SensitiveContentDerivationGuard(
            FixedSensitiveContentPostures.ScanningNothing(),
            null!,
            this.timeProvider));
        Assert.Throws<ArgumentNullException>(() => new SensitiveContentDerivationGuard(
            FixedSensitiveContentPostures.ScanningNothing(),
            new RecordingSensitiveContentDerivationTelemetry(),
            null!));
    }
}
