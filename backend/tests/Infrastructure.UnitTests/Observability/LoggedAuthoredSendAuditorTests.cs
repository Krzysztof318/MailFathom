// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

public sealed class LoggedAuthoredSendAuditorTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly OutgoingEmailId Record =
        OutgoingEmailId.Create(new Guid("2f9d5f52-5d4c-4a1e-9d0b-2f1a3c4d5e6f"));

    /// <summary>An owner asking who sent something is answered by the record, so it names the caller and its grant.</summary>
    [Fact]
    public async Task RecordAuthoredSendAsync_OrdinarySend_RecordsWhoAskedAndUnderWhichGrant()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var auditor = CreateAuditor(logs);

        // Act
        await auditor.RecordAuthoredSendAsync(
            Send(unvouchedRecipientCount: 0),
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("agent-key", record.Properties["Caller"]);
        Assert.Equal(MailFathomPermission.MailSend.Name, record.Properties["Grant"]);
        Assert.Equal(AuthoredSendAct.Reply, record.Properties["AuthoredSendAct"]);
        Assert.Equal("work", record.Properties["AccountId"]);
        Assert.Equal(Record.Value, record.Properties["OutgoingEmailId"]);
        Assert.Equal(3, record.Properties["RecipientCount"]);
        Assert.Equal(OccurredAt, record.Properties["OccurredAt"]);
    }

    /// <summary>A send reaching somebody nobody here vouches for is the line an owner looks for, so it stands out.</summary>
    [Fact]
    public async Task RecordAuthoredSendAsync_SendReachingSomebodyNobodyVouchesFor_RecordsTheCountAsAWarning()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var auditor = CreateAuditor(logs);

        // Act
        await auditor.RecordAuthoredSendAsync(
            Send(unvouchedRecipientCount: 2),
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(2, record.Properties["UnvouchedRecipientCount"]);
    }

    /// <summary>The record is evidence about a send rather than a copy of it, so nothing of the message reaches the log.</summary>
    [Fact]
    public async Task RecordAuthoredSendAsync_AnySend_RecordsNothingOfTheMessage()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var auditor = CreateAuditor(logs);

        // Act
        await auditor.RecordAuthoredSendAsync(
            Send(unvouchedRecipientCount: 1),
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.All(
            record.Properties.Values,
            value => Assert.DoesNotContain("@", value?.ToString() ?? string.Empty, StringComparison.Ordinal));
        Assert.Equal(
            [
                "AccountId",
                "AuthoredSendAct",
                "Caller",
                "Grant",
                "OccurredAt",
                "OutgoingEmailId",
                "RecipientCount",
                "UnvouchedRecipientCount",
            ],
            [.. record.Properties.Keys.Order(StringComparer.Ordinal)]);
    }

    private static AuthoredSend Send(int unvouchedRecipientCount) => new(
        "agent-key",
        MailFathomPermission.MailSend,
        AuthoredSendAct.Reply,
        MailAccountId.Create("work"),
        Record,
        RecipientCount: 3,
        unvouchedRecipientCount,
        OccurredAt);

    private static LoggedAuthoredSendAuditor CreateAuditor(RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));

        return new LoggedAuthoredSendAuditor(loggerFactory.CreateLogger<LoggedAuthoredSendAuditor>());
    }
}
