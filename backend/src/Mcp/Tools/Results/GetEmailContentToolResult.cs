// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.GetEmailContent;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes what one <c>get_email_content</c> call produced for every email it named.</summary>
/// <remarks>
/// <para>
/// The record is the tool's structured output, so its shape is the advertised output schema and its descriptions travel
/// with it. It is the use case's result republished rather than narrowed a second time here, which is what keeps the
/// privacy rules in one place: nothing reachable from it can carry attachment bytes or raw MIME, because the types it is
/// built from have nowhere to put them.
/// </para>
/// <para>
/// This is the most sensitive result MailFathom publishes. Nothing in it may be logged, and every part of it inherits the
/// classification, retention, access, and erasure constraints of the mail it was read from.
/// </para>
/// </remarks>
[Description("One entry per email the call named, in the order it named them. Each entry carries either the email's content or the reason there is none, so one email this deployment cannot serve does not discard the others.")]
internal sealed record GetEmailContentToolResult
{
    /// <summary>Gets one entry per named email, in the order the call named them.</summary>
    [Description("One entry per email the call named, in the same order. An email named once appears once: the call is refused rather than served twice when an identifier is repeated. A call that named a thread is answered with that thread's messages in the thread's own order instead.")]
    public required IReadOnlyList<RetrievedEmail> Emails { get; init; }

    /// <summary>Gets the messages of a named conversation this call did not carry, in the conversation's order.</summary>
    [Description("For a call that named a thread longer than one read serves: the storedEmailIds of that thread's remaining messages, in the thread's own order. Ask for them directly in a second call. Empty for every call that named its emails itself.")]
    public required IReadOnlyList<string> UnreadThreadMessages { get; init; }

    /// <summary>Publishes what a read returned.</summary>
    /// <param name="result">The result to publish.</param>
    /// <returns>The wire representation of <paramref name="result" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    public static GetEmailContentToolResult From(GetEmailContentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new GetEmailContentToolResult
        {
            Emails = [.. result.Emails.Select(RetrievedEmail.From)],
            UnreadThreadMessages = [.. result.UnreadThreadEmails.Select(storedEmailId => storedEmailId.ToString())],
        };
    }
}
