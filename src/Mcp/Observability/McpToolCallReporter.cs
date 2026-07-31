// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Failures;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Observability;

/// <summary>Records how every tool call ended and keeps undiagnosed failures inside the server.</summary>
/// <param name="timeProvider">Measures how long a call took.</param>
/// <param name="logger">Records the tool name, the outcome, and the duration.</param>
/// <remarks>
/// <para>
/// The reporter wraps every <c>tools/call</c> invocation, so the two obligations it carries are met once for the whole
/// protocol surface instead of per tool. The first is observability: the tool name, the outcome, and the duration are
/// recorded, and never a filter value, a mailbox address, a subject, or any part of a result.
/// </para>
/// <para>
/// The second is that nothing undiagnosed reaches a client, and this is the one place that decides it. A
/// <see cref="MailFathomException" /> whose code belongs to the MCP-boundary category is a refusal a caller caused and can
/// act on, so its own code and message are published; the rule is the category rather than a list of exception types,
/// which is what stops a failure added later from reaching a client because nobody extended a list. Every other
/// exception — a failure from another category, or one thrown before a tool was even reached, such as a dependency that
/// failed to resolve — becomes the single generic code, with the exception logged in full where it stays: on the server,
/// correlated by the trace the request already carries.
/// </para>
/// <para>
/// An <see cref="McpException" /> the SDK raises — while binding an argument to the advertised schema, for instance —
/// carries a message this repository does not own and cannot keep to the rule above, so it collapses like any other
/// exception rather than being forwarded. What a client loses is a description of a request it can already compare
/// against the published input schema; what it cannot gain is a rejected value, a CLR type name, or a serializer detail.
/// </para>
/// <para>
/// Cancellation and protocol-level failures are recorded and then rethrown rather than converted. A cancelled call is the
/// caller's own doing and is not a tool error, and an <see cref="McpProtocolException" /> names a JSON-RPC error the
/// transport must report as one.
/// </para>
/// </remarks>
internal sealed partial class McpToolCallReporter(TimeProvider timeProvider, ILogger<McpToolCallReporter> logger)
{
    /// <summary>Invokes the rest of the tool-call pipeline and reports how it ended.</summary>
    /// <param name="next">The pipeline this reporter wraps.</param>
    /// <param name="request">The call being served.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The tool's result, or a generic error result when the call failed for an undiagnosed reason.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="next" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This is the protocol boundary the specification requires to answer every undiagnosed failure with one generic code rather than with a type a client could read.")]
    public async Task<CallToolResult> ReportAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(request);

        var toolName = RecordableToolName(request.Params?.Name);
        var startedAt = timeProvider.GetTimestamp();

        try
        {
            var result = await next(request, cancellationToken);

            var completedAfterMilliseconds = this.ElapsedMilliseconds(startedAt);
            this.LogCallCompleted(toolName, result.IsError is true, completedAfterMilliseconds);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var cancelledAfterMilliseconds = this.ElapsedMilliseconds(startedAt);
            this.LogCallCanceled(toolName, cancelledAfterMilliseconds);

            throw;
        }
        catch (McpProtocolException protocolFailure)
        {
            // Recorded before it leaves, so a call that ended as a JSON-RPC error still appears in the audit trail with a
            // duration. It is then rethrown rather than answered, because the transport has to report it as the protocol
            // error it is.
            var refusedAfterMilliseconds = this.ElapsedMilliseconds(startedAt);
            this.LogCallFailedTheProtocol(toolName, (int)protocolFailure.ErrorCode, refusedAfterMilliseconds);

            throw;
        }
        catch (MailFathomException expectedFailure) when (McpToolFailure.CanDescribeToClient(expectedFailure))
        {
            // A use case raised this, so the code and the client-safe wording are already decided and no tool has to
            // repeat the mapping. Which failure it was is logged here rather than by the tool, because the tool did not
            // diagnose it.
            var rejectedAfterMilliseconds = this.ElapsedMilliseconds(startedAt);
            this.LogCallReportedAKnownFailure(toolName, expectedFailure.ErrorCode.Value, rejectedAfterMilliseconds);

            return ErrorResult(McpToolFailure.Describe(expectedFailure));
        }
        catch (Exception unexpectedFailure)
        {
            var failedAfterMilliseconds = this.ElapsedMilliseconds(startedAt);
            this.LogCallFailedUnexpectedly(toolName, failedAfterMilliseconds, unexpectedFailure);

            return ErrorResult(
                McpToolFailure.Describe(
                    MailFathomErrorCode.McpToolFailedUnexpectedly,
                    "The request could not be completed. The failure was recorded on the server."));
        }
    }

    /// <summary>Reduces the tool name a caller sent to something safe to keep in a log.</summary>
    /// <remarks>
    /// The name reaches this filter before anything has established that a tool by that name exists, so on an unknown
    /// tool it is unvalidated caller input on its way into retained structured logs. Anything outside the shape a MailFathom
    /// tool name is spelled with is therefore recorded as one fixed placeholder, which keeps arbitrary text, control
    /// characters, and unbounded length out of the log without needing the registry to answer first.
    /// </remarks>
    private static string RecordableToolName(string? requestedName) =>
        requestedName is not null && ToolNameShape().IsMatch(requestedName) ? requestedName : "(unrecognized)";

    [GeneratedRegex("^[a-z0-9_]{1,64}$")]
    private static partial Regex ToolNameShape();

    private static CallToolResult ErrorResult(string failureText) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = failureText }],
    };

    private long ElapsedMilliseconds(long startedAt) =>
        (long)timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {ToolName} tool call ended after {DurationMilliseconds} ms. IsError = {IsError}.")]
    private partial void LogCallCompleted(string toolName, bool isError, long durationMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {ToolName} tool call was cancelled after {DurationMilliseconds} ms.")]
    private partial void LogCallCanceled(string toolName, long durationMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {ToolName} tool call failed the protocol with JSON-RPC error {JsonRpcErrorCode} after {DurationMilliseconds} ms.")]
    private partial void LogCallFailedTheProtocol(string toolName, int jsonRpcErrorCode, long durationMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {ToolName} tool call was refused with error code {ErrorCode} after {DurationMilliseconds} ms.")]
    private partial void LogCallReportedAKnownFailure(string toolName, int errorCode, long durationMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The {ToolName} tool call failed unexpectedly after {DurationMilliseconds} ms and was answered with the generic error code.")]
    private partial void LogCallFailedUnexpectedly(string toolName, long durationMilliseconds, Exception exception);
}
