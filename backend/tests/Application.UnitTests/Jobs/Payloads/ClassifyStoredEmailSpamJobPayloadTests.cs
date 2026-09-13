// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Payloads;

public sealed class ClassifyStoredEmailSpamJobPayloadTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("account-a"));

    private static readonly StoredEmailId Email =
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001"));

    /// <summary>The payload is a reference to committed local state, so what goes in has to come back out unchanged.</summary>
    [Fact]
    public void ToStoredEmailId_AfterDescribingAStoredEmail_RebuildsTheSameAccountAndIdentity()
    {
        // Act
        var payload = ClassifyStoredEmailSpamJobPayload.For(Account, Email);

        // Assert
        Assert.Equal(Account, payload.ToAccountIdentity());
        Assert.Equal(Email, payload.ToStoredEmailId());
    }

    /// <summary>
    /// The type names exactly one payload contract, which is what lets a stored document be read back as the shape it
    /// was written as without a discriminator — and why this shape has a type of its own rather than the occurrence's.
    /// </summary>
    [Fact]
    public void JobType_OfAStoredEmailPayload_NamesTheTypeItIsTheContractOf()
    {
        // Act
        var payload = ClassifyStoredEmailSpamJobPayload.For(Account, Email);

        // Assert
        Assert.Equal(JobType.ClassifyStoredEmailSpam, payload.JobType);
    }

    /// <summary>
    /// Job state must not become a second uncontrolled copy of personal data, and the guarantee is structural: there is
    /// no property here to put a subject, an address, or a body in. This test fails the moment one is added.
    /// </summary>
    [Fact]
    public void Payload_DeclaresOnlyTheAccountAndTheStoredIdentity()
    {
        // Arrange
        string[] expected =
        [
            nameof(ClassifyStoredEmailSpamJobPayload.UserId),
            nameof(ClassifyStoredEmailSpamJobPayload.AccountId),
            nameof(ClassifyStoredEmailSpamJobPayload.EmailRecordId),
            nameof(ClassifyStoredEmailSpamJobPayload.JobType),
        ];

        // Act
        var declared = typeof(ClassifyStoredEmailSpamJobPayload)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => !string.Equals(name, "EqualityContract", StringComparison.Ordinal))
            .ToArray();

        // Assert
        Assert.Equal(expected.Order(StringComparer.Ordinal), declared.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A stored document whose identity no longer validates describes work nothing can perform, so it is refused
    /// rather than reconstructed into an identity that would point the work at a different message.
    /// </summary>
    [Fact]
    public void ToStoredEmailId_AStoredIdentityThatIsEmpty_IsRefused()
    {
        // Arrange
        var payload = ClassifyStoredEmailSpamJobPayload.For(Account, Email) with { EmailRecordId = Guid.Empty };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => payload.ToStoredEmailId());
    }
}
