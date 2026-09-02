// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.DeadLetters;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.DeadLetters;

/// <summary>Covers what a request for a page of dead letters is accepted and refused on.</summary>
public sealed class DeadLetteredJobQueryTests
{
    private static readonly DateTimeOffset StoppedAt = new(2026, 8, 13, 9, 30, 0, TimeSpan.Zero);

    private static readonly JobId Job = JobId.Create(new Guid("2f1c1d6c-6f0b-4a5e-9f3d-0f9b2a5c7e11"));

    /// <summary>A caller that names no filter reads the most recent page rather than every job that ever stopped.</summary>
    [Fact]
    public void Create_NoFiltersAtAll_IsAcceptedAndBoundedByTheDefaultPageSize()
    {
        // Arrange, Act
        var result = DeadLetteredJobQuery.Create(jobType: null, account: null, pageSize: null, cursor: null);

        // Assert
        Assert.Equal(DeadLetteredJobQueryOutcome.Accepted, result.Outcome);
        Assert.Equal(DeadLetteredJobQuery.DefaultPageSize, result.Query?.PageSize);
    }

    /// <summary>The page is bounded at both ends, because an answer's weight is what the bound is for.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(DeadLetteredJobQuery.MaximumPageSize + 1)]
    public void Create_APageSizeOutsideWhatTheReadingServes_IsRefused(int pageSize)
    {
        // Arrange, Act
        var result = DeadLetteredJobQuery.Create(jobType: null, account: null, pageSize, cursor: null);

        // Assert
        Assert.Equal(DeadLetteredJobQueryOutcome.PageSizeOutOfRange, result.Outcome);
        Assert.Null(result.Query);
    }

    /// <summary>The struct default names no type, so filtering on it would filter on a name nothing can read back.</summary>
    [Fact]
    public void Create_AJobTypeThatNamesNothing_IsRefused()
    {
        // Arrange, Act
        var result = DeadLetteredJobQuery.Create(default(JobType), account: null, pageSize: null, cursor: null);

        // Assert
        Assert.Equal(DeadLetteredJobQueryOutcome.JobTypeUnknown, result.Outcome);
    }

    /// <summary>
    /// A position names a page edge only within the filtered set it was computed for, so a cursor presented against
    /// other filters is refused rather than resolved somewhere inside a different reading.
    /// </summary>
    [Fact]
    public void Create_ACursorIssuedForOtherFilters_IsRefused()
    {
        // Arrange
        var everything = DeadLetteredJobQuery.Create(null, null, null, null).Query!;
        var cursor = DeadLetteredJobCursor.After(StoppedAt, Job, everything.FilterFingerprint);

        // Act
        var result = DeadLetteredJobQuery.Create(
            JobType.ClassifyEmailSpam,
            account: null,
            pageSize: null,
            cursor);

        // Assert
        Assert.Equal(DeadLetteredJobQueryOutcome.CursorFilterMismatch, result.Outcome);
    }

    /// <summary>
    /// A caller may pace a walk however they like, so the page size stays out of the fingerprint: refusing a shorter or
    /// longer page mid-walk would be a rule about pacing rather than about which records the boundary sits in.
    /// </summary>
    [Fact]
    public void Create_TheSameFiltersAtAnotherPageSize_ContinuesTheSameWalk()
    {
        // Arrange
        var account = MailAccountId.Create("work");
        var first = DeadLetteredJobQuery.Create(JobType.ClassifyEmailSpam, MailAccountIdentity.Create(SyntheticMailOwner.Deployment, account), 10, null).Query!;
        var cursor = DeadLetteredJobCursor.After(StoppedAt, Job, first.FilterFingerprint);

        // Act
        var result = DeadLetteredJobQuery.Create(JobType.ClassifyEmailSpam, MailAccountIdentity.Create(SyntheticMailOwner.Deployment, account), 25, cursor);

        // Assert
        Assert.Equal(DeadLetteredJobQueryOutcome.Accepted, result.Outcome);
        Assert.Equal(25, result.Query?.PageSize);
    }
}
