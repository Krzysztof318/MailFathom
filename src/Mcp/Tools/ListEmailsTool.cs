// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.ListEmails;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools;

/// <summary>Publishes the <c>list_emails</c> tool over the <see cref="MailboxTimelineReader" /> use case.</summary>
/// <param name="mailboxTimelineReader">Answers the listing from the local mailbox copy.</param>
/// <param name="accountCatalog">Names the accounts a result publishes, which is the outward half of what the scope arguments do inward.</param>
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
internal sealed class ListEmailsTool(
    MailboxTimelineReader mailboxTimelineReader,
    IMailAccountCatalog accountCatalog)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "list_emails";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>It reads the local mailbox copy, which is what <see cref="MailFathomPermission.MailRead" /> covers. Declaring it beside the name is what keeps <see cref="PublishedTools" /> able to answer for every tool this surface publishes.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailRead;

    /// <summary>The kind of thing this tool is for, which is what a deployment publishes or withholds it by.</summary>
    /// <remarks>It reads the local mailbox copy, which is the retrieval surface a deployment standing an instance up for reading publishes on its own. A category decides what this endpoint offers rather than who may reach it, so it turns nothing on: the tool appears only where the capability behind it is available and the caller's grant reaches it.</remarks>
    public static McpToolCategory Category => McpToolCategory.Mailbox;

    /// <summary>Lists a bounded page of summaries from the local mailbox copy.</summary>
    /// <param name="accounts">The accounts to read, named by identifier or display name, or none to read every account this deployment serves.</param>
    /// <param name="folders">The folders to read, each named by alias or by role, or none to read every folder of those accounts.</param>
    /// <param name="senderAddress">The address the sender must carry.</param>
    /// <param name="recipientAddress">The address a <c>To</c> or <c>Cc</c> recipient must carry.</param>
    /// <param name="subjectFragment">Text the subject must contain.</param>
    /// <param name="receivedOnOrAfter">The inclusive start of the received range.</param>
    /// <param name="receivedBefore">The exclusive end of the received range.</param>
    /// <param name="isRemotelySeen">The remote <c>\Seen</c> state to require.</param>
    /// <param name="isRemotelyFlagged">The remote <c>\Flagged</c> state to require.</param>
    /// <param name="keyword">The keyword an email must carry.</param>
    /// <param name="hasAttachments">Whether attachments are required.</param>
    /// <param name="direction">Which end of the timeline to read from.</param>
    /// <param name="pageSize">How many summaries to return, or none to take the default.</param>
    /// <param name="cursor">The cursor a previous page returned.</param>
    /// <param name="includeJunkMail">Whether the account's junk folder takes part in the listing.</param>
    /// <param name="cancellationToken">Cancels the read when the caller disconnects or the host shuts down.</param>
    /// <returns>The page, with the cursor of the next one and how current each covered folder is.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when text naming an account or a folder alias is not a value this system could have issued.</exception>
    /// <exception cref="MailFathomException">
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
        "Lists summaries of emails already synchronized into MailFathom's local mailbox copy, newest received first by "
        + "default. Filters by account, folder, sender address, recipient address, subject text, received date range, "
        + "remote seen state, remote flagged (starred) state, a keyword the mail server reported, and attachment "
        + "presence. Reads the local copy only: it never contacts a mail server, never "
        + "marks mail as read, and never returns body text, raw MIME, or attachment content. Mail in the account's junk "
        + "folder is left out unless includeJunkMail is set. Returns at most 100 "
        + "summaries per call, with an opaque cursor for the next page and a per-folder statement of how current the "
        + "local copy is.")]
    public async Task<ListEmailsToolResult> ListEmailsAsync(
        [Description("MailFathom accounts to read, each named by its configured account identifier or by the display name it is published under. Omit to read every account this deployment serves; call list_accounts to see what they are. At most 64 may be named, and a name this deployment does not serve is refused rather than answered with an empty page.")]
        string[]? accounts = null,
        [Description("MailFathom folders to read, each named by its alias, such as INBOX, or by the role it plays, written as role:Junk. Roles are Inbox, Archive, Drafts, Sent, Junk, Trash, All, Flagged, Important, and Outbox; naming one reads whichever folder each account in scope maps with that role, whatever it is called there. Omit to read every folder of the accounts in scope. At most 64 may be named. An alias is MailFathom's own name for a folder and is matched without regard to case.")]
        string[]? folders = null,
        [Description("Return only emails sent from this mail address. Matched as a whole address rather than as a fragment, without regard to case; a non-empty value that is not a usable mail address is refused. Omit to match any sender, which an empty string does too.")]
        string? senderAddress = null,
        [Description("Return only emails addressed to this mail address in their To or Cc header. Matched as a whole address rather than as a fragment; Reply-To is not searched. Omit to match any recipient, which an empty string does too.")]
        string? recipientAddress = null,
        [Description("Return only emails whose subject contains this text, without regard to case, up to 256 characters. Wildcard characters match themselves. Omit to match any subject, which an empty string does too.")]
        string? subjectFragment = null,
        [Description("Return only emails received at or after this ISO 8601 timestamp. Emails whose received date is unknown are excluded whenever either bound is named. Omit for no lower bound.")]
        DateTimeOffset? receivedOnOrAfter = null,
        [Description("Return only emails received strictly before this ISO 8601 timestamp, so consecutive ranges built from one instant neither overlap nor leave a gap. Omit for no upper bound.")]
        DateTimeOffset? receivedBefore = null,
        [Description("Return only emails the mail server last reported as read (true) or unread (false). Omit to match either. Listing never changes this state. An email whose flags no run has observed yet counts as unread.")]
        bool? isRemotelySeen = null,
        [Description("Return only emails the mail server last reported as flagged (true) or unflagged (false), which is the star most mail clients show. Omit to match either. This is the \\Flagged flag on a message and is unrelated to the Flagged folder role; an email whose flags no run has observed yet counts as unflagged.")]
        bool? isRemotelyFlagged = null,
        [Description("Return only emails carrying this keyword, which is a flag a mail client or server set rather than one of the five standard ones, such as $Junk or a label. Matched as a whole keyword without regard to case; up to 64 characters, and a value that is not a keyword this system stores is refused. Omit to match any, which an empty string does too. The keywords each email carries are reported in its remoteFlags.")]
        string? keyword = null,
        [Description("Return only emails that carry attachments (true) or that carry none (false). Omit to match either. Inline images and cryptographic signature parts do not count as attachments.")]
        bool? hasAttachments = null,
        [Description("Which end of the timeline to read from: newestFirst to browse recent mail, oldestFirst to walk a mailbox in full.")]
        ListEmailsDirection direction = ListEmailsDirection.NewestFirst,
        [Description("How many summaries to return, from 1 to 100. Omit to take the default of 25. A value outside the range is refused rather than clamped.")]
        int? pageSize = null,
        [Description("The nextCursor value from a previous call, to read the following page. Reuse it only with the same filters and direction; presenting it with different ones is refused. Changing only the page size is allowed.")]
        string? cursor = null,
        [Description("Include mail in the account's junk folder, which is left out by default. Naming the junk folder in folderAliases does not include it; only this does. A cursor issued with one answer cannot be presented with the other. The result reports which answer produced it.")]
        bool includeJunkMail = false,
        CancellationToken cancellationToken = default)
    {
        var request = new ListEmailsRequest
        {
            Accounts = MailboxScopeArguments.Accounts(accounts),
            Folders = MailboxScopeArguments.Folders(folders),
            SenderAddress = senderAddress,
            RecipientAddress = recipientAddress,
            SubjectFragment = subjectFragment,
            ReceivedOnOrAfter = receivedOnOrAfter,
            ReceivedBefore = receivedBefore,
            IsRemotelySeen = isRemotelySeen,
            IsRemotelyFlagged = isRemotelyFlagged,
            Keyword = keyword,
            HasAttachments = hasAttachments,
            Direction = DomainDirection(direction),
            PageSize = pageSize,
            Cursor = cursor,
            IncludeJunkMail = includeJunkMail,
        };

        var result = await mailboxTimelineReader.ListEmailsAsync(request, cancellationToken);

        return ListEmailsToolResult.From(result, PublishedAccountNames.From(accountCatalog));
    }

    /// <summary>Reads the domain direction the protocol value names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the protocol value names no direction, which the SDK's schema binding prevents.</exception>
    private static EmailTimelineDirection DomainDirection(ListEmailsDirection direction) => direction switch
    {
        ListEmailsDirection.NewestFirst => EmailTimelineDirection.NewestFirst,
        ListEmailsDirection.OldestFirst => EmailTimelineDirection.OldestFirst,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "The reading direction names no timeline order."),
    };
}
