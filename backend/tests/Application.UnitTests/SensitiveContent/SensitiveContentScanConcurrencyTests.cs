// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent;

/// <summary>The one budget of scans a process runs at once, whichever owner's mail each of them is reading.</summary>
public sealed class SensitiveContentScanConcurrencyTests
{
    /// <summary>A bound of one is what makes the budget observable: the second acquisition waits for the first to release.</summary>
    [Fact]
    public async Task AcquireAsync_APermitAlreadyHeld_WaitsUntilItIsReleased()
    {
        // Arrange
        using var concurrency = new SensitiveContentScanConcurrency(1);
        var held = await concurrency.AcquireAsync(TestContext.Current.CancellationToken);

        // Act
        var waiting = concurrency.AcquireAsync(TestContext.Current.CancellationToken);
        var beforeRelease = waiting.IsCompleted;

        held.Dispose();

        using var granted = await waiting;

        // Assert
        Assert.False(beforeRelease);
        Assert.NotNull(granted);
    }

    /// <summary>
    /// Disposal is callable more than once, and a second release would either add a permit nothing took — quietly
    /// raising the bound this type exists to hold — or throw out of a <c>Dispose</c> that was tidying up after
    /// something else had already gone wrong.
    /// </summary>
    [Fact]
    public async Task Dispose_APermitDisposedTwice_ReleasesOnce()
    {
        // Arrange
        using var concurrency = new SensitiveContentScanConcurrency(1);
        var held = await concurrency.AcquireAsync(TestContext.Current.CancellationToken);

        // Act
        held.Dispose();
        var second = Record.Exception(held.Dispose);

        var granted = await concurrency.AcquireAsync(TestContext.Current.CancellationToken);
        var stillBounded = concurrency.AcquireAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(second);
        Assert.False(stillBounded.IsCompleted);

        granted.Dispose();

        using var served = await stillBounded;
        Assert.NotNull(served);
    }

    /// <summary>A budget of no scans at all is a deployment that scans nothing rather than one that scans without bound.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ABudgetThatIsNotPositive_IsRejected(int maximumConcurrentScans)
    {
        // Act
        var rejected = Record.Exception(() => new SensitiveContentScanConcurrency(maximumConcurrentScans));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(rejected);
    }
}
