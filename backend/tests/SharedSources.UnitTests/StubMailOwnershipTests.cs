// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
    public async Task ReadStoredEmailUserAsync_NothingArranged_AnswersWithTheDefaultUser()
    {
        // Arrange
        var ownership = new StubMailOwnership();

        // Act
        var user = await ownership.ReadStoredEmailUserAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, user);
    }

    [Fact]
    public async Task ReadStoredEmailUserAsync_AMessageArrangedToSomebodyElse_AnswersWithThatUser()
    {
        // Arrange
        var ownership = new StubMailOwnership().Owns(Message, SyntheticMailUser.Another);
        var unarranged = StoredEmailId.Create(Guid.NewGuid());

        // Act
        var user = await ownership.ReadStoredEmailUserAsync(Message, TestContext.Current.CancellationToken);
        var fallback = await ownership.ReadStoredEmailUserAsync(unarranged, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Another, user);
        Assert.Equal(SyntheticMailUser.Deployment, fallback);
    }

    /// <summary>The default is the stub's own, so a suite serving somebody other than the deployment states it once.</summary>
    [Fact]
    public async Task ReadStoredEmailUserAsync_AStatedDefault_IsWhatAnUnarrangedMessageAnswersWith()
    {
        // Arrange
        var ownership = new StubMailOwnership(SyntheticMailUser.Another);

        // Act
        var user = await ownership.ReadStoredEmailUserAsync(Message, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Another, user);
    }

    [Fact]
    public async Task ReadStoredEmailUserAsync_ACancelledToken_IsObserved()
    {
        // Arrange
        var ownership = new StubMailOwnership();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ownership.ReadStoredEmailUserAsync(Message, cancellation.Token));
    }
}
