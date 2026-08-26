// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Infrastructure.Persistence.Delivery;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Delivery;

/// <summary>Reads the claim as the text it is, which is the reason it was made a type of its own.</summary>
/// <remarks>
/// Each clause asserted here fails silently if it is lost: without the stage filter a message that may already have
/// been transmitted is offered again, without the locking clause two workers take the same send, and without the bound
/// one pass drains the whole queue. What the statement then does against PostgreSQL belongs to the integration suite;
/// what it says is decidable here.
/// </remarks>
public sealed class OutgoingEmailClaimStatementTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));
    private static readonly DateTimeOffset ClaimedAt = DateTimeOffset.UnixEpoch.AddHours(9);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    private const int BatchSize = 25;

    /// <summary>Only a recorded send is claimable, which is what keeps an unfinished transmission out of the batch.</summary>
    [Fact]
    public void Compose_Always_TakesRecordedSendsAlone()
    {
        // Act
        var statement = Compose();

        // Assert
        Assert.Contains($"""candidate."{nameof(OutgoingEmailEntity.Stage)}" =""", statement.Format, StringComparison.Ordinal);
        Assert.Contains(nameof(OutgoingEmailStage.Recorded), statement.GetArguments().OfType<string>());
    }

    /// <summary>
    /// The claim takes one account's sends, which is what keeps a pass out of every other account's outbox — and the
    /// account it names is the pair, so a second owner's account of the same configured name is another outbox.
    /// </summary>
    [Fact]
    public void Compose_Always_TakesTheOwnersAccountItWasAskedAbout()
    {
        // Act
        var statement = Compose();

        // Assert
        Assert.Contains(
            $"""candidate."{nameof(OutgoingEmailEntity.OwnerId)}" =""",
            statement.Format,
            StringComparison.Ordinal);
        Assert.Contains(
            $"""candidate."{nameof(OutgoingEmailEntity.MailboxAccountId)}" =""",
            statement.Format,
            StringComparison.Ordinal);
        Assert.Contains(Account.Owner.Value, statement.GetArguments().OfType<Guid>());
        Assert.Contains(Account.Id.Value, statement.GetArguments().OfType<string>());
    }

    /// <summary>The locking clause is what makes two workers claiming at once take different sends.</summary>
    [Fact]
    public void Compose_Always_LocksTheRowsItTakesAndSkipsTheHeldOnes()
    {
        // Act
        var statement = Compose();

        // Assert
        Assert.Contains("FOR UPDATE SKIP LOCKED", statement.Format, StringComparison.Ordinal);
    }

    /// <summary>The bound is what makes a pass a batch rather than the whole queue, and it is the batch size asked for.</summary>
    [Fact]
    public void Compose_Always_BoundsTheBatchToWhatWasAskedFor()
    {
        // Act
        var statement = Compose();

        // Assert
        Assert.Contains("LIMIT", statement.Format, StringComparison.Ordinal);
        Assert.Contains(BatchSize, statement.GetArguments().OfType<int>());
    }

    /// <summary>A send is due when its next-attempt instant has passed, and again once whatever held it ran out of lease.</summary>
    [Fact]
    public void Compose_Always_TakesTheSendsNothingHoldsAndTheOnesWhoseLeaseHasRunOut()
    {
        // Act
        var statement = Compose();

        // Assert
        Assert.Contains($"""candidate."{nameof(OutgoingEmailEntity.AvailableAt)}" <=""", statement.Format, StringComparison.Ordinal);
        Assert.Contains($"""candidate."{nameof(OutgoingEmailEntity.LeaseExpiresAt)}" IS NULL""", statement.Format, StringComparison.Ordinal);
        Assert.Contains(ClaimedAt, statement.GetArguments().OfType<DateTimeOffset>());
    }

    /// <summary>The claim stamps the holder and counts the attempt in the statement that took the row, not after it.</summary>
    [Fact]
    public void Compose_Always_StampsTheHolderAndCountsTheAttempt()
    {
        // Act
        var statement = Compose();

        // Assert
        Assert.Contains($"""SET "{nameof(OutgoingEmailEntity.LeaseOwner)}" =""", statement.Format, StringComparison.Ordinal);
        Assert.Contains(
            $"\"{nameof(OutgoingEmailEntity.AttemptCount)}\" = outgoing.\"{nameof(OutgoingEmailEntity.AttemptCount)}\" + 1",
            statement.Format,
            StringComparison.Ordinal);
    }

    /// <summary>The lease it stamps ends the duration that was asked for after the instant the claim was judged at.</summary>
    [Fact]
    public void Compose_Always_StampsTheLeaseFromTheInstantTheClaimWasJudgedAt()
    {
        // Act
        var statement = Compose();

        // Assert
        Assert.Contains(ClaimedAt + LeaseDuration, statement.GetArguments().OfType<DateTimeOffset>());
    }

    private static FormattableString Compose() => OutgoingEmailClaimStatement.Compose(
        OutgoingEmailClaimRequest.Create(Account, BatchSize, LeaseDuration),
        ClaimedAt);
}
