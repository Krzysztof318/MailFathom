// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools;

/// <summary>Publishes the <c>get_email_content</c> tool over the <see cref="EmailContentReader" /> use case.</summary>
/// <param name="emailContentReader">Reads emails from the local mailbox copy.</param>
/// <remarks>
/// <para>
/// The tool translates and nothing more. It converts the caller's text into the domain identities the request is
/// expressed in, calls the use case, and maps the result onto the published contract. The account authorization, the
/// integrity check on the local copy, the body bounds, the read's character budget, and the repair request a damaged
/// copy produces are the use case's own, checked there so an entrypoint added later cannot bypass them.
/// </para>
/// <para>
/// One thing is checked here that cannot be checked anywhere else: how many identifiers the caller sent, counted before
/// any of them is parsed. The parse scans whatever it is handed, and a caller nobody vouches for decides both how long
/// each identifier is and how many there are.
/// </para>
/// <para>
/// It reaches no mail server, because the use case it calls holds no mailbox port. A protocol request therefore cannot
/// download a message, cannot wait on IMAP, and cannot set the remote <c>\Seen</c> flag — a missing local copy is
/// answered with a stable code and a durable repair request instead of a fetch.
/// </para>
/// <para>
/// This is the most sensitive tool MailFathom publishes, since its result is message content in full — and, for a call
/// that asked for it, a short-lived unauthenticated capability over each attached file. Nothing here writes any part of
/// a result to a log, and the failures it raises name neither the content nor the text the caller supplied.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class GetEmailContentTool(EmailContentReader emailContentReader)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "get_email_content";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>It reads the local mailbox copy, which is what <see cref="MailFathomPermission.MailRead" /> covers. Declaring it beside the name is what keeps <see cref="PublishedTools" /> able to answer for every tool this surface publishes.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailRead;

    /// <summary>The kind of thing this tool is for, which is what a deployment publishes or withholds it by.</summary>
    /// <remarks>It reads the local mailbox copy, which is the retrieval surface a deployment standing an instance up for reading publishes on its own. A category decides what this endpoint offers rather than who may reach it, so it turns nothing on: the tool appears only where the capability behind it is available and the caller's grant reaches it.</remarks>
    public static McpToolCategory Category => McpToolCategory.Mailbox;

    /// <summary>The greatest length text naming a conversation may carry before anything tries to read an identity out of it.</summary>
    /// <remarks>
    /// The longest form <see cref="Guid.TryParse(string, out Guid)" /> accepts is the 68-character hexadecimal one, so
    /// nothing a client could legitimately send is refused by this. What it stops is work proportional to a request:
    /// the parse scans whatever it is handed, and a caller nobody vouches for decides how much that is.
    /// </remarks>
    private const int MaximumIdentifierLength = 68;

    /// <summary>Reads the content of the named emails, or of one named conversation, from the local mailbox copy.</summary>
    /// <param name="storedEmailIds">The stable local identifiers a listing or a search returned for the emails.</param>
    /// <param name="threadId">The conversation to read instead of naming its messages.</param>
    /// <param name="includeSanitizedHtml">Whether to also return the sanitized HTML representation of each body.</param>
    /// <param name="includeAttachmentDownloadLinks">Whether to mint a link for fetching each attachment rather than only describe it.</param>
    /// <param name="cancellationToken">Cancels the read when the caller disconnects or the host shuts down.</param>
    /// <returns>One entry per email read, each carrying its content or the reason there is none.</returns>
    /// <exception cref="EmailContentReadSelectionInvalidException">Thrown when both selections are given, or neither is.</exception>
    /// <exception cref="EmailContentReadCountOutOfRangeException">Thrown when a named list holds no email, or more than the call serves, before any identifier is parsed.</exception>
    /// <exception cref="StoredEmailIdentifierMalformedException">Thrown when an identifier is not one this system issues, before anything is read.</exception>
    /// <exception cref="EmailThreadIdentifierMalformedException">Thrown when the thread identifier is not one this system issues, before anything is read.</exception>
    /// <exception cref="EmailContentReadDuplicateEmailException">Thrown when the same email is named more than once.</exception>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a request it will not serve. The call-tool filter turns every one of them into the
    /// coded result a client reads, so this tool neither catches nor re-describes any. A single email it cannot serve is
    /// not one of them: that is reported inside the result, beside the emails it could.
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
        "Reads up to 10 emails already synchronized into MailFathom's local mailbox copy, in one call: for each one its "
        + "normalized headers, the plain-text body, optionally a sanitized HTML body, and every attachment it carries "
        + "described by file name, media type, and size. Name what to read in exactly one of two ways — storedEmailIds "
        + "for particular emails, or threadId for a whole conversation, which returns its messages in the "
        + "conversation's own order and names any it could not carry in unreadThreadMessages. A call naming both, or "
        + "neither, is refused. Every email returned also carries the conversation it belongs to, with the other "
        + "messages in it named rather than reproduced. Reads the local copy only: it never contacts a mail server, "
        + "never downloads mail, and never marks mail as read. Each email is answered for separately, so one this "
        + "deployment cannot serve does not discard the others. Bodies are bounded per email and by a budget shared "
        + "across the whole call, and a scanned deployment bounds what it analyzes as well; each body says which of "
        + "those bounds cut it in truncatedBy, and only readCharacterBudget is the one that returns more when fewer "
        + "emails are named at once. No response ever carries an attachment's bytes: set includeAttachmentDownloadLinks to receive, for "
        + "each file, a short-lived URL in downloadUrl that fetches it over HTTP with no credential attached, and "
        + "downloadState says why one was not issued when it was not. Where the deployment scans mail for sensitive "
        + "content, what a message's author wrote "
        + "is scanned on every call and returned with each detection replaced by a [redacted:category] marker: the "
        + "marker means material of that kind stood there and was withheld, it is never message text, and asking again "
        + "returns the same marker. Nothing stored is rewritten by it.")]
    public async Task<GetEmailContentToolResult> GetEmailContentAsync(
        [Description("The storedEmailIds a listing or a search returned, at most 10, each named at most once. Each is a UUID and does not change when the mail server renumbers or moves the message. Results come back in the order given, and the call is refused rather than truncated when it names more than 10. Omit it entirely when naming threadId instead.")]
        IReadOnlyList<string>? storedEmailIds = null,
        [Description("The threadId a listing, a search, or an earlier read returned, to read that whole conversation instead of naming its messages. Its messages come back in the conversation's own order, bounded to 10 per call, and unreadThreadMessages names the rest so a second call asks for them directly. Omit it entirely when naming storedEmailIds instead.")]
        string? threadId = null,
        [Description("Whether to also return the sanitized HTML body of each email. Omit it unless the markup itself matters: the plain text is the representation to read from, HTML costs a sanitization pass, and it draws on the same character budget as the plain text. An email carrying no HTML part returns none either way.")]
        bool includeSanitizedHtml = false,
        [Description("Whether to mint a link for fetching each attachment, rather than only describing it. Omitted still returns every attachment's file name, media type, and size, which is what an ordinary read needs to decide whether a file is worth fetching. Each link is a bearer capability: it names one file, it expires within minutes, and anyone holding the URL can fetch that file without a credential — so ask for links only when the files are what you are after, and do not store or log what comes back. The response size is the same either way.")]
        bool includeAttachmentDownloadLinks = false,
        CancellationToken cancellationToken = default)
    {
        // Both are handed over unresolved so the selection is settled before either is counted or parsed: a call naming
        // a conversation and an empty list, or a list and a misspelled conversation, is one that named both, and
        // reporting the list as too short or the conversation as malformed would answer a question nobody asked.
        var request = GetEmailContentRequest.CreateForSelection(
            storedEmailIds is null ? null : () => NamedEmails(storedEmailIds),
            threadId is null ? null : () => NamedThread(threadId),
            includeSanitizedHtml,
            includeAttachmentDownloadLinks);

        var result = await emailContentReader.ReadContentAsync(request, cancellationToken);

        return GetEmailContentToolResult.From(result);
    }

    /// <summary>Reads the conversation identity the caller's text names.</summary>
    /// <remarks>
    /// The same three checks the email identifiers get, and for the same reasons: the length before the parse because a
    /// parse scans what it is handed, the empty UUID refused with everything else because it is what a client sends
    /// when it holds no identifier, and the refused text absent from the failure because it is the caller's own input
    /// on its way into a client-readable result. It is reached only once the selection has settled, so a call that also
    /// named a list is refused as having named both rather than for how it spelled a conversation it will not be read by.
    /// </remarks>
    /// <exception cref="EmailThreadIdentifierMalformedException">Thrown when the text is not an identifier this system issues.</exception>
    private static EmailThreadId NamedThread(string threadId) =>
        threadId is { Length: <= MaximumIdentifierLength }
        && Guid.TryParse(threadId, out var identity)
        && identity != Guid.Empty
            ? EmailThreadId.Create(identity)
            : throw new EmailThreadIdentifierMalformedException();

    /// <summary>Reads the email identities the caller's text names.</summary>
    /// <remarks>
    /// The count is checked before the first identifier is parsed, for the same reason each identifier's length is
    /// checked before that identifier is parsed: a parse scans what it is handed, and the caller decides both how much
    /// of it there is and how many times it is repeated. A list refused after the ninetieth parse would have paid for
    /// eighty-nine of them.
    /// </remarks>
    /// <exception cref="EmailContentReadCountOutOfRangeException">Thrown when no email is named, or more than one call serves.</exception>
    /// <exception cref="StoredEmailIdentifierMalformedException">Thrown when one of the texts is not an identifier this system issues.</exception>
    private static IReadOnlyList<StoredEmailId> NamedEmails(IReadOnlyList<string> storedEmailIds)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);

        if (storedEmailIds.Count is 0 || storedEmailIds.Count > GetEmailContentRequest.MaximumEmails)
        {
            throw new EmailContentReadCountOutOfRangeException(GetEmailContentRequest.MaximumEmails);
        }

        return [.. storedEmailIds.Select(AuthoredMailArguments.AnsweredEmail)];
    }
}
