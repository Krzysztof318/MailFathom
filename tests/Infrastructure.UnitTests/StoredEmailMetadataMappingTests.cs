// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Infrastructure.Persistence;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Covers what one message's row keeps, and what it deliberately does not.</summary>
public sealed class StoredEmailMetadataMappingTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 7, 24, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ReceivedAt = new(2026, 7, 24, 8, 0, 12, TimeSpan.Zero);

    [Fact]
    public void ApplyExtractedMetadata_MessageWithParticipantsAndAttachments_RecordsEveryPersistedField()
    {
        // Arrange
        var entity = CreateEntity();
        var metadata = CreateExtractedMetadata(
            participants:
            [
                Participant(EmailAddressRole.From, "Anna Kowalska", "Anna@Example.test"),
                Participant(EmailAddressRole.To, null, "bob@example.test"),
                Participant(EmailAddressRole.To, null, "carol@example.test"),
                Participant(EmailAddressRole.Cc, null, "dan@example.test"),
                Participant(EmailAddressRole.ReplyTo, null, "replies@example.test"),
            ],
            threadReferences: EmailThreadReferences.Create(
                "<own@example.test>",
                "<parent@example.test>",
                ["<root@example.test>", "<parent@example.test>"]),
            attachments: EmailAttachmentSummary.Create(
                [
                    new ExtractedEmailAttachment(FileName("report.pdf"), "application/pdf", 2048),
                    new ExtractedEmailAttachment(null, "image/png", 512),
                ],
                inlineResourceCount: 3,
                isEncrypted: false,
                carriesUnverifiedSignature: true,
                containsUnexpandedTnefPart: false));

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, metadata);

        // Assert
        Assert.Equal("Quarterly report", entity.Subject);
        Assert.Equal(SentAt, entity.SentAt);
        Assert.Equal(ReceivedAt, entity.ReceivedAt);
        Assert.Equal("Anna Kowalska", entity.SenderDisplayName);
        Assert.Equal("Anna@Example.test", entity.SenderAddress);
        Assert.Equal("ANNA@EXAMPLE.TEST", entity.SenderNormalizedAddress);
        Assert.Equal(["BOB@EXAMPLE.TEST", "CAROL@EXAMPLE.TEST"], entity.ToAddresses);
        Assert.Equal(["DAN@EXAMPLE.TEST"], entity.CcAddresses);
        Assert.Equal(["REPLIES@EXAMPLE.TEST"], entity.ReplyToAddresses);
        Assert.Equal("own@example.test", entity.InternetMessageId);
        Assert.Equal("parent@example.test", entity.InReplyTo);
        Assert.Equal(["root@example.test", "parent@example.test"], entity.ThreadReferences);
        Assert.Equal(2, entity.AttachmentCount);
        Assert.Equal(2560, entity.AttachmentTotalSizeOctets);
        Assert.Equal(3, entity.InlineResourceCount);
        Assert.False(entity.IsEncrypted);
        Assert.True(entity.CarriesUnverifiedSignature);
        Assert.False(entity.ContainsUnexpandedTnefPart);
    }

    /// <summary>File names are mail content that no planned query filters on, so the row keeps only the countable part.</summary>
    [Fact]
    public void ApplyExtractedMetadata_MessageWithNamedAttachments_KeepsNoPerAttachmentDetail()
    {
        // Arrange
        var entity = CreateEntity();
        var metadata = CreateExtractedMetadata(
            attachments: EmailAttachmentSummary.Create(
                [new ExtractedEmailAttachment(FileName("payroll.xlsx"), "application/vnd.ms-excel", 64)],
                inlineResourceCount: 0,
                isEncrypted: false,
                carriesUnverifiedSignature: false,
                containsUnexpandedTnefPart: false));

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, metadata);

        // Assert
        var columnsThatCouldCarryAName = new[] { entity.Subject, entity.SenderAddress, entity.InReplyTo }
            .Concat(entity.ToAddresses)
            .Concat(entity.ThreadReferences)
            .ToArray();
        Assert.DoesNotContain("payroll.xlsx", columnsThatCouldCarryAName);
        Assert.Equal(1, entity.AttachmentCount);
        Assert.Equal(64, entity.AttachmentTotalSizeOctets);
    }

    [Fact]
    public void ApplyExtractedMetadata_HeaderRepeatingOneRecipient_RecordsThatRecipientOnce()
    {
        // Arrange
        var entity = CreateEntity();
        var metadata = CreateExtractedMetadata(
            participants:
            [
                Participant(EmailAddressRole.To, null, "bob@example.test"),
                Participant(EmailAddressRole.To, "Bob", "BOB@example.test"),
            ]);

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, metadata);

        // Assert
        Assert.Equal(["BOB@EXAMPLE.TEST"], entity.ToAddresses);
    }

    /// <summary><c>Sender</c> names the submitter, so it stands in only for a message that named no author.</summary>
    [Fact]
    public void ApplyExtractedMetadata_MessageWithoutAnAuthor_FallsBackToTheSubmittingAddress()
    {
        // Arrange
        var entity = CreateEntity();
        var metadata = CreateExtractedMetadata(
            participants: [Participant(EmailAddressRole.Sender, "Mailing list", "list@example.test")]);

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, metadata);

        // Assert
        Assert.Equal("list@example.test", entity.SenderAddress);
        Assert.Equal("LIST@EXAMPLE.TEST", entity.SenderNormalizedAddress);
    }

    [Fact]
    public void ApplyExtractedMetadata_MessageNamingBothAnAuthorAndASubmitter_RecordsTheAuthor()
    {
        // Arrange
        var entity = CreateEntity();
        var metadata = CreateExtractedMetadata(
            participants:
            [
                Participant(EmailAddressRole.Sender, null, "assistant@example.test"),
                Participant(EmailAddressRole.From, null, "author@example.test"),
            ]);

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, metadata);

        // Assert
        Assert.Equal("author@example.test", entity.SenderAddress);
    }

    [Fact]
    public void ApplyExtractedMetadata_MessageCarryingNoMessageIdentifier_KeepsTheOneTheEnvelopeReported()
    {
        // Arrange
        var entity = CreateEntity();
        StoredEmailMetadataMapping.ApplyRemoteSummary(
            entity,
            CreateRemoteMetadata("envelope@example.test"),
            StoredEmailContentAvailability.Available);

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, CreateExtractedMetadata());

        // Assert
        Assert.Equal("envelope@example.test", entity.InternetMessageId);
    }

    [Fact]
    public void ApplyExtractedMetadata_MessageCarryingItsOwnIdentifier_ReplacesTheEnvelopeIdentifier()
    {
        // Arrange
        var entity = CreateEntity();
        StoredEmailMetadataMapping.ApplyRemoteSummary(
            entity,
            CreateRemoteMetadata("envelope@example.test"),
            StoredEmailContentAvailability.Available);

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(
                threadReferences: EmailThreadReferences.Create("<own@example.test>", inReplyTo: null, references: null)));

        // Assert
        Assert.Equal("own@example.test", entity.InternetMessageId);
    }

    /// <summary>An oversized occurrence never reaches a MIME reader, so its row is whatever the server's envelope said.</summary>
    [Fact]
    public void ApplyRemoteSummary_OccurrenceStoredWithoutContent_LeavesEveryExtractedColumnUnset()
    {
        // Arrange
        var entity = CreateEntity();

        // Act
        StoredEmailMetadataMapping.ApplyRemoteSummary(
            entity,
            CreateRemoteMetadata("envelope@example.test"),
            StoredEmailContentAvailability.ExceededSizeLimit);

        // Assert
        Assert.Equal(StoredEmailContentAvailability.ExceededSizeLimit, entity.ContentAvailability);
        Assert.Equal("envelope@example.test", entity.InternetMessageId);
        Assert.Null(entity.ReceivedAt);
        Assert.Null(entity.SenderNormalizedAddress);
        Assert.Empty(entity.ToAddresses);
        Assert.Empty(entity.ThreadReferences);
        Assert.Equal(0, entity.AttachmentCount);
    }

    /// <summary>
    /// Nothing between the mail server and the row bounds a header, so an over-long value is dropped here. Letting it
    /// reach the column would fail the commit, exhaust the retry budget, and leave the folder checkpoint stopped on the
    /// same message for every later run.
    /// </summary>
    [Fact]
    public void ApplyExtractedMetadata_AddressLongerThanItsColumn_IsNotStored()
    {
        // Arrange
        var entity = CreateEntity();
        var overlongLocalPart = new string('a', StoredEmailEntity.MaximumAddressLength);
        var metadata = CreateExtractedMetadata(
            participants:
            [
                Participant(EmailAddressRole.From, null, $"{overlongLocalPart}@example.test"),
                Participant(EmailAddressRole.To, null, $"{overlongLocalPart}@example.test"),
                Participant(EmailAddressRole.To, null, "bob@example.test"),
            ]);

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, metadata);

        // Assert
        Assert.Null(entity.SenderAddress);
        Assert.Equal(["BOB@EXAMPLE.TEST"], entity.ToAddresses);
    }

    /// <summary>A prefix of a message identifier is an identifier another message may carry, so a long one is dropped rather than cut.</summary>
    [Fact]
    public void ApplyExtractedMetadata_IdentifierLongerThanItsColumn_IsNotStored()
    {
        // Arrange
        var entity = CreateEntity();
        var overlongIdentifier = new string('x', StoredEmailEntity.MaximumIdentifierLength + 1);
        var metadata = CreateExtractedMetadata(
            threadReferences: EmailThreadReferences.Create(
                overlongIdentifier,
                overlongIdentifier,
                [overlongIdentifier, "root@example.test"]));

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, metadata);

        // Assert
        Assert.Null(entity.InternetMessageId);
        Assert.Null(entity.InReplyTo);
        Assert.Equal(["root@example.test"], entity.ThreadReferences);
    }

    [Fact]
    public void ApplyExtractedMetadata_HeaderNamingMoreRecipientsThanTheColumnKeeps_StoresTheBoundedPrefix()
    {
        // Arrange
        var entity = CreateEntity();
        var recipients = Enumerable.Range(0, StoredEmailEntity.MaximumAddressesPerRole + 10)
            .Select(index => Participant(EmailAddressRole.To, null, $"recipient{index}@example.test"))
            .ToArray();

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, CreateExtractedMetadata(participants: recipients));

        // Assert
        Assert.Equal(StoredEmailEntity.MaximumAddressesPerRole, entity.ToAddresses.Length);
        Assert.Equal("RECIPIENT0@EXAMPLE.TEST", entity.ToAddresses[0]);
    }

    /// <summary>The nearest ancestors are the end of the path a thread view walks first, so a long chain keeps its tail.</summary>
    [Fact]
    public void ApplyExtractedMetadata_ThreadDeeperThanTheColumnKeeps_StoresTheNearestAncestors()
    {
        // Arrange
        var entity = CreateEntity();
        var ancestorCount = StoredEmailEntity.MaximumThreadReferences + 10;
        var ancestors = Enumerable.Range(0, ancestorCount)
            .Select(index => $"ancestor{index}@example.test")
            .ToArray();

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(
                threadReferences: EmailThreadReferences.Create(messageId: null, inReplyTo: null, ancestors)));

        // Assert
        Assert.Equal(StoredEmailEntity.MaximumThreadReferences, entity.ThreadReferences.Length);
        Assert.Equal($"ancestor{ancestorCount - 1}@example.test", entity.ThreadReferences[^1]);
    }

    [Fact]
    public void ApplyRemoteSummary_EnvelopeIdentifierLongerThanItsColumn_IsNotStored()
    {
        // Arrange
        var entity = CreateEntity();

        // Act
        StoredEmailMetadataMapping.ApplyRemoteSummary(
            entity,
            CreateRemoteMetadata(new string('x', StoredEmailEntity.MaximumIdentifierLength + 1)),
            StoredEmailContentAvailability.Available);

        // Assert
        Assert.Null(entity.InternetMessageId);
    }

    /// <summary>The remote flags are an observation nothing has made yet, and the row must say so rather than imply a read server.</summary>
    [Fact]
    public void ApplyExtractedMetadata_AnyMessage_LeavesTheRemoteFlagSnapshotUnobserved()
    {
        // Arrange
        var entity = CreateEntity();

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, CreateExtractedMetadata());

        // Assert
        Assert.Null(entity.RemoteFlagsObservedAt);
        Assert.False(entity.IsRemotelySeen);
        Assert.False(entity.IsRemotelyAnswered);
        Assert.False(entity.IsRemotelyFlagged);
        Assert.False(entity.IsRemotelyDraft);
        Assert.False(entity.IsRemotelyDeleted);
    }

    private static AttachmentFileName FileName(string decodedFileName)
    {
        Assert.True(AttachmentFileName.TryNormalize(decodedFileName, out var fileName));

        return fileName;
    }

    private static EmailParticipant Participant(EmailAddressRole role, string? displayName, string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName, address, out var emailAddress));

        return new EmailParticipant(role, emailAddress);
    }

    private static ExtractedEmailMetadata CreateExtractedMetadata(
        IReadOnlyList<EmailParticipant>? participants = null,
        EmailThreadReferences? threadReferences = null,
        EmailAttachmentSummary? attachments = null,
        ExtractedEmailText? text = null) =>
        new(
            OccurrenceId,
            "Quarterly report",
            SentAt,
            ReceivedAt,
            participants ?? [],
            threadReferences ?? EmailThreadReferences.None,
            attachments ?? EmailAttachmentSummary.None,
            text ?? ExtractedEmailText.NoTextualBody);

    private static RemoteEmailMetadata CreateRemoteMetadata(string internetMessageId) =>
        new(OccurrenceId, internetMessageId, "Quarterly report", SentAt, SizeOctets: 4096);

    private static EmailOccurrenceId OccurrenceId => EmailOccurrenceId.Create(
        MailAccountId.Create("primary"),
        new MailFolderResolutionId(MailFolderAlias.Create("inbox"), MailFolderResolutionGeneration.First),
        ImapUidValidity.Create(5),
        ImapUid.Create(10));

    private static StoredEmailEntity CreateEntity() => new()
    {
        Id = Guid.CreateVersion7(),
        MailboxAccountId = "primary",
        MailFolder = new MailFolderEntity
        {
            MailboxAccountId = "primary",
            Alias = "inbox",
            RemotePath = "INBOX",
            MailboxAccount = new MailboxAccountEntity { Id = "primary" },
        },
    };
}
