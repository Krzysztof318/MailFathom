// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Enrichment;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Enrichment;

/// <summary>Covers which mail the account run derives marks from, which is the last of the arrival pipeline's orderings.</summary>
/// <remarks>
/// <para>
/// The predicate is composed here and evaluated by PostgreSQL, so what these tests establish is which rows it selects
/// rather than what SQL it becomes. It carries the cut's orderings for the cut's own reasons, and two of its own: a
/// derivation is taken once and never revisited, so a message reaching it before every passage it will ever have exists
/// would be recorded as a reading of half a message, permanently and silently.
/// </para>
/// <para>
/// Both passes in front of this one walk their own queues from their own fronts, so neither's batch bounds the other's
/// and neither ordering can be assumed from the order the stages run in. That is why waiting for each of them is a
/// clause here rather than a consequence of the supervisor's sequence.
/// </para>
/// </remarks>
public sealed class StoredEmailEnrichmentSelectionTests
{
    private static readonly Guid Owner = SyntheticMailOwner.Deployment.Value;

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    private static readonly MailFolderIdentity WorkInbox = new(
        MailAccountId.Create("work"),
        MailFolderAlias.Create("INBOX"));

    private static readonly MailFolderIdentity WorkArchive = new(
        MailAccountId.Create("work"),
        MailFolderAlias.Create("ARCHIVE"));

    /// <summary>The ordinary arrival: cut, admitted, and nothing derived from it yet.</summary>
    [Fact]
    public void Selecting_CutMailNoDerivationHasReached_SelectsIt()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.Chunks.Add(BodyPassage(email, 0));

        // Act
        var selected = Select(email);

