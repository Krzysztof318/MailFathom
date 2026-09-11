// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.BodyCleanup;
using MailFathom.Application.EmailContent.Cleaning;
using Xunit;

namespace MailFathom.AI.UnitTests.BodyCleanup;

/// <summary>Covers what a deployment that has not turned the third rendering on proposes, which is nothing at all.</summary>
/// <remarks>
/// The absence is a renderable reason rather than a failure, which is why the registration exists: a missing service would
/// have left the route deciding for itself what an absence meant.
/// </remarks>
public sealed class InactiveMailBodyCleanerTests
{
    /// <summary>What the pass reads before it composes an outline, so a deployment cleaning nothing describes nothing.</summary>
    [Fact]
    public void IsActive_ADeploymentThatHasNotTurnedItOn_SaysSoWithoutBeingAskedAboutAMessage()
    {
        // Act and assert
        Assert.False(InactiveMailBodyCleaner.Instance.IsActive);
    }

    [Fact]
    public async Task ProposeAsync_ADeploymentThatHasNotTurnedItOn_WithholdsRatherThanFailing()
    {
        // Act
        var proposal = await InactiveMailBodyCleaner.Instance.ProposeAsync(
            Outline(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningWithholding.NotActivated, proposal.Withholding);
        Assert.Empty(proposal.Segments);
    }

    [Fact]
    public async Task ProposeAsync_ACancelledCaller_StopsRatherThanAnswering()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act and assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InactiveMailBodyCleaner.Instance.ProposeAsync(Outline(), cancellation.Token));
    }

    private static CleanableMailBody Outline() => new(
        "Your receipt",
        "The Shop",
        [new CleanableMailBlock(0, "paragraph", LinkCount: 0, "Your code is 558132")]);
}
