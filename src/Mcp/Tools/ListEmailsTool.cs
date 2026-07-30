// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Emails;
using MailMcp.Application.Emails.ListEmails;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Failures;
using MailMcp.Domain.Folders;
using ModelContextProtocol.Server;

namespace MailMcp.Mcp.Tools;

/// <summary>Publishes the <c>list_emails</c> tool over the <see cref="MailboxTimelineReader" /> use case.</summary>
/// <param name="mailboxTimelineReader">Answers the listing from the local mailbox copy.</param>
/// <remarks>
/// <para>
/// The tool translates and nothing more. It converts the caller's strings into the domain identities the request is
/// expressed in, calls the use case, and maps the page onto the published contract. Every filter bound, the page-size
/// range, the account authorization, and the cursor's authenticity are the use case's own, checked there so an entrypoint
/// added later cannot bypass them — which is why nothing in this class re-states a limit.
/// </para>
/// <para>
/// It reaches no mail server, because the use case it calls speaks no mail protocol. A protocol request therefore cannot
/// wait on IMAP and cannot set the remote <c>\Seen</c> flag.
/// </para>
/// <para>
/// The arguments are personal data — a filter states who the caller is looking for. Nothing here writes an argument value
/// to a log, and the failures this class raises name the argument rather than what was in it.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class ListEmailsTool(MailboxTimelineReader mailboxTimelineReader)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "list_emails";

    /// <summary>The greatest length an account identifier or folder alias a caller names may carry.</summary>
    /// <remarks>
    /// Generous against every identifier an operator configures and short enough that a refusal cannot become a way to
    /// place a paragraph of caller-chosen text into an error message or a log line.
    /// </remarks>
    private const int MaximumIdentifierLength = 256;

    /// <summary>Lists a bounded page of summaries from the local mailbox copy.</summary>
    /// <param name="accountIds">The accounts to read, or none to read every account this deployment serves.</param>
    /// <param name="folderAliases">The folder aliases to read, or none to read every folder of those accounts.</param>
    /// <param name="senderAddress">The address the sender must carry.</param>
    /// <param name="recipientAddress">The address a <c>To</c> or <c>Cc</c> recipient must carry.</param>
    /// <param name="subjectFragment">Text the subject must contain.</param>
    /// <param name="receivedOnOrAfter">The inclusive start of the received range.</param>
    /// <param name="receivedBefore">The exclusive end of the received range.</param>
    /// <param name="isRemotelySeen">The remote <c>\Seen</c> state to require.</param>
    /// <param name="hasAttachments">Whether attachments are required.</param>
    /// <param name="direction">Which end of the timeline to read from.</param>
    /// <param name="pageSize">How many summaries to return, or none to take the default.</param>
    /// <param name="cursor">The cursor a previous page returned.</param>
    /// <param name="cancellationToken">Cancels the read when the caller disconnects or the host shuts down.</param>
    /// <returns>The page, with the cursor of the next one and how current each covered folder is.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when an account identifier or folder alias is not a value this system could have issued.</exception>
    /// <exception cref="MailMcpException">
    /// Raised by the use case for a filter, page size, account, or cursor it refuses. The call-tool filter turns every one
    /// of them into the coded result a client reads, so this tool neither catches nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "List emails",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Lists summaries of emails already synchronized into MailMcp's local mailbox copy, newest received first by "
        + "default. Filters by account, folder, sender address, recipient address, subject text, received date range, "
        + "remote seen state, and attachment presence. Reads the local copy only: it never contacts a mail server, never "
        + "marks mail as read, and never returns body text, raw MIME, or attachment content. Returns at most 100 "
        + "summaries per call, with an opaque cursor for the next page and a per-folder statement of how current the "
        + "local copy is.")]
    public async Task<ListEmailsToolResult> ListEmailsAsync(
        [Description("Configured MailMcp account identifiers to read. Omit to read every account this deployment serves. At most 64 may be named, and an identifier this deployment does not serve is refused rather than answered with an empty page.")]
        string[]? accountIds = null,
        [Description("MailMcp folder aliases to read, such as INBOX. Omit to read every folder of the accounts in scope. At most 64 may be named. An alias is MailMcp's own name for a folder and is matched without regard to case.")]
        string[]? folderAliases = null,
        [Description("Return only emails sent from this mail address. Matched as a whole address rather than as a fragment, without regard to case; a value that is not a usable mail address is refused. Omit to match any sender.")]
        string? senderAddress = null,
        [Description("Return only emails addressed to this mail address in their To or Cc header. Matched as a whole address rather than as a fragment; Reply-To is not searched. Omit to match any recipient.")]
        string? recipientAddress = null,
        [Description("Return only emails whose subject contains this text, without regard to case, up to 256 characters. Wildcard characters match themselves. Omit to match any subject.")]
        string? subjectFragment = null,
        [Description("Return only emails received at or after this ISO 8601 timestamp. Emails whose received date is unknown are excluded whenever either bound is named. Omit for no lower bound.")]
        DateTimeOffset? receivedOnOrAfter = null,
        [Description("Return only emails received strictly before this ISO 8601 timestamp, so consecutive ranges built from one instant neither overlap nor leave a gap. Omit for no upper bound.")]
        DateTimeOffset? receivedBefore = null,
        [Description("Return only emails the mail server last reported as read (true) or unread (false). Omit to match either. Listing never changes this state. An email whose flags no run has observed yet counts as unread.")]
        bool? isRemotelySeen = null,
        [Description("Return only emails that carry attachments (true) or that carry none (false). Omit to match either. Inline images and cryptographic signature parts do not count as attachments.")]
        bool? hasAttachments = null,
        [Description("Which end of the timeline to read from: newestFirst to browse recent mail, oldestFirst to walk a mailbox in full.")]
        ListEmailsDirection direction = ListEmailsDirection.NewestFirst,
        [Description("How many summaries to return, from 1 to 100. Omit to take the default of 25. A value outside the range is refused rather than clamped.")]
        int? pageSize = null,
        [Description("The nextCursor value from a previous call, to read the following page. Reuse it only with the same filters and direction; presenting it with different ones is refused. Changing only the page size is allowed.")]
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ListEmailsRequest
        {
            AccountIds = ParseAccountIds(accountIds),
            FolderAliases = ParseFolderAliases(folderAliases),
            SenderAddress = senderAddress,
            RecipientAddress = recipientAddress,
            SubjectFragment = subjectFragment,
            ReceivedOnOrAfter = receivedOnOrAfter,
            ReceivedBefore = receivedBefore,
            IsRemotelySeen = isRemotelySeen,
            HasAttachments = hasAttachments,
            Direction = DomainDirection(direction),
            PageSize = pageSize,
            Cursor = cursor,
        };

        var result = await mailboxTimelineReader.ListEmailsAsync(request, cancellationToken);

        return ListEmailsToolResult.From(result);
    }

    /// <summary>Reads the domain direction the protocol value names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the protocol value names no direction, which the SDK's schema binding prevents.</exception>
    private static EmailTimelineDirection DomainDirection(ListEmailsDirection direction) => direction switch
    {
        ListEmailsDirection.NewestFirst => EmailTimelineDirection.NewestFirst,
        ListEmailsDirection.OldestFirst => EmailTimelineDirection.OldestFirst,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "The reading direction names no timeline order."),
    };

    /// <summary>Turns the account identifiers a caller supplied into domain values.</summary>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when more than the accepted number are named, or one of them is not an identifier this system issues.</exception>
    private static IReadOnlyList<MailAccountId> ParseAccountIds(string[]? accountIds) =>
        Parse(accountIds, MailAccountId.Create, MailboxScope.MaximumAccountIds, "accounts");

    /// <summary>Turns the folder aliases a caller supplied into domain values.</summary>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when more than the accepted number are named, or one of them is not an alias this system issues.</exception>
    private static IReadOnlyList<MailFolderAlias> ParseFolderAliases(string[]? folderAliases) =>
        Parse(folderAliases, MailFolderAlias.Create, MailboxScope.MaximumFolderAliases, "folder aliases");

    /// <summary>Converts one list of caller-supplied text into the domain identity it names.</summary>
    /// <remarks>
    /// <para>
    /// The count is checked before anything is converted. The list arrives from a caller nobody vouches for, and a
    /// ceiling applied after the trimming and upper-casing it exists to prevent has already run over every element is not
    /// a ceiling. The limit is the query's own, so the boundary refuses early without inventing a second one.
    /// </para>
    /// <para>
    /// The conversion is refused through the query's own filter failure rather than through a failure this boundary
    /// declares. The code it carries already names a filter the query cannot accept, so a caller reads one code for one
    /// answer, and the refusal reaches the client and the log through the single path every use-case refusal takes.
    /// </para>
    /// <para>
    /// The refused value is deliberately absent from it. An identifier a caller invented is that caller's own input rather
    /// than one of MailMcp's configured names, and echoing input back is how a boundary starts reflecting content.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<TIdentity> Parse<TIdentity>(
        string[]? values,
        Func<string, TIdentity> createIdentity,
        int limit,
        string filterName)
    {
        if (values is null or { Length: 0 })
        {
            return [];
        }

        MailboxQueryFilterInvalidException.ThrowIfCountExceeded(values.Length, limit, filterName);

        try
        {
            return [.. values.Select(value => createIdentity(UsableIdentifierText(value, filterName)))];
        }
        catch (ArgumentException exception)
        {
            throw MailboxQueryFilterInvalidException.NotAUsableIdentifier(filterName, exception);
        }
    }

    /// <summary>Refuses text no identifier of this system is spelled with, before a domain type is asked to read it.</summary>
    /// <remarks>
    /// The domain identities differ in what they reject — a folder alias refuses control characters and an account
    /// identifier does not — while both are equally reachable from an untrusted caller here. The stricter rule is applied
    /// at this boundary for both, because an identifier that is never matched still travels: an account this deployment
    /// does not serve is named back in the refusal a client reads, so an unbounded string carrying newlines would be a
    /// way to write arbitrary text into that contract and into the log beside it.
    /// </remarks>
    private static string UsableIdentifierText(string value, string filterName)
    {
        if (value.Length > MaximumIdentifierLength || value.Any(char.IsControl))
        {
            throw MailboxQueryFilterInvalidException.NotAUsableIdentifier(filterName);
        }

        return value;
    }
}
