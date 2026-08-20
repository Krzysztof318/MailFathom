// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools;

/// <summary>Publishes the <c>search_emails</c> tool over the <see cref="MailboxSearchReader" /> use case.</summary>
/// <param name="mailboxSearchReader">Answers the search from the local mailbox copy.</param>
/// <param name="snippetBounds">How much of a message's body this deployment lets one result show.</param>
/// <param name="accountCatalog">Names the accounts a result publishes, which is the outward half of what the scope arguments do inward.</param>
/// <remarks>
/// <para>
/// The tool translates and nothing more. It converts the caller's strings into the domain identities the request is
/// expressed in, calls the use case, and maps the window onto the published contract. The query text's bound, every
/// filter bound, the result-count range, and the account authorization are the use case's own, checked there so an
/// entrypoint added later cannot bypass them — which is why nothing in this class re-states a limit a caller could name.
/// </para>
/// <para>
/// What it does apply again is the deployment's snippet bounds and the greatest number of results a search serves, on
/// what it is about to publish. Those are not request input and not a caller's to widen: they are the control on how
/// much mail content one call draws out of a mailbox, and this is the last place that content passes before it reaches
/// a model.
/// </para>
/// <para>
/// It reaches no mail server, because the use case it calls speaks no mail protocol. A protocol request therefore cannot
/// wait on IMAP and cannot set the remote <c>\Seen</c> flag. It reaches no chat model either, and cannot: the
/// <c>Mcp</c> project depends on <c>Domain</c> and <c>Application</c> and on nothing that could rewrite or summarize a
/// query. A hybrid search does embed the query text, which is a comparison rather than an interpretation and happens
/// behind an application port this project cannot see.
/// </para>
/// <para>
/// The query text is the most revealing argument any MailFathom tool takes — what somebody is looking for in their own
/// mailbox — so nothing here writes it to a log, and no failure this path raises repeats it.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class SearchEmailsTool(
    MailboxSearchReader mailboxSearchReader,
    EmailSearchSnippetBounds snippetBounds,
    IMailAccountCatalog accountCatalog)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "search_emails";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>It reads the local mailbox copy, which is what <see cref="MailFathomPermission.MailRead" /> covers — not an egress-free grant, because a deployment configuring semantic retrieval places the caller's own query text with the embedding provider. What it does not cover is sending mail content to a chat provider, which <see cref="AskMailTool" /> requires its own permission for. Declaring it beside the name is what keeps <see cref="PublishedTools" /> able to answer for every tool this surface publishes.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailRead;

    /// <summary>The kind of thing this tool is for, which is what a deployment publishes or withholds it by.</summary>
    /// <remarks>It reads the local mailbox copy, which is the retrieval surface a deployment standing an instance up for reading publishes on its own. A category decides what this endpoint offers rather than who may reach it, so it turns nothing on: the tool appears only where the capability behind it is available and the caller's grant reaches it.</remarks>
    public static McpToolCategory Category => McpToolCategory.Mailbox;

    /// <summary>Searches the local mailbox copy for text and returns one bounded ranked window.</summary>
    /// <param name="queryText">The text to search for.</param>
    /// <param name="accounts">The accounts to search, named by identifier or display name, or none to search every account this deployment serves.</param>
    /// <param name="folders">The folders to search, each named by alias or by role, or none to search every folder of those accounts.</param>
    /// <param name="senderAddress">The address the sender must carry.</param>
    /// <param name="recipientAddress">The address a <c>To</c> or <c>Cc</c> recipient must carry.</param>
    /// <param name="subjectFragment">Text the subject must contain.</param>
    /// <param name="receivedOnOrAfter">The inclusive start of the received range.</param>
    /// <param name="receivedBefore">The exclusive end of the received range.</param>
    /// <param name="isRemotelySeen">The remote <c>\Seen</c> state to require.</param>
    /// <param name="isRemotelyFlagged">The remote <c>\Flagged</c> state to require.</param>
    /// <param name="keyword">The keyword an email must carry.</param>
    /// <param name="hasAttachments">Whether attachments are required.</param>
    /// <param name="resultLimit">How many ranked results to return, or none to take the default.</param>
    /// <param name="includeJunkMail">Whether the account's junk folder takes part in the search.</param>
    /// <param name="cancellationToken">Cancels the search when the caller disconnects or the host shuts down.</param>
    /// <returns>The ranked window, how it was retrieved, and how current each covered folder is.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when text naming an account or a folder alias is not a value this system could have issued.</exception>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a query text, a filter, a result count, or an account it refuses. The call-tool filter
    /// turns every one of them into the coded result a client reads, so this tool neither catches nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Search emails",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Searches the emails already synchronized into MailFathom's local mailbox copy for text, and returns the best "
        + "matches ranked by relevance with bounded extracts of the body around the matched words. Retrieval is lexical "
        + "or hybrid depending on how this server is configured, and every response says which in its retrievalMode "
        + "field: lexical finds the words a query contains rather than what they mean, while hybrid also finds mail "
        + "whose meaning is close and combines the two rankings. Words that appear only inside an attachment are never "
        + "searchable either way. Narrows by account, folder, sender address, recipient address, subject text, "
        + "received date range, remote seen state, remote flagged (starred) state, a keyword the mail server reported, "
        + "and attachment presence. Reads the local copy only: it never contacts "
        + "a mail server, never marks mail as read, and never returns whole bodies, raw MIME, or attachment content. "
        + "Mail in the account's junk folder is left out unless includeJunkMail is set. "
        + "Returns one window of at most 50 results that nothing continues, so narrow the filters or write a different "
        + "query to reach other mail. Matching nothing is a normal empty result rather than an error.")]
    public async Task<SearchEmailsToolResult> SearchEmailsAsync(
        [Description("The text to search for, up to 512 characters. Quoted phrases, OR, and a leading - to exclude a word are understood; every other punctuation mark is ordinary text. Write the words the mail itself is likely to contain, in the language it was written in rather than the language of your request: matching compares words rather than translating them, so a mailbox holding several languages is reached by a search per language. Required: a search with no text is a listing, which list_emails answers in a stable order and with a cursor.")]
        string queryText,
        [Description("MailFathom accounts to search, each named by its configured account identifier or by the display name it is published under. Omit to search every account this deployment serves; call list_accounts to see what they are. At most 64 may be named, and a name this deployment does not serve is refused rather than answered with an empty window.")]
        string[]? accounts = null,
        [Description("MailFathom folders to search, each named by its alias, such as INBOX, or by the role it plays, written as role:Junk. Roles are Inbox, Archive, Drafts, Sent, Junk, Trash, All, Flagged, Important, and Outbox; naming one searches whichever folder each account in scope maps with that role, whatever it is called there. Omit to search every folder of the accounts in scope. At most 64 may be named. An alias is MailFathom's own name for a folder and is matched without regard to case.")]
        string[]? folders = null,
        [Description("Return only emails sent from this mail address. Matched as a whole address rather than as a fragment, without regard to case; a non-empty value that is not a usable mail address is refused. Omit to match any sender, which an empty string does too.")]
        string? senderAddress = null,
        [Description("Return only emails addressed to this mail address in their To or Cc header. Matched as a whole address rather than as a fragment; Reply-To is not searched. Omit to match any recipient, which an empty string does too.")]
        string? recipientAddress = null,
        [Description("Return only emails whose subject contains this text, without regard to case, up to 256 characters. This narrows which emails are eligible before any of them is ranked and is unrelated to queryText, which is what the eligible ones are matched against. Omit to match any subject, which an empty string does too.")]
        string? subjectFragment = null,
        [Description("Return only emails received at or after this ISO 8601 timestamp. Emails whose received date is unknown are excluded whenever either bound is named. Omit for no lower bound.")]
        DateTimeOffset? receivedOnOrAfter = null,
        [Description("Return only emails received strictly before this ISO 8601 timestamp, so consecutive ranges built from one instant neither overlap nor leave a gap. Omit for no upper bound.")]
        DateTimeOffset? receivedBefore = null,
        [Description("Return only emails the mail server last reported as read (true) or unread (false). Omit to match either. Searching never changes this state. An email whose flags no run has observed yet counts as unread.")]
        bool? isRemotelySeen = null,
        [Description("Return only emails the mail server last reported as flagged (true) or unflagged (false), which is the star most mail clients show. Omit to match either. This is the \\Flagged flag on a message and is unrelated to the Flagged folder role; an email whose flags no run has observed yet counts as unflagged.")]
        bool? isRemotelyFlagged = null,
        [Description("Return only emails carrying this keyword, which is a flag a mail client or server set rather than one of the five standard ones, such as $Junk or a label. Matched as a whole keyword without regard to case; up to 64 characters, and a value that is not a keyword this system stores is refused. Omit to match any, which an empty string does too. The keywords each email carries are reported in its remoteFlags.")]
        string? keyword = null,
        [Description("Return only emails that carry attachments (true) or that carry none (false). Omit to match either. Inline images and cryptographic signature parts do not count as attachments.")]
        bool? hasAttachments = null,
        [Description("How many ranked results to return, from 1 to 50. Omit to take the default of 20. A value outside the range is refused rather than clamped, so a window is never smaller than it claims to be.")]
        int? resultLimit = null,
        [Description("Include mail in the account's junk folder, which is left out by default. Naming the junk folder in folderAliases does not include it; only this does. The result reports which answer produced it.")]
        bool includeJunkMail = false,
        CancellationToken cancellationToken = default)
    {
        var request = new SearchEmailsRequest
        {
            QueryText = queryText,
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
            ResultLimit = resultLimit,
            IncludeJunkMail = includeJunkMail,
        };

        var result = await mailboxSearchReader.SearchEmailsAsync(request, cancellationToken);

        return SearchEmailsToolResult.From(result, snippetBounds, PublishedAccountNames.From(accountCatalog));
    }
}
