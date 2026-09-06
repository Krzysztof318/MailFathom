// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>Covers which mail has its attachments read, which is where withholding reaches an attachment.</summary>
/// <remarks>
/// <para>
/// The predicate is composed here and evaluated by PostgreSQL, so what these tests establish is which rows it selects
/// rather than what SQL it becomes. It carries the cut's four orderings — the message is local, the rules have finished
/// with it and are not still moving it, its folder is one an operator asked to have embedded, and the classification
/// gate admits it — which is how an attachment on a withheld message is excluded by the rule the pipeline already has
/// rather than by a second evaluation written here.
/// </para>
/// <para>
/// The two clauses of its own are what makes the walk terminate and stay proportional, and each fails silently when it
/// lapses: a mailbox of attachment-free mail read on every run for ever, and a mailbox re-parsed on every run.
/// </para>
/// </remarks>
public sealed class StoredEmailAttachmentTextSelectionTests
{
    private static readonly Guid Owner = SyntheticMailOwner.Deployment.Value;

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    private static readonly MailFolderIdentity WorkInbox = new(
        MailAccountId.Create("work"),
        MailFolderAlias.Create("INBOX"));

    private static readonly MailFolderIdentity WorkArchive = new(
        MailAccountId.Create("work"),
        MailFolderAlias.Create("ARCHIVE"));

    /// <summary>The ordinary arrival: cut, evaluated, admitted, carrying an attachment, and never read.</summary>
    [Fact]
    public void Selecting_MailCarryingAnUnreadAttachment_SelectsIt()
    {
        // Act
        var selected = Selecting(Email("work", "INBOX"), [WorkInbox], ClassificationOff);

        // Assert
        Assert.Single(selected);
    }

    /// <summary>
    /// A body's words and a file's words are independent, so a message whose body yielded nothing may still carry a
    /// contract worth reading — which is why this predicate is the cut's without its search-document clause.
    /// </summary>
    [Fact]
    public void Selecting_MailWhoseBodyYieldedNoWords_StillSelectsItForItsAttachments()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.SearchDocument = null;

        // Act
        var selected = Selecting(email, [WorkInbox], ClassificationOff);

        // Assert
        Assert.Single(selected);
    }

    /// <summary>A message with nothing attached has nothing to read, and reading it on every run would never end.</summary>
    [Fact]
    public void Selecting_MailCarryingNoAttachment_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.AttachmentCount = 0;

        // Act
        var selected = Selecting(email, [WorkInbox], ClassificationOff);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>
    /// The stamp is what takes a message out of this walk. Without it every run would re-open every attachment in the
    /// mailbox, re-parse each one, and — for a picture — pay a provider for a description it already had.
    /// </summary>
    [Fact]
    public void Selecting_MailWhoseAttachmentsWereAlreadyRead_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.AttachmentTextDerivedAt = Now;

        // Act
        var selected = Selecting(email, [WorkInbox], ClassificationOff);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>
    /// The ordering the cut exists for holds here too: a rule may file the message into a folder mapped differently
    /// from the one it arrived in, so reading before the rules ran would read attachments of an unsettled placement.
    /// </summary>
    [Fact]
    public void Selecting_MailTheRulesHaveNotReachedYet_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.RulesEvaluatedAt = null;

        // Act
        var selected = Selecting(email, [WorkInbox], ClassificationOff);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>A folder an operator asked not to have embedded has its attachments left unopened as well.</summary>
    [Fact]
    public void Selecting_AFolderNotMappedToEmbed_LeavesItsMailOut()
    {
        // Act
        var selected = Selecting(Email("work", "INBOX"), [WorkArchive], ClassificationOff);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>One account's run never reads another account's attachments, even under an alias of the same name.</summary>
    [Fact]
    public void Selecting_AnotherAccountsMail_LeavesItOut()
    {
        // Act
        var selected = Selecting(
            Email("home", "INBOX"),
            [WorkInbox, new MailFolderIdentity(MailAccountId.Create("home"), MailFolderAlias.Create("INBOX"))],
            ClassificationOff);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>
    /// Withholding reaches an attachment through the gate the pipeline already applies rather than through a second
    /// evaluation: nothing expensive happens to a message on its way to the junk folder, a parse least of all.
    /// </summary>
    [Fact]
    public void Selecting_MailAVerdictCalledJunk_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.SpamClassification = new EmailSpamClassificationEntity
        {
            StoredEmailId = email.Id,
            StoredEmail = email,
            Verdict = SpamVerdict.Spam,
            EvaluatedAt = Now,
        };

        // Act
        var selected = Selecting(email, [WorkInbox], ClassificationOn);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>A message a deletion left behind as a marker carries nothing to open.</summary>
    [Fact]
    public void Selecting_ATombstonedMessage_LeavesItOut()
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.RemoteExpungeObservedAt = Now;

        // Act
        var selected = Selecting(email, [WorkInbox], ClassificationOff);

        // Assert
        Assert.Empty(selected);
    }

    private static DerivedWorkAdmissionTerms ClassificationOff { get; } = new([], [], [], Now);

    private static DerivedWorkAdmissionTerms ClassificationOn { get; } = new(
        [MailAccountId.Create("work")],
        [],
        [WorkInbox],
        Now - TimeSpan.FromMinutes(15));

    /// <summary>A relocation still converging moves the message to a folder whose mapping may embed nothing.</summary>
    /// <remarks>
    /// The clause matters most here of the three passes that carry it: reading an attachment opens a stranger's file and
    /// may send a picture to a vision provider, so doing it under the mapping the message is leaving would spend that on
    /// a folder an operator never asked to have embedded.
    /// </remarks>
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
        var selected = Selecting(email, [WorkInbox], ClassificationOff);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>A relocation that has stopped converging moves nothing again, so waiting for it would wait for ever.</summary>
    [Theory]
    [InlineData(MailboxMutationStage.Completed)]
    [InlineData(MailboxMutationStage.Abandoned)]
    [InlineData(MailboxMutationStage.Cancelled)]
    public void Selecting_MailWhoseRelocationHasEnded_SelectsIt(MailboxMutationStage stage)
    {
        // Arrange
        var email = Email("work", "INBOX");
        email.Mutations.Add(Mutation(email, MailboxMutation.Relocate, stage));

        // Act
        var selected = Selecting(email, [WorkInbox], ClassificationOff);

        // Assert
        Assert.Single(selected);
    }

    /// <summary>One mutation of the message, as the relocation clause reads it.</summary>
    private static MailboxMutationEntity Mutation(
        StoredEmailEntity email,
        MailboxMutation mutation,
        MailboxMutationStage stage) => new()
        {
            StoredEmailId = email.Id,
            StoredEmail = email,
            OwnerId = email.OwnerId,
            MailboxAccountId = email.MailboxAccountId,
            MailFolder = email.MailFolder,
            Mutation = mutation.Name,
            RequesterIdentity = "rule:file-the-newsletters",
            RequesterOrigin = MailboxMutationOrigin.Rule,
            Stage = stage,
            RecordedAt = Now,
            StageChangedAt = Now,
        };

    private static IReadOnlyList<StoredEmailEntity> Selecting(
        StoredEmailEntity email,
        IReadOnlyList<MailFolderIdentity> embeddedFolders,
        DerivedWorkAdmissionTerms terms) =>
        [.. StoredEmailAttachmentTextStore.Selecting(
            new[] { email }.AsQueryable(),
            Owner,
            "work",
            embeddedFolders,
            terms)];

    /// <summary>Builds the message the pass is meant to read, which every test then takes one fact away from.</summary>
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
            AttachmentCount = 1,
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
}
