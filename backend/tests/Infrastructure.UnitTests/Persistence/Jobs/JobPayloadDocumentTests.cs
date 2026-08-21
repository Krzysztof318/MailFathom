// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Jobs;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Jobs;

public sealed class JobPayloadDocumentTests
{
    private static ClassifyEmailSpamJobPayload Payload => ClassifyEmailSpamJobPayload.For(EmailOccurrenceId.Create(
        MailAccountId.Create("account-a"),
        new MailFolderResolutionId(
            MailFolderAlias.Create("inbox"),
            MailFolderResolutionGeneration.Create(2)),
        ImapUidValidity.Create(12345),
        ImapUid.Create(4711)));

    /// <summary>
    /// The job type decides the contract on both sides, so a stored document is always read back as the shape it was
    /// written as — which is what removes the discriminator a polymorphic document would otherwise need.
    /// </summary>
    [Fact]
    public void Serialize_ThenDeserialize_ReadsBackTheShapeItWasWrittenAs()
    {
        // Act
        var document = JobPayloadDocument.Serialize(Payload);
        var restored = JobPayloadDocument.Deserialize(JobType.ClassifyEmailSpam, document);

        // Assert
        Assert.Equal(Payload, restored);
    }

    /// <summary>The document is read by an operator looking at a queue, so its property names are the ones they see.</summary>
    [Fact]
    public void Serialize_AnOccurrencePayload_WritesTheReferencesAndNothingElse()
    {
        // Act
        var document = JobPayloadDocument.Serialize(Payload);

        // Assert
        Assert.Equal(
            """{"accountId":"account-a","folderAlias":"INBOX","folderResolutionGeneration":2,"uidValidity":12345,"uid":4711}""",
            document);
    }

    /// <summary>A recurring dispatch stores an account and nothing about the mail in it, and reads it back the same way.</summary>
    [Fact]
    public void Serialize_AnAccountPayload_WritesTheAccountAndReadsItBackAsTheSameReference()
    {
        // Arrange
        var payload = RunScheduledMailRulesJobPayload.For(MailAccountId.Create("account-a"));

        // Act
        var document = JobPayloadDocument.Serialize(payload);

        // Assert
        Assert.Equal("""{"accountId":"account-a"}""", document);
        Assert.Equal(payload, JobPayloadDocument.Deserialize(JobType.RunScheduledMailRules, document));
    }

    /// <summary>
    /// A message waiting for the time it was written to leave at is named by the account and the record, and by nothing
    /// about the message — a queued job an operator reads must say which send it is for and no more than that.
    /// </summary>
    [Fact]
    public void Serialize_AHeldSendPayload_WritesTheRecordAndReadsItBackAsTheSameReferences()
    {
        // Arrange
        var payload = HeldSendJobPayload.For(
            MailAccountId.Create("account-a"),
            OutgoingEmailId.Create(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff")));

        // Act
        var document = JobPayloadDocument.Serialize(payload);

        // Assert
        Assert.Equal(
            """{"accountId":"account-a","outgoingRecordId":"6f9619ff-8b86-d011-b42d-00c04fc964ff"}""",
            document);
        Assert.Equal(payload, JobPayloadDocument.Deserialize(JobType.DispatchHeldSend, document));
    }

    /// <summary>
    /// A recurring dispatch names the declaration rather than the occasion, so its document is the same for the life of
    /// the declaration and carries nothing about the message the occasions repeat.
    /// </summary>
    [Fact]
    public void Serialize_ARecurringSendPayload_WritesTheDeclarationAndReadsItBackAsTheSameReferences()
    {
        // Arrange
        var payload = RecurringSendJobPayload.For(
            MailAccountId.Create("account-a"),
            RecurringSendId.Create(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff")));

        // Act
        var document = JobPayloadDocument.Serialize(payload);

        // Assert
        Assert.Equal(
            """{"accountId":"account-a","declarationId":"6f9619ff-8b86-d011-b42d-00c04fc964ff"}""",
            document);
        Assert.Equal(payload, JobPayloadDocument.Deserialize(JobType.SendRecurringOccurrence, document));
    }

    /// <summary>
    /// A payload holds references and every reference this system composes is short, so a document over the bound is
    /// evidence that something copied content into job state. It is refused rather than truncated or stored.
    /// </summary>
    [Fact]
    public void Serialize_ADocumentOverTheBound_IsRefusedRatherThanStored()
    {
        // Arrange
        var oversized = Payload with { FolderAlias = new string('f', JobPayloadDocument.MaximumByteCount) };

        // Act
        var refusal = Assert.Throws<JobPayloadTooLargeException>(() => JobPayloadDocument.Serialize(oversized));

        // Assert
        Assert.Equal(JobType.ClassifyEmailSpam, refusal.JobType);
        Assert.Equal(JobPayloadDocument.MaximumByteCount, refusal.MaximumByteCount);
        Assert.True(refusal.SerializedByteCount > JobPayloadDocument.MaximumByteCount);
    }

    /// <summary>The bound is on bytes rather than characters, because that is what the column and the transport carry.</summary>
    [Fact]
    public void Serialize_ADocumentWhoseCharactersFitButWhoseBytesDoNot_IsRefused()
    {
        // Arrange
        var multiByteAlias = new string('ł', (JobPayloadDocument.MaximumByteCount / 2) + 1);
        var oversized = Payload with { FolderAlias = multiByteAlias };

        // Act & Assert
        Assert.True(multiByteAlias.Length <= JobPayloadDocument.MaximumByteCount);
        Assert.Throws<JobPayloadTooLargeException>(() => JobPayloadDocument.Serialize(oversized));
    }

    [Fact]
    public void Serialize_APayloadNamingNoDeclaredType_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => JobPayloadDocument.Serialize(new UnspecifiedJobPayload()));
    }

    /// <summary>A payload record with no entry in the serialization context is a defect rather than a shape to guess at.</summary>
    [Fact]
    public void Serialize_APayloadWithNoContractInThisStore_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => JobPayloadDocument.Serialize(new UncontractedJobPayload()));
    }

    /// <summary>
    /// A stored document that no longer parses describes work nothing can perform, so the read stops rather than
    /// producing a plausible reconstruction that would point the work at something else.
    /// </summary>
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("""{"folderAlias":"inbox"}""")]
    public void Deserialize_ADocumentThatIsNotTheContractOfItsType_IsRefused(string document)
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => JobPayloadDocument.Deserialize(JobType.ClassifyEmailSpam, document));
    }

    [Fact]
    public void Deserialize_UnderTheUnspecifiedType_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => JobPayloadDocument.Deserialize(default, "{}"));
    }

    private sealed record UnspecifiedJobPayload : IJobPayload
    {
        public JobType JobType => default;
    }

    private sealed record UncontractedJobPayload : IJobPayload
    {
        public JobType JobType => JobType.ClassifyEmailSpam;
    }
}
