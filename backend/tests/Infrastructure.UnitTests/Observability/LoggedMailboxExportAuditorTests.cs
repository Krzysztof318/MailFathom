// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Export;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Exports;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

public sealed class LoggedMailboxExportAuditorTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailboxExportId Export =
        MailboxExportId.Create(new Guid("0199a0c0-0000-7000-8000-000000000001"));

    /// <summary>A mailbox owner asking who took a copy of their mail is answered by the caller and the grant.</summary>
    [Fact]
    public async Task RecordAsync_AnExportBeingDownloaded_RecordsWhoAskedAndUnderWhichGrant()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var auditor = CreateAuditor(logs);

        // Act
        await auditor.RecordAsync(
            Act(MailboxExportActKind.Downloaded, folderPath: null, export: Export),
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("operator-key", record.Properties["Caller"]);
        Assert.Equal(MailFathomPermission.AdminExport.Name, record.Properties["Grant"]);
        Assert.Equal(MailboxExportActKind.Downloaded, record.Properties["MailboxExportActKind"]);
        Assert.Equal("work", record.Properties["AccountId"]);
        Assert.Equal(Export.Value, record.Properties["ExportId"]);
        Assert.Equal(4L, record.Properties["MessageCount"]);
        Assert.Equal(40960L, record.Properties["ByteCount"]);
        Assert.Equal(OccurredAt, record.Properties["OccurredAt"]);
    }

    /// <summary>A measurement exists before any export does, so the record carries no identity for one.</summary>
    [Fact]
    public async Task RecordAsync_AMeasurement_RecordsNoExportIdentity()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var auditor = CreateAuditor(logs);

        // Act
        await auditor.RecordAsync(
            Act(MailboxExportActKind.Measured, folderPath: null, export: null),
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Null(record.Properties["ExportId"]);
        Assert.Equal(true, record.Properties["WholeMailbox"]);
    }

    /// <summary>
    /// The folder path is the one value a person chose, so the log says whether the whole mailbox was covered instead of
    /// writing the name back out. A folder is named after what it holds, which is why it is treated as mail rather than
    /// as one of MailFathom's own names for things.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AnExportOfOneFolder_RecordsThatItWasNotTheWholeMailboxAndNotWhichFolder()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var auditor = CreateAuditor(logs);

        // Act
        await auditor.RecordAsync(
            Act(MailboxExportActKind.Started, folderPath: "Clients/Weiss v. Braun", export: Export),
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(false, record.Properties["WholeMailbox"]);
        Assert.All(
            record.Properties.Values,
            value => Assert.DoesNotContain("Weiss", value?.ToString() ?? string.Empty, StringComparison.Ordinal));
        Assert.Equal(
            [
                "AccountId",
                "ByteCount",
                "Caller",
                "ExportId",
                "Grant",
                "MailboxExportActKind",
                "MessageCount",
                "OccurredAt",
                "WholeMailbox",
            ],
            [.. record.Properties.Keys.Order(StringComparer.Ordinal)]);
    }

    private static MailboxExportAct Act(
        MailboxExportActKind kind,
        string? folderPath,
        MailboxExportId? export) => new(
        "operator-key",
        MailFathomPermission.AdminExport,
        kind,
        MailAccountId.Create("work"),
        folderPath,
        export,
        MessageCount: 4,
        ByteCount: 40960,
        OccurredAt);

    private static LoggedMailboxExportAuditor CreateAuditor(RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));

        return new LoggedMailboxExportAuditor(loggerFactory.CreateLogger<LoggedMailboxExportAuditor>());
    }
}
