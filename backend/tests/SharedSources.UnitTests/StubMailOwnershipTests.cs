// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the ownership answer every suite bounding two mailboxes against each other arranges through.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement. A stub that answered with one mailbox whatever it was asked
/// would make a ceiling test pass while the ceiling bounded nothing, which is exactly the claim those tests exist to
/// make; and one that ignored its default would leave every suite that arranges no mailbox at all refused.
/// </remarks>
public sealed class StubMailOwnershipTests
{
    private static readonly StoredEmailId Message = StoredEmailId.Create(Guid.NewGuid());

    [Fact]
    public async Task ReadStoredEmailAccountAsync_NothingArranged_AnswersWithTheDefaultMailbox()
    {
        // Arrange
        var ownership = new StubMailOwnership();

        // Act
        var account = await ownership.ReadStoredEmailAccountAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailAccount.Deployment, account);
    }

    [Fact]
    public async Task ReadStoredEmailAccountAsync_AMessageArrangedToAnotherMailbox_AnswersWithThatMailbox()
    {
        // Arrange
        var ownership = new StubMailOwnership().Owns(Message, SyntheticMailAccount.Another);
        var unarranged = StoredEmailId.Create(Guid.NewGuid());

        // Act
        var account = await ownership.ReadStoredEmailAccountAsync(Message, TestContext.Current.CancellationToken);
        var fallback = await ownership.ReadStoredEmailAccountAsync(unarranged, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailAccount.Another, account);
        Assert.Equal(SyntheticMailAccount.Deployment, fallback);
    }

    /// <summary>The default is the stub's own, so a suite serving a mailbox other than the deployment's states it once.</summary>
    [Fact]
    public async Task ReadStoredEmailAccountAsync_AStatedDefault_IsWhatAnUnarrangedMessageAnswersWith()
    {
        // Arrange
        var ownership = new StubMailOwnership(SyntheticMailAccount.Another);

        // Act
        var account = await ownership.ReadStoredEmailAccountAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailAccount.Another, account);
    }

    [Fact]
    public async Task ReadStoredEmailAccountAsync_ACancelledToken_IsObserved()
    {
        // Arrange
        var ownership = new StubMailOwnership();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ownership.ReadStoredEmailAccountAsync(Message, cancellation.Token));
    }
}
