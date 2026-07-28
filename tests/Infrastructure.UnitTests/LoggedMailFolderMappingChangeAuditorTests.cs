// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Folders;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Infrastructure.Folders;
using MailMcp.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class LoggedMailFolderMappingChangeAuditorTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 7, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordMappingChangeAsync_FirstBinding_RecordsTheBoundPathAtInformation()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var auditor = CreateAuditor(logs);
        var change = new MailFolderMappingChange(
            MailAccountId.Create("primary"),
            MailFolderAlias.Create("inbox"),
            PreviousRemotePath: null,
            RemoteFolderPath.Create("INBOX", '/'),
            MailFolderResolutionGeneration.First,
            OccurredAt);

        // Act
        await auditor.RecordMappingChangeAsync(change, TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("INBOX", record.Properties["NewRemotePath"]);
        Assert.Equal(1, record.Properties["ResolutionGeneration"]);
        Assert.DoesNotContain("PreviousRemotePath", record.Properties.Keys, StringComparer.Ordinal);
    }

    /// <summary>A folder that resynchronizes from the beginning is unexplained without both paths, so the audit record carries them.</summary>
    [Fact]
    public async Task RecordMappingChangeAsync_AliasRepointed_RecordsBothPathsAndTheNewGenerationAsAWarning()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var auditor = CreateAuditor(logs);
        var change = new MailFolderMappingChange(
            MailAccountId.Create("primary"),
            MailFolderAlias.Create("archive"),
            RemoteFolderPath.Create("Archief", '/'),
            RemoteFolderPath.Create("Archive/2026", '/'),
            MailFolderResolutionGeneration.Create(2),
            OccurredAt);

        // Act
        await auditor.RecordMappingChangeAsync(change, TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("ARCHIVE", record.Properties["FolderAlias"]);
        Assert.Equal("Archief", record.Properties["PreviousRemotePath"]);
        Assert.Equal("Archive/2026", record.Properties["NewRemotePath"]);
        Assert.Equal(2, record.Properties["ResolutionGeneration"]);
    }

    private static LoggedMailFolderMappingChangeAuditor CreateAuditor(RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));

        return new LoggedMailFolderMappingChangeAuditor(
            loggerFactory.CreateLogger<LoggedMailFolderMappingChangeAuditor>());
    }
}
