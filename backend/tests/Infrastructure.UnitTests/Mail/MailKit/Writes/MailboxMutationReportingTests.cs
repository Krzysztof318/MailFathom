// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapWriteSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Writes;

/// <summary>
/// Proves the rule the whole two-path design rests on: one operation, one name. A fallback that announced a copy and a
/// delete would turn a missing server extension into something an operator has to interpret, and the support question
/// that produces is about mail that was copied and deleted instead of moved — asked about an operation that did exactly
/// what was asked of it.
/// </summary>
public sealed class MailboxMutationReportingTests
{
    private static readonly RemoteFolderPath Archive = RemoteFolderPath.Create(ArchivePath, '/');

    [Fact]
    public async Task RelocateAsync_OnEitherProtocolPath_ReportsTheSameOperationAboveDebug()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();

        // Act
        var nativeReport = await RelocateAndReadTheRecordAsync(resilience, ImapCapabilities.Move | ImapCapabilities.UidPlus);
        var fallbackReport = await RelocateAndReadTheRecordAsync(resilience, ImapCapabilities.UidPlus);

        // Assert
        Assert.Equal(AboveDebug(nativeReport), AboveDebug(fallbackReport));
        Assert.NotEmpty(AboveDebug(nativeReport));
        Assert.All(AboveDebug(fallbackReport), record => Assert.Contains("relocate", record));
    }

    /// <summary>
    /// The debug record still has to be complete, because a genuinely broken fallback is diagnosed from which of the
    /// three commands was reached. Absent from the record above it, present in it.
    /// </summary>
    [Fact]
    public async Task RelocateAsync_OnTheFallbackPath_RecordsEveryCommandItIssuedAtDebug()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();

        // Act
        var report = await RelocateAndReadTheRecordAsync(resilience, ImapCapabilities.UidPlus);

        // Assert
        var debugMessages = report.Where(record => record.Level == LogLevel.Debug)
            .Select(record => record.Message)
            .ToArray();

        Assert.Contains(debugMessages, message => message.Contains("UID COPY", StringComparison.Ordinal));
        Assert.Contains(debugMessages, message => message.Contains("UID STORE +FLAGS (\\Deleted)", StringComparison.Ordinal));
        Assert.Contains(debugMessages, message => message.Contains("UID EXPUNGE", StringComparison.Ordinal));
        Assert.Contains(debugMessages, message => message.Contains("fallback", StringComparison.Ordinal));
    }

    /// <summary>The native path is named at debug too, so the record answers which path ran rather than only when it was odd.</summary>
    [Fact]
    public async Task RelocateAsync_OnTheNativePath_RecordsTheOneCommandItIssuedAtDebug()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();

        // Act
        var report = await RelocateAndReadTheRecordAsync(resilience, ImapCapabilities.Move | ImapCapabilities.UidPlus);

        // Assert
        var debugMessages = report.Where(record => record.Level == LogLevel.Debug)
            .Select(record => record.Message)
            .ToArray();

        Assert.Contains(debugMessages, message => message.Contains("UID MOVE", StringComparison.Ordinal));
        Assert.Contains(debugMessages, message => message.Contains("native", StringComparison.Ordinal));
        Assert.DoesNotContain(debugMessages, message => message.Contains("UID COPY", StringComparison.Ordinal));
    }

    /// <summary>A failure keeps the same identity: a relocation that failed, not a copy or an expunge that failed.</summary>
    [Fact]
    public async Task RelocateAsync_RefusedForWantOfAnExtension_IsStillReportedAsARelocation()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.None };
        await using var harness = CreateHarness(resilience, client, CreateWritableFolder());
        await using var session = await harness.OpenSessionAsync();

        // Act
        await Assert.ThrowsAsync<MailboxMutationUnsupportedException>(
            () => session.RelocateAsync(CreateOccurrenceId(42U), Archive, new RecordingMailboxMutationJournal(), CancellationToken.None));

        // Assert
        var failureRecord = Assert.Single(
            harness.RecordedLogs.Records,
            record => record.Level == LogLevel.Warning);

        Assert.Contains("relocate", failureRecord.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("copy", failureRecord.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("delete", failureRecord.Message, StringComparison.Ordinal);
    }

    /// <summary>Everything an operator reads without turning debug on, in the order it was written.</summary>
    private static IReadOnlyList<string> AboveDebug(IReadOnlyList<RecordingLoggerProvider.LogRecord> report) =>
    [
        .. report.Where(record => record.Level >= LogLevel.Information).Select(record => record.Message),
    ];

    private static async Task<IReadOnlyList<RecordingLoggerProvider.LogRecord>> RelocateAndReadTheRecordAsync(
        OutboundResilienceTestHost resilience,
        ImapCapabilities capabilities)
    {
        var client = new FakeImapClient { Capabilities = capabilities };
        var openFolder = CreateWritableFolder();
        AnswerWithCopyUid(openFolder, sourceUid: 42U, destinationUid: 7U);

        await using var harness = CreateHarness(resilience, client, openFolder);
        await using (var session = await harness.OpenSessionAsync())
        {
            await session.RelocateAsync(CreateOccurrenceId(42U), Archive, new RecordingMailboxMutationJournal(), CancellationToken.None);
        }

        return [.. harness.RecordedLogs.Records];
    }
}