        // Assert
        Assert.Single(selected);
    }

    /// <summary>Writing the record is what takes a message out of this query, which is what lets the pass run without a cursor.</summary>
    [Fact]
    public void Selecting_MailAlreadyDerivedFrom_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.Chunks.Add(BodyPassage(email, 0));
        email.Enrichment = new EmailEnrichmentEntity { StoredEmailId = email.Id, DerivedAt = Now };

        // Act
        var selected = Select(email);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>A mark cites passages, so a message with none has nothing for a reading to rest on.</summary>
    [Fact]
    public void Selecting_MailWithNoPassages_LeavesItOut()
    {
        // Act
        var selected = Select(Email("work", "INBOX"));

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>
    /// The ordering this stage is last for. Reading a message's attachments does not wait for its body to be cut, so a
    /// message whose attachment was read first carries passages while its body is still uncut — and a derivation taken
    /// there would be a permanent reading of the attachments alone.
    /// </summary>
    [Fact]
    public void Selecting_MailWhoseAttachmentWasReadBeforeItsBodyWasCut_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.AttachmentCount = 1;
        email.AttachmentTextDerivedAt = Now;
        email.Chunks.Add(AttachmentPassage(email, 0));

        // Act
        var selected = Select(email);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>
    /// The mirror of the case above. A message whose body is cut but whose attachment is still unread would be derived
    /// from without the part somebody attached, which on a quotation or an invoice is the whole of what it says.
    /// </summary>
    [Fact]
    public void Selecting_CutMailWhoseAttachmentIsStillUnread_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.AttachmentCount = 1;
        email.Chunks.Add(BodyPassage(email, 0));

        // Act
        var selected = Select(email);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>
    /// A deployment that reads no attachment never derives text from one, so waiting for a reading that will not happen
    /// would hold every message carrying an attachment out of this queue for the life of the deployment.
    /// </summary>
    [Fact]
    public void Selecting_AnUnreadAttachmentWhereTheDeploymentReadsNone_SelectsItAnyway()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.AttachmentCount = 1;
        email.Chunks.Add(BodyPassage(email, 0));

        // Act
        var selected = Select(email, readsAttachments: false);

        // Assert
        Assert.Single(selected);
    }

    /// <summary>
    /// A message nobody could extract a body from will never gain a body passage, so waiting for one would hold it
    /// here for ever. Its attachment's passages are what a reading rests on instead.
    /// </summary>
    [Fact]
    public void Selecting_MailWithNoExtractedBodyWhoseAttachmentWasRead_SelectsIt()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.SearchDocument!.BodyText = null;
        email.AttachmentCount = 1;
        email.AttachmentTextDerivedAt = Now;
        email.Chunks.Add(AttachmentPassage(email, 0));

        // Act
        var selected = Select(email);

        // Assert
        Assert.Single(selected);
    }

    /// <summary>
    /// Extraction has not reached the message at all, which is a different state from having read it and found no body:
    /// the words of this message arrive on a later run, and a derivation taken now would settle permanently without
    /// them.
    /// </summary>
    /// <remarks>
    /// Reachable because attachment reading asks nothing about the search document, so an attachment can be read and
    /// cut while extraction is still outstanding — which is exactly what the backfill walking every message without a
    /// document exists for.
    /// </remarks>
    [Fact]
    public void Selecting_MailExtractionHasNotReachedWhoseAttachmentWasRead_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.SearchDocument = null;
        email.AttachmentCount = 1;
        email.AttachmentTextDerivedAt = Now;
        email.Chunks.Add(AttachmentPassage(email, 0));

        // Act
        var selected = Select(email);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>A rule may file the message into a folder mapped differently, and a reading is derived where it settles.</summary>
    [Fact]
    public void Selecting_MailTheRulesHaveNotReachedYet_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.RulesEvaluatedAt = null;
        email.Chunks.Add(BodyPassage(email, 0));

        // Act
        var selected = Select(email);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>A relocation a rule declared has not happened yet, so the message is not where it will be read.</summary>
    [Theory]
    [InlineData(MailboxMutationStage.Recorded)]
    [InlineData(MailboxMutationStage.PlacementIssued)]
    public void Selecting_MailARuleIsStillRelocating_LeavesItOut(MailboxMutationStage stage)
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.Chunks.Add(BodyPassage(email, 0));
        email.Mutations.Add(Mutation(email, stage));

        // Act
        var selected = Select(email);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>Nothing a provider is paid for happens to a message on its way to the junk folder.</summary>
    [Fact]
    public void Selecting_MailAVerdictCalledJunk_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.Chunks.Add(BodyPassage(email, 0));
        email.SpamClassification = new EmailSpamClassificationEntity
        {
            StoredEmailId = email.Id,
            Verdict = SpamVerdict.Spam,
            DecidedBy = SpamClassificationStage.Deterministic,
            EvaluatedAt = Now,
        };

        // Act
        var selected = Select(email, terms: ClassificationOn);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>A verdict still inside the wait it is allowed holds the derivation rather than paying for it early.</summary>
    [Fact]
    public void Selecting_MailStillWaitingOnAVerdict_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.Chunks.Add(BodyPassage(email, 0));

        // Act
        var selected = Select(email, terms: ClassificationOn);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>A folder an operator asked not to embed pays for no derivation either.</summary>
    [Fact]
    public void Selecting_AFolderNotMappedToEmbed_LeavesItsMailOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.Chunks.Add(BodyPassage(email, 0));

        // Act
        var selected = Select(email, folders: [WorkArchive]);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>One account's pass never derives from another account's mail, even under a folder alias of the same name.</summary>
    [Fact]
    public void Selecting_AnotherAccountsMail_LeavesItOut()
    {
        // Arrange
        var email = Email("home", "INBOX");
        email.Chunks.Add(BodyPassage(email, 0));

        // Act
        var selected = Select(
            email,
            folders: [WorkInbox, new MailFolderIdentity(MailAccountId.Create("home"), MailFolderAlias.Create("INBOX"))]);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>A row the local mailbox no longer holds is not worth a provider call.</summary>
    [Fact]
    public void Selecting_TombstonedMail_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.RemoteExpungeObservedAt = Now;
        email.Chunks.Add(BodyPassage(email, 0));

        // Act
        var selected = Select(email);

        // Assert
        Assert.Empty(selected);
    }

    private static DerivedWorkAdmissionTerms ClassificationOff { get; } = new([], [], [], Now);

    private static DerivedWorkAdmissionTerms ClassificationOn { get; } = new(
        [MailAccountId.Create("work")],
        [],
        [WorkInbox],
        Now - TimeSpan.FromMinutes(15));

    private static IReadOnlyList<StoredEmailEntity> Select(
        StoredEmailEntity email,
        IReadOnlyList<MailFolderIdentity>? folders = null,
        bool readsAttachments = true,
        DerivedWorkAdmissionTerms? terms = null) =>
        [
            .. StoredEmailEnrichmentStore.Selecting(
                new[] { email }.AsQueryable(),
                Owner,
                "work",
                folders ?? [WorkInbox],
                readsAttachments,
                terms ?? ClassificationOff),
        ];

    /// <summary>Builds the message the pass is meant to derive from, which every test then takes one fact away from.</summary>
    private static StoredEmailEntity Email(string accountId, string alias)
    {
        var email = new StoredEmailEntity
        {
            OwnerId = Owner,
            MailboxAccountId = accountId,
            MailFolder = new MailFolderEntity
            {
                OwnerId = Owner,
                MailboxAccountId = accountId,
                Alias = alias,
                RemotePath = alias,
                MailboxAccount = new MailboxAccountEntity { OwnerId = Owner, Id = accountId },
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

    private static EmailChunkEntity BodyPassage(StoredEmailEntity email, int ordinal) => new()
    {
        StoredEmailId = email.Id,
        StoredEmail = email,
        Ordinal = ordinal,
        Text = "a passage",
        ContentHash = "h",
    };

    private static EmailChunkEntity AttachmentPassage(StoredEmailEntity email, int ordinal) => new()
    {
        StoredEmailId = email.Id,
        StoredEmail = email,
        Ordinal = ordinal,
        AttachmentPosition = 0,
        Text = "a passage",
        ContentHash = "h",
    };

    /// <summary>Builds the record a rule's declared change is durable as, in the stage the test is about.</summary>
    private static MailboxMutationEntity Mutation(StoredEmailEntity email, MailboxMutationStage stage) => new()
    {
        StoredEmailId = email.Id,
        StoredEmail = email,
        OwnerId = email.OwnerId,
        MailboxAccountId = email.MailboxAccountId,
        MailFolder = email.MailFolder,
        Mutation = MailboxMutation.Relocate.Name,
        RequesterIdentity = "rule:file-the-newsletters",
        RequesterOrigin = MailboxMutationOrigin.Rule,
        Stage = stage,
        RecordedAt = Now,
        StageChangedAt = Now,
    };
}
