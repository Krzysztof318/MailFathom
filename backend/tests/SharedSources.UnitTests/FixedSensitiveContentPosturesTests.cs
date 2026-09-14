// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the postures every scanning suite arranges through, and the one answer this double may not invent.</summary>
/// <remarks>
/// A read that spans a user's accounts is served by the strictest of their postures, and the real composition builds
/// that by unioning what each account switched on. This double is handed postures rather than the ingredients of one,
/// so the only honest answers it has are a candidate that already covers the rest and a refusal — returning the widest
/// of two that cover neither would hand a suite a posture no deployment ever composes.
/// </remarks>
public sealed class FixedSensitiveContentPosturesTests
{
    private static readonly MailAccountId Archive = MailAccountId.Create("archive");

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void AcrossAccountsOf_OneAccountScanningAndOneScanningNothing_AnswersWithTheScanningOne()
    {
        // Arrange
        using var secrets = ScanningSensitiveContentEgress.Finding("AKIAEXAMPLEKEY", this.timeProvider);
        var scanning = secrets.Postures.ForAccount(FixedSensitiveContentPostures.SoleAccount);

        var postures = FixedSensitiveContentPostures.Of(
            SensitiveContentPosture.ScanningNothing,
            (FixedSensitiveContentPostures.SoleAccount, scanning),
            (Archive, SensitiveContentPosture.ScanningNothing));

        // Act
        var strictest = postures.AcrossAccountsOf(SyntheticMailUser.Deployment);

        // Assert
        Assert.Same(scanning, strictest);
    }

    /// <summary>
    /// Two mailboxes each opting into a scanner the other did not. The composition unions them; this double cannot,
    /// so it says so rather than answering with whichever of the two it met first.
    /// </summary>
    [Fact]
    public void AcrossAccountsOf_TwoAccountsAskingForThingsNeitherCovers_SaysItCannotComposeTheUnion()
    {
        // Arrange
        using var secrets = ScanningSensitiveContentEgress.Finding("AKIAEXAMPLEKEY", this.timeProvider);
        using var personalData = ScanningSensitiveContentEgress.Finding(
            "Alex Doe",
            this.timeProvider,
            SensitiveContentScannerKind.Pii);

        var postures = FixedSensitiveContentPostures.Of(
            SensitiveContentPosture.ScanningNothing,
            (FixedSensitiveContentPostures.SoleAccount, secrets.Postures.ForAccount(FixedSensitiveContentPostures.SoleAccount)),
            (Archive, personalData.Postures.ForAccount(FixedSensitiveContentPostures.SoleAccount)));

        // Act
        var refusal = Assert.Throws<InvalidOperationException>(
            () => postures.AcrossAccountsOf(SyntheticMailUser.Deployment));

        // Assert
        Assert.Contains("this double cannot build", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two mailboxes that screen, running the same scanner. A screening policy says whether it refuses anything and
    /// never which categories it refuses, so neither of the two can be shown to cover the other's refusals; answering
    /// with the first would hand the suite a posture that may refuse less than the union does.
    /// </summary>
    [Fact]
    public void AcrossAccountsOf_TwoAccountsThatBothScreen_SaysItCannotComposeTheUnion()
    {
        // Arrange
        using var mine = ScanningSensitiveContentEgress.Finding("AKIAEXAMPLEKEY", this.timeProvider);
        using var theirs = ScanningSensitiveContentEgress.Finding("AKIAEXAMPLEKEY", this.timeProvider);

        var postures = FixedSensitiveContentPostures.Of(
            SensitiveContentPosture.ScanningNothing,
            (FixedSensitiveContentPostures.SoleAccount, mine.Postures.ForAccount(FixedSensitiveContentPostures.SoleAccount)),
            (Archive, theirs.Postures.ForAccount(FixedSensitiveContentPostures.SoleAccount)));

        // Act
        var refusal = Assert.Throws<InvalidOperationException>(
            () => postures.AcrossAccountsOf(SyntheticMailUser.Deployment));

        // Assert
        Assert.Contains("this double cannot build", refusal.Message, StringComparison.Ordinal);
    }
}
