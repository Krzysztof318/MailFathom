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
        Assert.NotNull(await guard.StampForAsync(FixedSensitiveContentPostures.SoleAccount, TestContext.Current.CancellationToken));
        Assert.Null(await guard.StampForAsync(AnotherAccount, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The walk that re-derives stale rows reads every account asking for more than the deployment from here, each beside
    /// its own stamp, and judges every other account's rows by the deployment's own.
    /// </summary>
    [Fact]
    public async Task ReadAccountsBeyondDeploymentAsync_ADeploymentServingTwoAccounts_ReportsOnlyTheOneAskingForMoreThanTheDeployment()
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
        var beyondDeployment = await guard.ReadAccountsBeyondDeploymentAsync(TestContext.Current.CancellationToken);

        // Assert
        var scanned = Assert.Single(beyondDeployment);
        Assert.Equal(FixedSensitiveContentPostures.SoleAccount, scanned.Account);
        Assert.NotNull(scanned.Posture.Stamp);
        Assert.Null(guard.StampForEveryOtherAccount);
    }

    /// <summary>A row's stamp is what makes a later configuration change answerable rather than silent.</summary>
    [Fact]
    public async Task Stamp_ASwitchedOnScanner_NamesTheConfigurationARowIsWrittenUnder()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);

        // Act
        var stamp = await derivation.Guard.StampForAsync(
            ScanningSensitiveContentDerivation.Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await derivation.Guard.IsActiveAsync(TestContext.Current.CancellationToken));
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
        Assert.False(await guard.IsActiveAsync(TestContext.Current.CancellationToken));
        Assert.Null(await guard.StampForAsync(ScanningSensitiveContentDerivation.Account, TestContext.Current.CancellationToken));
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
    public async Task StampFor_AnAccountNothingScans_IsAbsentBesideTheRedactionThatIsAbsentToo()
    {
        // Arrange
        var guard = ScanningSensitiveContentDerivation.Inactive();

        // Act
        var stamp = await guard.StampForAsync(ScanningSensitiveContentDerivation.Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(stamp);
        Assert.False(await guard.IsActiveAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await guard.ReadAccountsBeyondDeploymentAsync(TestContext.Current.CancellationToken));
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
