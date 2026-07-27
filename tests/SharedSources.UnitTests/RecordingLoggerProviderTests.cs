// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailMcp.SharedSources.UnitTests;

/// <summary>
/// Proves the shared log recorder, because every suite that asserts what a component logged — or kept out of a log —
/// reads its result through this provider rather than through the component under test.
/// </summary>
public sealed class RecordingLoggerProviderTests
{
    private const string Category = "MailMcp.Infrastructure.Resilience";

    [Fact]
    public void Records_WrittenWithAFailure_CaptureCategoryLevelMessageAndFailure()
    {
        // Arrange
        using var provider = new RecordingLoggerProvider();
        var logger = provider.CreateLogger(Category);
        var failure = new InvalidOperationException("the pipeline rejected the call");

        // Act
        logger.Log(LogLevel.Warning, new EventId(1), "attempt 3 failed", failure, (state, _) => state);

        // Assert
        var record = Assert.Single(provider.Records);
        Assert.Equal(Category, record.Category);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("attempt 3 failed", record.Message);
        Assert.Same(failure, record.Failure);
    }

    /// <summary>
    /// A test that asserts a component kept something out of the log depends on this: a recorder that filtered by level
    /// could report an absence the component never earned.
    /// </summary>
    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void IsEnabled_AnyLevel_RecordsWithoutFiltering(LogLevel level)
    {
        // Arrange
        using var provider = new RecordingLoggerProvider();
        var logger = provider.CreateLogger(Category);

        // Act
        var isEnabled = logger.IsEnabled(level);
        logger.Log(level, new EventId(1), "written", null, (state, _) => state);

        // Assert
        Assert.True(isEnabled);
        Assert.Single(provider.Records);
    }

    [Fact]
    public void Records_ReadAroundAFurtherWrite_ReturnsAnIndependentSnapshot()
    {
        // Arrange
        using var provider = new RecordingLoggerProvider();
        var logger = provider.CreateLogger(Category);
        logger.Log(LogLevel.Information, new EventId(1), "first", null, (state, _) => state);

        // Act
        var snapshotAfterFirstWrite = provider.Records;
        logger.Log(LogLevel.Information, new EventId(2), "second", null, (state, _) => state);

        // Assert
        Assert.Single(snapshotAfterFirstWrite);
        Assert.Equal(2, provider.Records.Count);
    }

    /// <summary>
    /// A composed container hands the provider to every category at once, so records arrive from several loggers on
    /// several threads.
    /// </summary>
    [Fact]
    public async Task Records_ConcurrentWritesAcrossCategories_CaptureEveryRecord()
    {
        // Arrange
        const int CategoryCount = 16;
        using var provider = new RecordingLoggerProvider();
        var expectedMessages = Enumerable.Range(0, CategoryCount)
            .Select(index => FormattableString.Invariant($"written by {index}"))
            .ToArray();

        // Act
        await Task.WhenAll(expectedMessages.Select((message, index) => Task.Run(
            () => provider.CreateLogger(FormattableString.Invariant($"{Category}.{index}"))
                .Log(LogLevel.Information, new EventId(index), message, null, (state, _) => state),
            TestContext.Current.CancellationToken)));

        // Assert
        var recordedMessages = provider.Records.Select(record => record.Message).OrderBy(message => message, StringComparer.Ordinal);
        Assert.Equal(expectedMessages.OrderBy(message => message, StringComparer.Ordinal), recordedMessages);
    }
}
