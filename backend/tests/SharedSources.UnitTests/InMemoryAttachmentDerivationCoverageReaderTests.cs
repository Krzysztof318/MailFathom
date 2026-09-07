// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Administration;
using MailFathom.Application.Emails.AttachmentText.Limits;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the coverage source every surface that reports attachment progress is measured against.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement. A reader that answered its default rather than what a test placed
/// on it would make a mapping test pass over figures nobody chose, and one that did not record the account each read
/// was scoped to would let a claim that the mailbox surface asks per account pass without anything having been asked.
/// </remarks>
public sealed class InMemoryAttachmentDerivationCoverageReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    [Fact]
    public async Task ReadCoverageAsync_ACoverageATestPlacedOnIt_AnswersWithExactlyThat()
    {
        // Arrange
        var reader = new InMemoryAttachmentDerivationCoverageReader
        {
            Coverage = new AttachmentDerivationCoverage(
                EmailsWithAttachmentCount: 12,
                ReadEmailCount: 5,
                new AttachmentDerivationEstimate(7, 2_048, 9),
                DocumentTextAttachmentCount: 4,
                DescribedImageCount: 1,
                IndexedCharacterCount: 900,
                [new AttachmentSkipCount("Encrypted", 2)]),
        };

        // Act
        var coverage = await reader.ReadCoverageAsync(account: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(12, coverage.EmailsWithAttachmentCount);
        Assert.Equal(2_048, coverage.Outstanding.OutstandingInputOctetCount);
        Assert.Equal(2, coverage.SkippedAttachmentCount);
    }

    [Fact]
    public async Task ReadCoverageAsync_NothingPlacedOnIt_AnswersWithAScopeThatHasNothingToRead()
    {
        // Arrange
        var reader = new InMemoryAttachmentDerivationCoverageReader();

        // Act
        var coverage = await reader.ReadCoverageAsync(account: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, coverage.EmailsWithAttachmentCount);
        Assert.True(coverage.IsComplete);
    }

    [Fact]
    public async Task ReadCoverageAsync_ReadsScopedToTwoAccounts_RecordsEachInTheOrderItHappened()
    {
        // Arrange
        var reader = new InMemoryAttachmentDerivationCoverageReader();

        // Act
        await reader.ReadCoverageAsync(Account, TestContext.Current.CancellationToken);
        await reader.ReadCoverageAsync(account: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Account, null], reader.Reads);
    }

    [Fact]
    public async Task ReadCoverageAsync_ACancelledCaller_StopsRatherThanAnswering()
    {
        // Arrange
        var reader = new InMemoryAttachmentDerivationCoverageReader();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            reader.ReadCoverageAsync(account: null, cancellation.Token));
    }

    /// <summary>The composed reader is what a surface under test actually resolves, so it answers from both halves.</summary>
    [Fact]
    public async Task Bounded_ADeclaredCeiling_ComposesAStatusReaderReportingItBesideTheCoverage()
    {
        // Arrange
        var (coverage, reader) = InMemoryAttachmentDerivationCoverageReader.Bounded(
            Now,
            AttachmentDerivationBudget.Create(4_096, 0, 25, 0, TimeSpan.FromDays(1)));
        coverage.Coverage = AttachmentDerivationCoverage.Nothing with { DescribedImageCount = 3 };

        // Act
        var status = await reader.ReadAsync(account: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, status.Coverage.DescribedImageCount);
        Assert.Equal(4_096, status.Extraction.CeilingUnitCount);
        Assert.Equal(25, status.Description.CeilingUnitCount);
    }

    /// <summary>The unbounded composition is the default a surface not under test for its ceilings meets.</summary>
    [Fact]
    public async Task Unbounded_NoDeclaredCeiling_ComposesAStatusReaderThatCountsAgainstNothing()
    {
        // Arrange
        var (_, reader) = InMemoryAttachmentDerivationCoverageReader.Unbounded(Now);

        // Act
        var status = await reader.ReadAsync(account: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(status.Extraction.CeilingUnitCount);
        Assert.Null(status.Description.CeilingUnitCount);
        Assert.True(status.Extraction.AdmitsWork);
    }
}
