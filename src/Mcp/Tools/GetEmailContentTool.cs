// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Emails;
using MailMcp.Application.Emails.GetEmailContent;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Failures;
using ModelContextProtocol.Server;

namespace MailMcp.Mcp.Tools;

/// <summary>Publishes the <c>get_email_content</c> tool over the <see cref="EmailContentReader" /> use case.</summary>
/// <param name="emailContentReader">Reads one email from the local mailbox copy.</param>
/// <remarks>
/// <para>
/// The tool translates and nothing more. It converts the caller's text into the domain identity the request is
/// expressed in, calls the use case, and maps the result onto the published contract. The account authorization, the
/// integrity check on the local copy, the body bound, and the repair request a damaged copy produces are the use case's
/// own, checked there so an entrypoint added later cannot bypass them.
/// </para>
/// <para>
/// It reaches no mail server, because the use case it calls holds no mailbox port. A protocol request therefore cannot
/// download a message, cannot wait on IMAP, and cannot set the remote <c>\Seen</c> flag — a missing local copy is
/// answered with a stable code and a durable repair request instead of a fetch.
/// </para>
/// <para>
/// This is the most sensitive tool MailMcp publishes, since its result is message content in full. Nothing here writes
/// any part of a result to a log, and the failures it raises name neither the content nor the text the caller supplied.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class GetEmailContentTool(EmailContentReader emailContentReader)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "get_email_content";

    /// <summary>The greatest length text naming an email may carry before anything tries to read an identity out of it.</summary>
    /// <remarks>
    /// The longest form <see cref="Guid.TryParse(string, out Guid)" /> accepts is the 68-character hexadecimal one, so
    /// nothing a client could legitimately send is refused by this. What it stops is work proportional to a request:
    /// the parse scans whatever it is handed, and a caller nobody vouches for decides how much that is.
    /// </remarks>
    private const int MaximumIdentifierLength = 68;

    /// <summary>Reads one email's content from the local mailbox copy.</summary>
    /// <param name="storedEmailId">The stable local identifier a listing or a search returned for the email.</param>
    /// <param name="includeSanitizedHtml">Whether to also return the sanitized HTML representation of the body.</param>
    /// <param name="cancellationToken">Cancels the read when the caller disconnects or the host shuts down.</param>
    /// <returns>The email's headers, body, attachment metadata, and remote flag snapshot.</returns>
    /// <exception cref="StoredEmailIdentifierMalformedException">Thrown when the identifier is not one this system issues, before anything is read.</exception>
    /// <exception cref="MailMcpException">
    /// Raised by the use case for an email it does not hold or whose local copy it cannot serve. The call-tool filter
    /// turns every one of them into the coded result a client reads, so this tool neither catches nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Get email content",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Reads one email already synchronized into MailMcp's local mailbox copy: its normalized headers, the plain-text "
        + "body, optionally a sanitized HTML body, and a description of each attachment. Reads the local copy only: it "
        + "never contacts a mail server, never downloads mail, and never marks mail as read. Bodies are bounded and say "
        + "so when they were cut, and attachment content is never returned in any form. Name the email by the "
        + "storedEmailId a listing or a search returned.")]
    public async Task<GetEmailContentToolResult> GetEmailContentAsync(
        [Description("The storedEmailId a listing or a search returned for the email. It is a UUID and does not change when the mail server renumbers or moves the message.")]
        string storedEmailId,
        [Description("Whether to also return the sanitized HTML body. Omit it unless the markup itself matters: the plain text is the representation to read from, and HTML costs a sanitization pass. An email carrying no HTML part returns none either way.")]
        bool includeSanitizedHtml = false,
        CancellationToken cancellationToken = default)
    {
        var request = new GetEmailContentRequest(NamedEmail(storedEmailId), includeSanitizedHtml);

        var result = await emailContentReader.ReadContentAsync(request, cancellationToken);

        return GetEmailContentToolResult.From(result);
    }

    /// <summary>Reads the email identity the caller's text names.</summary>
    /// <remarks>
    /// <para>
    /// The conversion happens before the use case is reached, so text that names no email is refused without a lookup.
    /// The empty UUID is refused with everything else rather than looked up: it is the value a client sends when it has
    /// no identifier at all, and no email is ever stored under it.
    /// </para>
    /// <para>
    /// The length is checked before the parse for the reason the listing checks its identifier lists before converting
    /// them: the parse scans what it is handed, and the caller decides how long that is. A ceiling applied afterwards
    /// would have let a request-sized argument be scanned before being refused for not being a UUID.
    /// </para>
    /// <para>
    /// The refused text is deliberately absent from the failure. It is the caller's own input on its way into a
    /// client-readable result and the log line beside it, and an identifier a caller invented says nothing an operator
    /// needs that the code does not already say.
    /// </para>
    /// </remarks>
    /// <exception cref="StoredEmailIdentifierMalformedException">Thrown when the text is not an identifier this system issues.</exception>
    private static StoredEmailId NamedEmail(string storedEmailId) =>
        storedEmailId.Length <= MaximumIdentifierLength
        && Guid.TryParse(storedEmailId, out var identity)
        && identity != Guid.Empty
            ? StoredEmailId.Create(identity)
            : throw new StoredEmailIdentifierMalformedException();
}
