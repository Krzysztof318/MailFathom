// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Domain.Failures;

namespace MailFathom.Mcp.Failures;

/// <summary>Writes the one error text every failed MailFathom tool call is reported through.</summary>
/// <remarks>
/// <para>
/// The shape is defined once here and used for every failure the surface publishes, so a client parses one format and an
/// operator reads one format. The text opens with the stable five-digit <see cref="MailFathomErrorCode" />, the machine-readable part
/// and the only part a runbook, an alert, or a support conversation should match on; the sentence after it is written
/// for whoever, or whatever, reads the message.
/// </para>
/// <para>
/// <b>Nothing else may enter the text.</b> No exception type, no stack trace, no inner-exception detail, no provider
/// payload, no internal identifier, no filter value, no mailbox address, and no message content. What the boundary
/// withholds is not lost: an unexpected failure is logged in full on the server, correlated by the trace the request
/// already carries.
/// </para>
/// </remarks>
internal static class McpToolFailure
{
    /// <summary>The error-code category whose failures are written to be read by a client.</summary>
    /// <remarks>
    /// Category 5 is the MCP boundary, and every code allocated in it names a failure a caller caused and can act on: a
    /// filter outside what the query accepts, a page size outside its range, an account this deployment does not serve, a
    /// cursor that does not continue this walk. Those messages are written for that audience and state a limit or a
    /// filter name rather than a value.
    /// </remarks>
    public const int ClientReadableCategory = 5;

    /// <summary>Reports whether a failure may be described to a client rather than collapsed into the generic code.</summary>
    /// <param name="failure">The failure a use case raised.</param>
    /// <returns><see langword="true" /> when the failure's code belongs to the category written for a client; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The category is the whole rule, deliberately, rather than a list of exception types this boundary would have to be
    /// edited to extend. A failure from any other category — a schema mismatch, an IMAP authentication refusal, a
    /// concurrency conflict — describes MailFathom's own internals to whoever asked, so it collapses into
    /// <see cref="MailFathomErrorCode.McpToolFailedUnexpectedly" /> and stays in the server log.
    /// </remarks>
    public static bool CanDescribeToClient(MailFathomException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.ErrorCode.IsSpecified && failure.ErrorCode.Category is ClientReadableCategory;
    }

    /// <summary>Describes a failure a use case raised, reusing the message it wrote.</summary>
    /// <param name="failure">The failure, whose code must belong to <see cref="ClientReadableCategory" />.</param>
    /// <returns>The failure text, opening with the five-digit code.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the failure's code is not one this boundary may describe.</exception>
    /// <remarks>
    /// The use case's own message is published rather than a second one written here. It already names the filter and the
    /// limit without naming the value, so restating it at this boundary would produce two texts for one failure that
    /// could drift apart, and the one a client reads would be the one no test of the use case covers.
    /// </remarks>
    public static string Describe(MailFathomException failure)
    {
        if (!CanDescribeToClient(failure))
        {
            throw new ArgumentException(
                "Only a failure whose error code belongs to the MCP boundary category may be described to a client.",
                nameof(failure));
        }

        return Describe(failure.ErrorCode, failure.Message);
    }

    /// <summary>Describes a failure as the text a tool result carries.</summary>
    /// <param name="errorCode">The stable code identifying the failure.</param>
    /// <param name="clientSafeMessage">A sentence free of exception detail, identifiers, filter values, addresses, and message content.</param>
    /// <returns>The failure text, opening with the five-digit code.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="clientSafeMessage" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="errorCode" /> is the unspecified struct default, which names no failure a client could act on.</exception>
    public static string Describe(MailFathomErrorCode errorCode, string clientSafeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSafeMessage);

        if (!errorCode.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(errorCode),
                "A tool failure must be reported through an allocated error code.");
        }

        return $"MailFathom error {errorCode}: {clientSafeMessage}";
    }
}
