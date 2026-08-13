// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>Covers which mail the account run's cut selects, which is where the arrival pipeline's ordering is enforced.</summary>
/// <remarks>
/// <para>
/// The predicate is composed here and evaluated by PostgreSQL, so what these tests establish is which rows it selects
/// rather than what SQL it becomes. Every clause it carries is one of the pipeline's orderings, and each of them fails
/// silently when it lapses: passages cut from a message the rules were about to move, passages of junk, passages of a
/// folder an operator asked not to have embedded, or a mailbox re-cut on every run.
/// </para>
/// <para>
/// What the predicate reads about a folder is the admitted list, so the tool switch is deliberately absent from these
/// tests: it decides nothing here. That a mapping withheld from tools still reaches the list — the one row of the
/// switch table worth stating explicitly — is asserted where the list is built, by
/// <c>MailFolderParticipationOptionsTests.FoldersVisibleToTools_AFolderWithdrawnFromTools_LeavesItOutAndKeepsItMirrored</c>,
/// and the two facts compose into the row.
/// </para>
/// </remarks>
public sealed class StoredEmailChunkingSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);

    private static readonly MailFolderIdentity WorkInbox = new(
        MailAccountId.Create("work"),
        MailFolderAlias.Create("INBOX"));

    private static readonly MailFolderIdentity WorkArchive = new(
        MailAccountId.Create("work"),
        MailFolderAlias.Create("ARCHIVE"));

    /// <summary>The ordinary arrival: extracted, evaluated, admitted, and not cut yet.</summary>
    [Fact]
    public void Selecting_MailTheRulesHaveFinishedWith_SelectsIt()
    {
        // Arrange
        var emails = Emails(Email("work", "INBOX"));

        // Act
        var selected = StoredEmailChunkingStore.Selecting(emails, "work", [WorkInbox], ClassificationOff);

        // Assert
        Assert.Single(selected.AsEnumerable());
    }

    /// <summary>
    /// The ordering this pass exists for. A rule may file the message into a folder mapped differently from the one it
    /// arrived in, so passages cut before the rules ran would be passages of a placement that had not been settled.
    /// </summary>
    [Fact]
    public void Selecting_MailTheRulesHaveNotReachedYet_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.RulesEvaluatedAt = null;

        // Act
        var selected = StoredEmailChunkingStore.Selecting(Emails(email), "work", [WorkInbox], ClassificationOff);

        // Assert
        Assert.Empty(selected.AsEnumerable());
    }

    /// <summary>Cutting is what removes a message from this query, which is what lets the pass run without a cursor.</summary>
    [Fact]
    public void Selecting_MailThatAlreadyHasPassages_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.Chunks.Add(new EmailChunkEntity { StoredEmailId = email.Id, StoredEmail = email, Text = "a", ContentHash = "h" });

        // Act
        var selected = StoredEmailChunkingStore.Selecting(Emails(email), "work", [WorkInbox], ClassificationOff);

        // Assert
        Assert.Empty(selected.AsEnumerable());
    }

    /// <summary>A message nobody could extract text from has nothing to cut, and no later run will produce any.</summary>
    [Fact]
    public void Selecting_MailWithNoExtractedBody_LeavesItOut()
    {
        // Arrange
        var withoutDocument = Email("work", "INBOX");
        withoutDocument.SearchDocument = null;
        var withoutBody = Email("work", "INBOX");
        withoutBody.SearchDocument!.BodyText = null;

        // Act
        var selected = StoredEmailChunkingStore.Selecting(
            Emails(withoutDocument, withoutBody),
            "work",
            [WorkInbox],
            ClassificationOff);

        // Assert
        Assert.Empty(selected.AsEnumerable());
    }

    /// <summary>A folder an operator asked not to embed cuts no passages, on this path as on every other.</summary>
    [Fact]
    public void Selecting_AFolderNotMappedToEmbed_LeavesItsMailOut()
    {
        // Arrange
        var emails = Emails(Email("work", "INBOX"));

        // Act
        var selected = StoredEmailChunkingStore.Selecting(emails, "work", [WorkArchive], ClassificationOff);

        // Assert
        Assert.Empty(selected.AsEnumerable());
    }

    /// <summary>An admitted set that is empty admits nothing, which is what a deployment mapping no folder to embed means.</summary>
    [Fact]
    public void Selecting_NoFolderMappedToEmbed_LeavesEverythingOut()
    {
        // Arrange
        var emails = Emails(Email("work", "INBOX"));

        // Act
        var selected = StoredEmailChunkingStore.Selecting(emails, "work", [], ClassificationOff);

        // Assert
        Assert.Empty(selected.AsEnumerable());
    }

    /// <summary>One account's pass never cuts another account's mail, even under a folder alias of the same name.</summary>
    [Fact]
    public void Selecting_AnotherAccountsMail_LeavesItOut()
    {
        // Arrange
        var emails = Emails(Email("home", "INBOX"));

        // Act
        var selected = StoredEmailChunkingStore.Selecting(
            emails,
            "work",
            [WorkInbox, new MailFolderIdentity(MailAccountId.Create("home"), MailFolderAlias.Create("INBOX"))],
            ClassificationOff);

        // Assert
        Assert.Empty(selected.AsEnumerable());
    }

    /// <summary>Nothing expensive happens to a message on its way to the junk folder, and the cut is where that starts.</summary>
    [Fact]
    public void Selecting_MailAVerdictCalledJunk_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.SpamClassification = new EmailSpamClassificationEntity
        {
            StoredEmailId = email.Id,
            Verdict = SpamVerdict.Spam,
            DecidedBy = SpamClassificationStage.Deterministic,
            EvaluatedAt = Now,
        };

        // Act
        var selected = StoredEmailChunkingStore.Selecting(Emails(email), "work", [WorkInbox], ClassificationOn);

        // Assert
        Assert.Empty(selected.AsEnumerable());
    }

    /// <summary>A message still inside the wait a verdict is allowed is held rather than cut.</summary>
    [Fact]
    public void Selecting_MailStillWaitingOnAVerdict_LeavesItOut()
    {
        // Arrange
        var emails = Emails(Email("work", "INBOX"));

        // Act
        var selected = StoredEmailChunkingStore.Selecting(emails, "work", [WorkInbox], ClassificationOn);

        // Assert
        Assert.Empty(selected.AsEnumerable());
    }

    /// <summary>A row the local mailbox no longer holds is not worth a passage.</summary>
    [Fact]
    public void Selecting_TombstonedMail_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.RemoteExpungeObservedAt = Now;

        // Act
        var selected = StoredEmailChunkingStore.Selecting(Emails(email), "work", [WorkInbox], ClassificationOff);

        // Assert
        Assert.Empty(selected.AsEnumerable());
    }

    /// <summary>
    /// A rule declares a move rather than performing one, and the account's next run carries it to the mail server. So
    /// the message is still in the folder it arrived in when this run's cut comes round, and cutting it there would
    /// derive passages under the mapping it is leaving.
    /// </summary>
    [Theory]
    [InlineData(MailboxMutationStage.Recorded)]
    [InlineData(MailboxMutationStage.PlacementIssued)]
    [InlineData(MailboxMutationStage.PlacementConfirmed)]
    [InlineData(MailboxMutationStage.SourceFlaggedDeleted)]
    public void Selecting_MailARuleIsStillRelocating_LeavesItOut(MailboxMutationStage stage)
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.Mutations.Add(Mutation(email, MailboxMutation.Relocate, stage));

        // Act
        var selected = StoredEmailChunkingStore.Selecting(Emails(email), "work", [WorkInbox], ClassificationOff);

        // Assert
        Assert.Empty(selected.AsEnumerable());
    }

    /// <summary>A relocation that has stopped converging moves nothing again, so holding the cut back for it would hold it forever.</summary>
    [Theory]
    [InlineData(MailboxMutationStage.Completed)]
    [InlineData(MailboxMutationStage.Abandoned)]
    public void Selecting_MailWhoseRelocationHasEnded_SelectsIt(MailboxMutationStage stage)
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.Mutations.Add(Mutation(email, MailboxMutation.Relocate, stage));

        // Act
        var selected = StoredEmailChunkingStore.Selecting(Emails(email), "work", [WorkInbox], ClassificationOff);

        // Assert
        Assert.Single(selected.AsEnumerable());
    }

    /// <summary>
    /// A copy leaves this message where it is — the second occurrence is discovered in the destination and walks the
    /// whole pipeline itself — so the mapping this message is cut under is the one it is already in.
    /// </summary>
    [Fact]
    public void Selecting_MailARuleIsCopyingElsewhere_SelectsIt()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.Mutations.Add(Mutation(email, MailboxMutation.Copy, MailboxMutationStage.Recorded));

        // Act
        var selected = StoredEmailChunkingStore.Selecting(Emails(email), "work", [WorkInbox], ClassificationOff);

        // Assert
        Assert.Single(selected.AsEnumerable());
    }

    private static DerivedWorkAdmissionTerms ClassificationOff { get; } = new(
        IsApplied: false,
        [],
        [],
        Now);

    private static DerivedWorkAdmissionTerms ClassificationOn { get; } = new(
        IsApplied: true,
        [],
        [MailFolderAlias.Create("INBOX")],
        Now - TimeSpan.FromMinutes(15));

    private static IQueryable<StoredEmailEntity> Emails(params StoredEmailEntity[] emails) => emails.AsQueryable();

    /// <summary>Builds the message the pass is meant to cut, which every test then takes one fact away from.</summary>
    private static StoredEmailEntity Email(string accountId, string alias)
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
            StoredAt = Now,
            ContentAvailability = StoredEmailContentAvailability.Available,
            RulesEvaluatedAt = Now,
        };

        email.SearchDocument = new EmailSearchDocumentEntity
        {
            StoredEmailId = email.Id,
            StoredEmail = email,
            BodyText = "a body",
            BodyTextBeforeTrimming = "a body",
            ExtractedAt = Now,
        };

        return email;
    }

    /// <summary>Builds the record a rule's declared change is durable as, in the stage the test is about.</summary>
    private static MailboxMutationEntity Mutation(
        StoredEmailEntity email,
        MailboxMutation mutation,
        MailboxMutationStage stage) => new()
        {
            StoredEmailId = email.Id,
            StoredEmail = email,
            MailboxAccountId = email.MailboxAccountId,
            MailFolder = email.MailFolder,
            Mutation = mutation.Name,
            RequesterIdentity = "rule:file-the-newsletters",
            RequesterOrigin = MailboxMutationOrigin.Rule,
            Stage = stage,
            RecordedAt = Now,
            StageChangedAt = Now,
        };
}
