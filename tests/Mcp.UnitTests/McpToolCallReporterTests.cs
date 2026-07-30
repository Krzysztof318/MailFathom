// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Accounts;
using MailMcp.Application.Persistence;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Failures;
using MailMcp.Mcp.Observability;
using MailMcp.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using Xunit;

namespace MailMcp.Mcp.UnitTests;

/// <summary>Covers what the boundary tells a client, and an operator, about a tool call that failed.</summary>
/// <remarks>
/// The two obligations are opposites of each other, which is why they are tested together: everything an undiagnosed
/// failure knows has to reach the log, and none of it may reach the client.
/// </remarks>
public sealed class McpToolCallReporterTests
{
    private const string ToolName = "list_emails";

    /// <summary>A leaked exception type, message, or stack trace is how a boundary starts describing its internals.</summary>
    [Fact]
    public async Task ReportAsync_UndiagnosedFailure_ReportsTheGenericCodeAndNoDetail()
    {
        // Arrange
        const string LeakedDetail = "Npgsql connection to mail-db-7 refused for user mailmcp";
        using var logs = new RecordingLoggerProvider();
        var reporter = ReporterOver(logs, out _);

        // Act
        var result = await reporter.ReportAsync(
            (_, _) => throw new InvalidOperationException(LeakedDetail),
            CallContext(),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        var reportedText = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith($"MailMcp error {MailMcpErrorCode.McpToolFailedUnexpectedly}:", reportedText, StringComparison.Ordinal);
        Assert.DoesNotContain(LeakedDetail, reportedText, StringComparison.Ordinal);
        Assert.DoesNotContain("mail-db-7", reportedText, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), reportedText, StringComparison.Ordinal);
    }

    /// <summary>What the client is not told has to be recoverable from the server, or the failure is simply lost.</summary>
    [Fact]
    public async Task ReportAsync_UndiagnosedFailure_LogsTheFailureAsAnError()
    {
        // Arrange
        var failure = new InvalidOperationException("connection refused");
        using var logs = new RecordingLoggerProvider();
        var reporter = ReporterOver(logs, out _);

        // Act
        _ = await reporter.ReportAsync((_, _) => throw failure, CallContext(), CancellationToken.None);

        // Assert
        var record = Assert.Single(logs.Records, candidate => candidate.Level is LogLevel.Error);
        Assert.Same(failure, record.Failure);
        Assert.Equal(ToolName, Assert.Contains("ToolName", record.Properties));
    }

