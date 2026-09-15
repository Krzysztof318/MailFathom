// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders.Local;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Folders;

public sealed class LoggedLocalMailFolderChangeAuditorTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A folder name is text a person typed, so the record names the folder by its identity and carries nothing else
    /// about it. Who changed it is named beside the mailbox rather than derived from it: a mailbox several people are
    /// assigned is changed by one of them, and a record naming only the mailbox could not say which.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AnErasure_RecordsIdentitiesAndTheKindAndNoFolderName()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var auditor = CreateAuditor(logs);
        var folder = LocalMailFolderId.Create(Guid.CreateVersion7(OccurredAt));
        var change = new LocalMailFolderChange(
            MailAccountId.Create("primary"),
            SyntheticMailUser.Deployment,
            folder,
            MailFolderChangeKind.Erased,
            ErasedFolderCount: 3,
            OccurredAt);

        // Act
        await auditor.RecordAsync(change, TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);

        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(
            ["AccountId", "ChangeKind", "ChangedBy", "ErasedFolderCount", "FolderId", "OccurredAt"],
            record.Properties.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(folder.Value, record.Properties["FolderId"]);
        Assert.Equal("primary", record.Properties["AccountId"]);
        Assert.Equal(SyntheticMailUser.Deployment.Value, record.Properties["ChangedBy"]);
        Assert.Equal(MailFolderChangeKind.Erased, record.Properties["ChangeKind"]);
        Assert.Equal(3, record.Properties["ErasedFolderCount"]);
    }

    private static LoggedLocalMailFolderChangeAuditor CreateAuditor(RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));

        return new LoggedLocalMailFolderChangeAuditor(loggerFactory.CreateLogger<LoggedLocalMailFolderChangeAuditor>());
    }
}
