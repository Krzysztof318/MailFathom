// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>
/// Proves the shared logging-pipeline double, because a suite that asserts a type released the pipeline it was handed
/// reads that fact from here rather than from the type under test.
/// </summary>
public sealed class RecordingLoggerFactoryTests
{
    private const string Category = "MailFathom.Host.Startup";

    [Fact]
    public void CreateLogger_Always_RemembersTheCategoryTheOwnerAskedFor()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();

        // Act
        loggerFactory.CreateLogger(Category);

        // Assert
        Assert.Equal(Category, loggerFactory.CategoryName);
    }

    [Fact]
    public void Records_WrittenThroughTheCreatedLogger_ReachTheDelegatedProvider()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var logger = loggerFactory.CreateLogger(Category);
        var state = new List<KeyValuePair<string, object?>>
        {
            KeyValuePair.Create("ServiceName", (object?)"mailfathom-host"),
            KeyValuePair.Create("{OriginalFormat}", (object?)"Host {ServiceName} ended."),
        };

        // Act
        logger.Log(LogLevel.Critical, new EventId(1), state, null, (_, _) => "Host mailfathom-host ended.");

        // Assert
        var record = Assert.Single(loggerFactory.Records);
        Assert.Equal(Category, record.Category);
        Assert.Equal(LogLevel.Critical, record.Level);
        Assert.Equal(
            [KeyValuePair.Create("ServiceName", (object?)"mailfathom-host")],
            record.Properties);
    }

    [Fact]
    public void DisposeCount_PipelineNotYetReleased_IsZero()
    {
        // Arrange, Act
        using var loggerFactory = new RecordingLoggerFactory();

        // Assert
        Assert.Equal(0, loggerFactory.DisposeCount);
    }

    [Fact]
    public void DisposeCount_ReleasedRepeatedly_CountsEveryRelease()
    {
        // Arrange
        var loggerFactory = new RecordingLoggerFactory();

        // Act
        loggerFactory.Dispose();
        loggerFactory.Dispose();

        // Assert
        Assert.Equal(2, loggerFactory.DisposeCount);
    }

    /// <summary>
    /// An owner disposes the pipeline before the test asserts on it, so the records have to outlive the release that
    /// the same test is checking for.
    /// </summary>
    [Fact]
    public void Records_ReadAfterRelease_StillReportWhatWasWritten()
    {
        // Arrange
        var loggerFactory = new RecordingLoggerFactory();
        var logger = loggerFactory.CreateLogger(Category);
        logger.Log(LogLevel.Information, new EventId(1), "written before release", null, (state, _) => state);

        // Act
        loggerFactory.Dispose();

        // Assert
        Assert.Single(loggerFactory.Records);
    }
}
