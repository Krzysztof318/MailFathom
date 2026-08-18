// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

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

    /// <summary>The verdict reaches its own columns in the comparison form, which is what a later reader matches on.</summary>
    [Fact]
    public void ApplyExtractedMetadata_AuthenticatedMessage_RecordsTheWholeVerdict()
    {
        // Arrange
        var entity = CreateEntity();
        Assert.True(SenderDomain.TryCreate("Signer.Test", out var dkimDomain));
        Assert.True(SenderDomain.TryCreate("relay.test", out var spfDomain));
        Assert.True(SenderDomain.TryCreate("bank.test", out var fromDomain));

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(
                senderAuthentication: SenderAuthentication.Authenticated(
                    [dkimDomain],
                    [spfDomain],
                    fromDomain,
                    DmarcOutcome.Fail)));

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, entity.SenderAuthenticationOutcome);
        Assert.Equal(SenderAuthenticationMethod.DomainKeysIdentifiedMail, entity.SenderAuthenticationMethod);
        Assert.Equal("SIGNER.TEST", entity.AuthenticatedSenderDomain);
        Assert.Equal("SIGNER.TEST", entity.DkimSignerDomain);
        Assert.Equal("RELAY.TEST", entity.SpfMailFromDomain);
        Assert.Equal("BANK.TEST", entity.DisplayedAuthorDomain);
        Assert.Equal(DmarcOutcome.Fail, entity.DmarcOutcome);
        Assert.Equal(AuthorAuthenticationOutcome.Failed, entity.AuthorAuthenticationOutcome);
        Assert.Null(entity.AuthenticatedAuthorDomain);
    }

    /// <summary>
    /// The authorship reading reaches its own four columns, each carrying its own part of it. A later read rebuilds the
    /// assessment from exactly these values, so a band recorded without the number it was reached from, or a number
    /// recorded without the weighting that produced it, is a row nothing can interpret.
    /// </summary>
    [Fact]
    public void ApplyExtractedMetadata_AnAssessedMessage_RecordsTheWholeReading()
    {
        // Arrange
        var entity = CreateEntity();
        var revision = MachineAuthorshipProfile.Standard.Revision;

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(
                machineAuthorship: MachineAuthorshipAssessment.Assessed(
                    MachineAuthorshipBand.Possible,
                    likelihood: 0.42,
                    MachineAuthorshipSignals.HiddenCharacters | MachineAuthorshipSignals.UnspacedEmDashes,
                    revision)));

        // Assert
        Assert.Equal(MachineAuthorshipBand.Possible, entity.MachineAuthorshipBand);
        Assert.Equal(0.42, entity.MachineAuthorshipLikelihood);
        Assert.Equal(
            MachineAuthorshipSignals.HiddenCharacters | MachineAuthorshipSignals.UnspacedEmDashes,
            entity.MachineAuthorshipSignals);
        Assert.Equal(revision.Value, entity.MachineAuthorshipProfileRevision);
    }

    /// <summary>
    /// A message nothing read stores the state of a message nothing read, and names no profile: a revision on such a
    /// row would say a weighting reached the lowest number rather than that nothing was weighed.
    /// </summary>
    [Fact]
    public void ApplyExtractedMetadata_AMessageNothingAssessed_RecordsNoReadingAndNoProfile()
    {
        // Arrange
        var entity = CreateEntity();

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, CreateExtractedMetadata());

        // Assert
        Assert.Equal(MachineAuthorshipBand.NotAssessed, entity.MachineAuthorshipBand);
        Assert.Equal(0, entity.MachineAuthorshipLikelihood);
        Assert.Equal(MachineAuthorshipSignals.None, entity.MachineAuthorshipSignals);
        Assert.Null(entity.MachineAuthorshipProfileRevision);
    }

    /// <summary>The displayed author's domain is recorded whether or not anything established it.</summary>
    /// <remarks>
    /// It is the half of the comparison a reader needs most where the author was not established, which is exactly
    /// where the column holding the established author is empty.
    /// </remarks>
    [Fact]
    public void ApplyExtractedMetadata_UnestablishedAuthor_StillRecordsTheDisplayedDomain()
    {
        // Arrange
        var entity = CreateEntity();
        Assert.True(SenderDomain.TryCreate("Bank.Test", out var fromDomain));

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(
                senderAuthentication: SenderAuthentication.Failed(fromDomain, DmarcOutcome.Fail)));

        // Assert
        Assert.Equal("BANK.TEST", entity.DisplayedAuthorDomain);
        Assert.Null(entity.AuthenticatedAuthorDomain);
        Assert.Null(entity.AuthenticatedSenderDomain);
    }

    /// <summary>A message that wrote no author displays none, whatever address the timeline names it by.</summary>
    /// <remarks>
    /// The row's sender falls back to the submitting address, and the displayed author's domain deliberately does not:
    /// the two answer different questions, which is why the domain is stored rather than derived from the address.
    /// </remarks>
    [Fact]
    public void ApplyExtractedMetadata_MessageWithoutAnAuthor_RecordsNoDisplayedDomainBesideTheSubmittersAddress()
    {
        // Arrange
        var entity = CreateEntity();

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(
                participants: [Participant(EmailAddressRole.Sender, null, "list@example.test")],
                senderAuthentication: SenderAuthentication.NotEstablished()));

        // Assert
        Assert.Equal("list@example.test", entity.SenderAddress);
        Assert.Null(entity.DisplayedAuthorDomain);
    }

    /// <summary>An established author reaches a column of its own, so a stored trust verdict names who it judged.</summary>
    [Fact]
    public void ApplyExtractedMetadata_EstablishedAuthor_RecordsTheAuthorBesideTheTransportIdentity()
    {
        // Arrange
        var entity = CreateEntity();
        Assert.True(SenderDomain.TryCreate("provider.test", out var dkimDomain));
        Assert.True(SenderDomain.TryCreate("Bank.Test", out var fromDomain));

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(
                senderAuthentication: SenderAuthentication.Authenticated(
                    [dkimDomain],
                    spfDomains: [],
                    fromDomain,
                    DmarcOutcome.Pass)));

        // Assert
        Assert.Equal("PROVIDER.TEST", entity.AuthenticatedSenderDomain);
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, entity.AuthorAuthenticationOutcome);
        Assert.Equal("BANK.TEST", entity.AuthenticatedAuthorDomain);
    }

    /// <summary>A re-derivation that establishes nothing must clear what a previous reading recorded, not leave half of it.</summary>
    [Fact]
    public void ApplyExtractedMetadata_NotEstablishedAfterAnAuthenticatedReading_ClearsTheWholeVerdict()
    {
        // Arrange
        var entity = CreateEntity();
        Assert.True(SenderDomain.TryCreate("signer.test", out var dkimDomain));
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(
                senderAuthentication: SenderAuthentication.Authenticated(
                    [dkimDomain],
                    spfDomains: [],
                    dkimDomain,
                    DmarcOutcome.Pass)));

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(senderAuthentication: SenderAuthentication.NotEstablished()));

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, entity.SenderAuthenticationOutcome);
        Assert.Equal(SenderAuthenticationMethod.None, entity.SenderAuthenticationMethod);
        Assert.Null(entity.AuthenticatedSenderDomain);
        Assert.Null(entity.DkimSignerDomain);
        Assert.Null(entity.SpfMailFromDomain);
        Assert.Null(entity.DisplayedAuthorDomain);
        Assert.Equal(DmarcOutcome.NotReported, entity.DmarcOutcome);
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, entity.AuthorAuthenticationOutcome);
        Assert.Null(entity.AuthenticatedAuthorDomain);
    }

    /// <summary>What this deployment made of the author reaches its own columns beside the identity it judged.</summary>
    [Fact]
    public void ApplyExtractedMetadata_RecognizedAuthor_RecordsTheVerdictAndThePolicyThatReachedIt()
    {
        // Arrange
        var entity = CreateEntity();
        var revision = SenderTrustPolicyRevision.Of(["domain:PARTNER.EXAMPLE"]);

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(
                senderTrust: SenderTrust.Trusted(
                    SenderTrustSource.ConfiguredTrustedSender,
                    revision)));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, entity.SenderTrustLevel);
        Assert.Equal(SenderTrustSource.ConfiguredTrustedSender, entity.SenderTrustGrantedBy);
        Assert.Equal(revision.Value, entity.SenderTrustPolicyRevision);
    }

    /// <summary>A row no policy judged says so through an absent revision rather than through an empty one.</summary>
    [Fact]
    public void ApplyExtractedMetadata_ReadingNoPolicyJudged_LeavesTheRevisionAbsent()
    {
        // Arrange
        var entity = CreateEntity();

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, CreateExtractedMetadata());

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, entity.SenderTrustLevel);
        Assert.Equal(SenderTrustSource.None, entity.SenderTrustGrantedBy);
        Assert.Null(entity.SenderTrustPolicyRevision);
    }

    /// <summary>A re-derivation under a narrower list must replace the whole verdict, not leave the half that recognized.</summary>
    [Fact]
    public void ApplyExtractedMetadata_UnknownAfterARecognizedReading_ReplacesTheWholeVerdict()
    {
        // Arrange
        var entity = CreateEntity();
        var narrowed = SenderTrustPolicyRevision.Of(["domain:ELSEWHERE.EXAMPLE"]);
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(
                senderTrust: SenderTrust.Trusted(
                    SenderTrustSource.StoredTrustedSender,
                    SenderTrustPolicyRevision.Of(["domain:PARTNER.EXAMPLE"]))));

        // Act
        StoredEmailMetadataMapping.ApplyExtractedMetadata(
            entity,
            CreateExtractedMetadata(senderTrust: SenderTrust.Unknown(narrowed)));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, entity.SenderTrustLevel);
        Assert.Equal(SenderTrustSource.None, entity.SenderTrustGrantedBy);
        Assert.Equal(narrowed.Value, entity.SenderTrustPolicyRevision);
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
        ExtractedEmailText? text = null,
        SenderAuthentication? senderAuthentication = null,
        SenderTrust? senderTrust = null,
        MachineAuthorshipAssessment? machineAuthorship = null) =>
        new(
            OccurrenceId,
            "Quarterly report",
            SentAt,
            ReceivedAt,
            participants ?? [],
            threadReferences ?? EmailThreadReferences.None,
            attachments ?? EmailAttachmentSummary.None,
            text ?? ExtractedEmailText.NoTextualBody,
            senderAuthentication ?? SenderAuthentication.NotEstablished())
        {
            SenderTrust = senderTrust ?? SenderTrust.NotEvaluated,
            MachineAuthorship = machineAuthorship ?? MachineAuthorshipAssessment.NotAssessed,
        };

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