    /// <summary>A use-case refusal is already coded and written for a caller, so the boundary publishes it instead of a second wording.</summary>
    [Fact]
    public async Task ReportAsync_RefusalFromTheMcpBoundaryCategory_ReportsItsOwnCodeAndMessage()
    {
        // Arrange
        var refusal = new MailAccountNotAccessibleException(MailAccountId.Create("someone-elses"));
        using var logs = new RecordingLoggerProvider();
        var reporter = ReporterOver(logs, out _);

        // Act
        var result = await reporter.ReportAsync((_, _) => throw refusal, CallContext(), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        var reportedText = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal($"MailMcp error {MailMcpErrorCode.MailAccountNotAccessible}: {refusal.Message}", reportedText);
        Assert.Equal(
            MailMcpErrorCode.MailAccountNotAccessible.Value,
            Assert.Contains("ErrorCode", Assert.Single(logs.Records).Properties));
    }

    /// <summary>A failure from any other category describes MailMcp's own internals, so its message stays on the server.</summary>
    [Fact]
    public async Task ReportAsync_FailureFromAnotherCategory_CollapsesIntoTheGenericCode()
    {
        // Arrange
        const string OperatorOnlyDetail = "The stored email row was modified by a competing run.";
        using var logs = new RecordingLoggerProvider();
        var reporter = ReporterOver(logs, out _);

        // Act
        var result = await reporter.ReportAsync(
            (_, _) => throw new PersistenceConcurrencyConflictException(OperatorOnlyDetail),
            CallContext(),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        var reportedText = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith($"MailMcp error {MailMcpErrorCode.McpToolFailedUnexpectedly}:", reportedText, StringComparison.Ordinal);
        Assert.DoesNotContain(OperatorOnlyDetail, reportedText, StringComparison.Ordinal);
        Assert.Contains(logs.Records, record => record.Level is LogLevel.Error);
    }

    /// <summary>An SDK message is not written to this repository's rule, so it is collapsed rather than forwarded.</summary>
    [Fact]
    public async Task ReportAsync_SdkFailureCarryingItsOwnMessage_CollapsesIntoTheGenericCode()
    {
        // Arrange
        const string SdkDetail = "Cannot bind 'pageSize' of type System.Int32 from \u0022victim@example.test\u0022.";
        using var logs = new RecordingLoggerProvider();
        var reporter = ReporterOver(logs, out _);

        // Act
        var result = await reporter.ReportAsync(
            (_, _) => throw new McpException(SdkDetail),
            CallContext(),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        var reportedText = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith($"MailMcp error {MailMcpErrorCode.McpToolFailedUnexpectedly}:", reportedText, StringComparison.Ordinal);
        Assert.DoesNotContain("victim@example.test", reportedText, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Int32", reportedText, StringComparison.Ordinal);
    }

    /// <summary>A protocol failure still belongs in the audit trail, even though the transport must report it as one.</summary>
    [Fact]
    public async Task ReportAsync_ProtocolFailure_RecordsTheOutcomeBeforeRethrowing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var reporter = ReporterOver(logs, out _);

        // Act
        _ = await Assert.ThrowsAsync<McpProtocolException>(
            async () => await reporter.ReportAsync(
                (_, _) => throw new McpProtocolException("unknown tool", McpErrorCode.InvalidParams),
                CallContext(),
                CancellationToken.None));

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal((int)McpErrorCode.InvalidParams, Assert.Contains("JsonRpcErrorCode", record.Properties));
        Assert.Equal(ToolName, Assert.Contains("ToolName", record.Properties));
    }

    /// <summary>An unknown tool name is unvalidated caller input on its way into a retained log.</summary>
    [Theory]
    [InlineData("victim@example.test\nINJECTED admin login")]
    [InlineData("List_Emails")]
    [InlineData("")]
    public async Task ReportAsync_ToolNameOutsideTheShapeMailMcpUses_RecordsAPlaceholderInstead(string requestedName)
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var reporter = ReporterOver(logs, out _);

        // Act
        _ = await reporter.ReportAsync(
            (_, _) => throw new InvalidOperationException("no such tool"),
            CallContext(requestedName),
            CancellationToken.None);

        // Assert
        var recordedName = Assert.Contains("ToolName", Assert.Single(logs.Records).Properties);
        Assert.Equal("(unrecognized)", recordedName);
    }

    /// <summary>A cancelled call is the caller's own doing and must not be converted into a tool error.</summary>
    [Fact]
    public async Task ReportAsync_CancelledCall_RethrowsAndRecordsTheCancellation()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var reporter = ReporterOver(logs, out _);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await reporter.ReportAsync(
                (_, token) => throw new OperationCanceledException(token),
                CallContext(),
                cancellation.Token));

        Assert.Contains(logs.Records, record => record.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReportAsync_SuccessfulCall_ReturnsTheResultAndRecordsHowLongItTook()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var reporter = ReporterOver(logs, out var timeProvider);
        var toolResult = new CallToolResult { Content = [new TextContentBlock { Text = "{}" }] };

        // Act
        var result = await reporter.ReportAsync(
            (_, _) =>
            {
                timeProvider.Advance(TimeSpan.FromMilliseconds(1500));

                return ValueTask.FromResult(toolResult);
            },
            CallContext(),
            CancellationToken.None);

        // Assert
        Assert.Same(toolResult, result);
        var record = Assert.Single(logs.Records);
        Assert.Equal(1500L, Assert.Contains("DurationMilliseconds", record.Properties));
        Assert.Equal(false, Assert.Contains("IsError", record.Properties));
    }

    /// <summary>A tool that answered with an error is still an answer, so the outcome is recorded rather than replaced.</summary>
    [Fact]
    public async Task ReportAsync_ToolAnsweredWithAnError_RecordsTheOutcomeAndReturnsItUnchanged()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var reporter = ReporterOver(logs, out _);
        var toolResult = new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "MailMcp error 51001: bad argument." }] };

        // Act
        var result = await reporter.ReportAsync((_, _) => ValueTask.FromResult(toolResult), CallContext(), CancellationToken.None);

        // Assert
        Assert.Same(toolResult, result);
        Assert.Equal(true, Assert.Contains("IsError", Assert.Single(logs.Records).Properties));
    }

    private static McpToolCallReporter ReporterOver(RecordingLoggerProvider logs, out FakeTimeProvider timeProvider)
    {
        timeProvider = new FakeTimeProvider();

        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new McpToolCallReporter(timeProvider, loggerFactory.CreateLogger<McpToolCallReporter>());
    }

    private static RequestContext<CallToolRequestParams> CallContext(string requestedName = ToolName) =>
        new(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams { Name = requestedName });
}
