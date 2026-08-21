// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Spam;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Spam;

/// <summary>Covers the narrowing every walk over stored mail applies for the classification gate.</summary>
/// <remarks>
/// The predicate is composed here and evaluated by PostgreSQL, so what these tests establish is which rows it selects
/// rather than what SQL it becomes. It is the set-based half of a rule whose per-occurrence half lives in
/// <see cref="DerivedWorkGate" />, and the two answering differently about one message is exactly the defect worth
/// catching: junk reaching a provider on one path, or a wedged scanner stopping the index on the other.
/// </remarks>
public sealed class DerivedWorkAdmittedEmailsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset WaitedLongEnough = Now - TimeSpan.FromHours(1);

    private static readonly MailFolderIdentity WorkJunk = new(
        MailAccountId.Create("work"),
        MailFolderAlias.Create("JUNK"));

    [Fact]
    public void Admitting_ClassificationSwitchedOff_LeavesTheQueryAsItWas()
    {
        // Arrange
        var emails = Emails(Email("work", "JUNK", Now, verdict: SpamVerdict.Spam));
        var terms = new DerivedWorkAdmissionTerms(
            IsApplied: false,
            [WorkJunk],
            [MailFolderAlias.Create("INBOX")],
            WaitedLongEnough);

        // Act
        var admitted = DerivedWorkAdmittedEmails.Admitting(emails, terms);

        // Assert
        Assert.Same(emails, admitted);
    }

    /// <summary>Placement decides with nothing having scored the message, and one account's junk folder is not another's.</summary>
    [Fact]
    public void Admitting_MailInAJunkFolder_LeavesItOutWhileKeepingAnotherAccountsFolderOfTheSameName()
    {
        // Arrange
        var emails = Emails(
            Email("work", "JUNK", WaitedLongEnough, verdict: null),
            Email("home", "JUNK", WaitedLongEnough, verdict: null),
            Email("work", "INBOX", WaitedLongEnough, verdict: null));

        // Act
        var admitted = DerivedWorkAdmittedEmails.Admitting(emails, Terms(WorkJunk));

        // Assert
        Assert.Equal(
            [("home", "JUNK"), ("work", "INBOX")],
            admitted.AsEnumerable().Select(email => (email.MailboxAccountId, email.MailFolder.Alias)));
    }

    /// <summary>Placement outranks the record here too, so a scan concluding otherwise does not put junk back in the walk.</summary>
    /// <remarks>
    /// Asserted separately from the clause order it rests on, because the folder exclusion and the verdict clause are
    /// two `Where` calls and a message carrying both facts is the only row that proves the first still applies.
    /// </remarks>
    [Fact]
    public void Admitting_MailInAJunkFolderScoredAsAnythingElse_StillLeavesItOut()
    {
        // Arrange
        var emails = Emails(
            Email("work", "JUNK", WaitedLongEnough, SpamVerdict.NotSpam),
            Email("work", "JUNK", WaitedLongEnough, SpamVerdict.Undetermined));

        // Act
        var admitted = DerivedWorkAdmittedEmails.Admitting(emails, Terms(WorkJunk));

        // Assert
        Assert.Empty(admitted.AsEnumerable());
    }

    /// <summary>A verdict withholds wherever the message sits, which is what an operator scoring without filing gets.</summary>
    [Fact]
    public void Admitting_MailScoredAsSpamAndNeverFiled_LeavesItOut()
    {
        // Arrange
        var emails = Emails(
            Email("work", "INBOX", Now, SpamVerdict.Spam),
            Email("work", "INBOX", Now, SpamVerdict.NotSpam),
            Email("work", "INBOX", Now, SpamVerdict.Undetermined));

        // Act
        var admitted = DerivedWorkAdmittedEmails.Admitting(emails, Terms());

        // Assert
        Assert.Equal(
            [SpamVerdict.NotSpam, SpamVerdict.Undetermined],
            admitted.AsEnumerable().Select(email => email.SpamClassification!.Verdict));
    }

    /// <summary>The gate's whole point: mail still waiting on a verdict is not offered to anything downstream of it.</summary>
    [Fact]
    public void Admitting_MailInsideTheScopeStillWithinItsWait_LeavesItOut()
    {
        // Arrange
        var emails = Emails(Email("work", "INBOX", Now, verdict: null));

        // Act
        var admitted = DerivedWorkAdmittedEmails.Admitting(emails, Terms());

        // Assert
        Assert.Empty(admitted.AsEnumerable());
    }

    /// <summary>The failure mode the gate must not have: a wedged scanner delays the index rather than stopping it.</summary>
    [Fact]
    public void Admitting_MailThatHasWaitedLongerThanTheBound_LetsItThrough()
    {
        // Arrange
        var emails = Emails(Email("work", "INBOX", WaitedLongEnough, verdict: null));

        // Act
        var admitted = DerivedWorkAdmittedEmails.Admitting(emails, Terms());

        // Assert
        Assert.Single(admitted.AsEnumerable());
    }

    /// <summary>Nothing is ever going to score mail outside the scope, so waiting on a verdict for it would never end.</summary>
    [Fact]
    public void Admitting_UnclassifiedMailOutsideTheScope_LetsItThroughWithoutWaiting()
    {
        // Arrange
        var emails = Emails(Email("work", "ARCHIVE", Now, verdict: null));

        // Act
        var admitted = DerivedWorkAdmittedEmails.Admitting(emails, Terms());

        // Assert
        Assert.Single(admitted.AsEnumerable());
    }

    /// <summary>An oversized message has no payload a classification could read, and every later run refuses it too.</summary>
    [Fact]
    public void Admitting_MailWhosePayloadWasNeverStored_LetsItThroughWithoutWaiting()
    {
        // Arrange
        var email = Email("work", "INBOX", Now, verdict: null);
        email.ContentAvailability = StoredEmailContentAvailability.ExceededSizeLimit;

        // Act
        var admitted = DerivedWorkAdmittedEmails.Admitting(Emails(email), Terms());

        // Assert
        Assert.Single(admitted.AsEnumerable());
    }

    /// <summary>A payload a later run will fetch is still expected, so the message keeps waiting on its verdict.</summary>
    [Fact]
    public void Admitting_MailWhosePayloadIsStillComing_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX", Now, verdict: null);
        email.ContentAvailability = StoredEmailContentAvailability.AwaitingStorageHeadroom;

        // Act
        var admitted = DerivedWorkAdmittedEmails.Admitting(Emails(email), Terms());

        // Assert
        Assert.Empty(admitted.AsEnumerable());
    }

    [Fact]
    public void Admitting_NoTerms_Throws()
    {
        // Arrange
        var emails = Emails(Email("work", "INBOX", Now, verdict: null));

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => DerivedWorkAdmittedEmails.Admitting(emails, null!));
    }

    private static DerivedWorkAdmissionTerms Terms(params MailFolderIdentity[] junkFolders) => new(
        IsApplied: true,
        junkFolders,
        [MailFolderAlias.Create("INBOX")],
        Now - TimeSpan.FromMinutes(15));

    private static IQueryable<StoredEmailEntity> Emails(params StoredEmailEntity[] emails) => emails.AsQueryable();

    private static StoredEmailEntity Email(string accountId, string alias, DateTimeOffset storedAt, SpamVerdict? verdict)
    {
        var email = new StoredEmailEntity
        {
            MailboxAccountId = accountId,
            MailFolder = new MailFolderEntity
            {
                MailboxAccountId = accountId,
                Alias = alias,
                RemotePath = alias,
                MailboxAccount = new MailboxAccountEntity { Id = accountId },
            },
            StoredAt = storedAt,
            ContentAvailability = StoredEmailContentAvailability.Available,
        };

        if (verdict is { } recorded)
        {
            email.SpamClassification = new EmailSpamClassificationEntity
            {
                StoredEmailId = email.Id,
                Verdict = recorded,
                DecidedBy = SpamClassificationStage.Deterministic,
                EvaluatedAt = storedAt,
            };
        }

        return email;
    }
}
