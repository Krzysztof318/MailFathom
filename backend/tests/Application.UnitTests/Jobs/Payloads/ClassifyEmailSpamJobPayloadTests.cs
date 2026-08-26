// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Payloads;

public sealed class ClassifyEmailSpamJobPayloadTests
{
    private static EmailOccurrenceId Occurrence => EmailOccurrenceId.Create(
        MailAccountId.Create("account-a"),
        new MailFolderResolutionId(
            MailFolderAlias.Create("inbox"),
            MailFolderResolutionGeneration.Create(2)),
        ImapUidValidity.Create(12345),
        ImapUid.Create(4711));

    /// <summary>The payload is a reference to committed local state, so what goes in has to come back out unchanged.</summary>
    [Fact]
    public void ToOccurrenceId_AfterDescribingAnOccurrence_RebuildsTheSameIdentity()
    {
        // Act
        var payload = ClassifyEmailSpamJobPayload.For(SyntheticMailOwner.Deployment, Occurrence);

        // Assert
        Assert.Equal(Occurrence, payload.ToOccurrenceId());
    }

    /// <summary>
    /// The type names exactly one payload contract, which is what lets a stored document be read back as the shape it
    /// was written as without a discriminator.
    /// </summary>
    [Fact]
    public void JobType_OfAnOccurrencePayload_NamesTheTypeItIsTheContractOf()
    {
        // Act
        var payload = ClassifyEmailSpamJobPayload.For(SyntheticMailOwner.Deployment, Occurrence);

        // Assert
        Assert.Equal(JobType.ClassifyEmailSpam, payload.JobType);
    }

    /// <summary>
    /// Job state must not become a second uncontrolled copy of personal data, and the guarantee is structural: there is
    /// no property here to put a subject, an address, or a body in. This test fails the moment one is added.
    /// </summary>
    [Fact]
    public void Payload_DeclaresOnlyTheComponentsOfAnOccurrenceIdentity()
    {
        // Arrange
        string[] expected =
        [
            nameof(ClassifyEmailSpamJobPayload.OwnerId),
            nameof(ClassifyEmailSpamJobPayload.AccountId),
            nameof(ClassifyEmailSpamJobPayload.FolderAlias),
            nameof(ClassifyEmailSpamJobPayload.FolderResolutionGeneration),
            nameof(ClassifyEmailSpamJobPayload.UidValidity),
            nameof(ClassifyEmailSpamJobPayload.Uid),
            nameof(ClassifyEmailSpamJobPayload.JobType),
        ];

        // Act
        var declared = typeof(ClassifyEmailSpamJobPayload)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => !string.Equals(name, "EqualityContract", StringComparison.Ordinal))
            .ToArray();

        // Assert
        Assert.Equal(expected.Order(StringComparer.Ordinal), declared.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void For_NoOccurrence_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ClassifyEmailSpamJobPayload.For(SyntheticMailOwner.Deployment, null!));
    }

    /// <summary>
    /// A stored document whose components no longer validate describes work nothing can perform, so it is refused
    /// rather than reconstructed into an identity that would point the work at a different message.
    /// </summary>
    [Fact]
    public void ToOccurrenceId_AStoredComponentThatNoLongerValidates_IsRefused()
    {
        // Arrange
        var payload = ClassifyEmailSpamJobPayload.For(SyntheticMailOwner.Deployment, Occurrence) with { Uid = 0 };

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(payload.ToOccurrenceId);
    }
}
