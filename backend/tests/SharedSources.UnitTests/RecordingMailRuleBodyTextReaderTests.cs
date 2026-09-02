// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the reader both mail-rule suites prove their read counts against.</summary>
/// <remarks>
/// The count and the token are what the suites assert through, so a fault in either would report a rule set resolving
/// nothing, or resolving a fact twice, as a passing test in whichever suite happened to use it.
/// </remarks>
public sealed class RecordingMailRuleBodyTextReaderTests
{
    [Fact]
    public void ReadCount_ReaderThatWasNeverAsked_HasCountedNothing()
    {
        // Arrange, Act
        var reader = new RecordingMailRuleBodyTextReader("an extracted body");

        // Assert
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task ReadBodyTextAsync_ConfiguredText_IsAnsweredAndCountedOncePerRead()
    {
        // Arrange
        var reader = new RecordingMailRuleBodyTextReader("an extracted body");

        // Act
        var first = await reader.ReadBodyTextAsync(TestContext.Current.CancellationToken);
        var second = await reader.ReadBodyTextAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("an extracted body", first);
        Assert.Equal("an extracted body", second);
        Assert.Equal(2, reader.ReadCount);
    }

    /// <summary>A message with no extracted text is a state a rule has to answer about, so the reader models it.</summary>
    [Fact]
    public async Task ReadBodyTextAsync_ReaderConfiguredWithNothing_AnswersWithNoText()
    {
        // Arrange
        var reader = new RecordingMailRuleBodyTextReader();

        // Act
        var text = await reader.ReadBodyTextAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(text);
        Assert.Equal(1, reader.ReadCount);
    }

    [Fact]
    public async Task ReadBodyTextAsync_CancelledToken_RaisesBeforeTheReadIsCounted()
    {
        // Arrange
        var reader = new RecordingMailRuleBodyTextReader("an extracted body");
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadBodyTextAsync(cancellation.Token));
        Assert.Equal(0, reader.ReadCount);
    }
}
