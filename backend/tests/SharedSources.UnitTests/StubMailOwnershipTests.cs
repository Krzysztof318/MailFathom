// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the ownership answer every suite bounding two users against each other arranges through.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement. A stub that answered with one user whatever it was asked would
/// make a per-user ceiling test pass while the ceiling bounded nothing, which is exactly the claim those tests exist
/// to make; and one that ignored its default would leave every suite that arranges no user at all refused.
/// </remarks>
public sealed class StubMailOwnershipTests
{
    private static readonly StoredEmailId Message = StoredEmailId.Create(Guid.NewGuid());

    [Fact]
    public async Task ReadStoredEmailAccountAsync_NothingArranged_AnswersWithTheDefaultUser()
    {
        // Arrange
        var ownership = new StubMailOwnership();

        // Act
        var account = await ownership.ReadStoredEmailAccountAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, account.User);
    }

    [Fact]
    public async Task ReadStoredEmailAccountAsync_AMessageArrangedToSomebodyElse_AnswersWithThatUser()
    {
        // Arrange
        var ownership = new StubMailOwnership().Owns(Message, SyntheticMailUser.Another);
        var unarranged = StoredEmailId.Create(Guid.NewGuid());

        // Act
        var account = await ownership.ReadStoredEmailAccountAsync(Message, TestContext.Current.CancellationToken);
        var fallback = await ownership.ReadStoredEmailAccountAsync(unarranged, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Another, account.User);
        Assert.Equal(SyntheticMailUser.Deployment, fallback.User);
    }

    /// <summary>The mailbox is what a posture is read against, so a test that is about one states it.</summary>
    [Fact]
    public async Task ReadStoredEmailAccountAsync_AMessageArrangedToOneMailbox_AnswersWithThatMailbox()
    {
        // Arrange
        var scanned = MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("scanned"));
        var ownership = new StubMailOwnership().Owns(Message, scanned);

        // Act
        var account = await ownership.ReadStoredEmailAccountAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(scanned, account);
    }

    /// <summary>The default is the stub's own, so a suite serving somebody other than the deployment states it once.</summary>
    [Fact]
    public async Task ReadStoredEmailAccountAsync_AStatedDefault_IsWhatAnUnarrangedMessageAnswersWith()
    {
        // Arrange
        var ownership = new StubMailOwnership(SyntheticMailUser.Another);

        // Act
        var account = await ownership.ReadStoredEmailAccountAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Another, account.User);
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
