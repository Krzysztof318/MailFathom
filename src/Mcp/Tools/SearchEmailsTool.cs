// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Emails;
using MailMcp.Application.Emails.SearchEmails;
using MailMcp.Domain.Failures;
using ModelContextProtocol.Server;

namespace MailMcp.Mcp.Tools;

/// <summary>Publishes the <c>search_emails</c> tool over the <see cref="MailboxSearchReader" /> use case.</summary>
/// <param name="mailboxSearchReader">Answers the search from the local mailbox copy.</param>
/// <param name="snippetBounds">How much of a message's body this deployment lets one result show.</param>
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
/// <c>Mcp</c> project depends on <c>Domain</c> and <c>Application</c> and on nothing that could embed, rewrite, or
/// summarize a query.
/// </para>
/// <para>
/// The query text is the most revealing argument any MailMcp tool takes — what somebody is looking for in their own
/// mailbox — so nothing here writes it to a log, and no failure this path raises repeats it.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class SearchEmailsTool(
    MailboxSearchReader mailboxSearchReader,
    EmailSearchSnippetBounds snippetBounds)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "search_emails";

    /// <summary>Searches the local mailbox copy for text and returns one bounded ranked window.</summary>
    /// <param name="queryText">The text to search for.</param>
    /// <param name="accountIds">The accounts to search, or none to search every account this deployment serves.</param>
    /// <param name="folderAliases">The folder aliases to search, or none to search every folder of those accounts.</param>
    /// <param name="senderAddress">The address the sender must carry.</param>
    /// <param name="recipientAddress">The address a <c>To</c> or <c>Cc</c> recipient must carry.</param>
    /// <param name="subjectFragment">Text the subject must contain.</param>
    /// <param name="receivedOnOrAfter">The inclusive start of the received range.</param>
    /// <param name="receivedBefore">The exclusive end of the received range.</param>
    /// <param name="isRemotelySeen">The remote <c>\Seen</c> state to require.</param>
    /// <param name="hasAttachments">Whether attachments are required.</param>
    /// <param name="resultLimit">How many ranked results to return, or none to take the default.</param>
    /// <param name="cancellationToken">Cancels the search when the caller disconnects or the host shuts down.</param>
    /// <returns>The ranked window, how it was retrieved, and how current each covered folder is.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when an account identifier or folder alias is not a value this system could have issued.</exception>
    /// <exception cref="MailMcpException">
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
        "Searches the emails already synchronized into MailMcp's local mailbox copy for text, and returns the best "
        + "matches ranked by relevance with bounded extracts of the body around the matched words. Retrieval is lexical: "
        + "it finds the words a query contains rather than what they mean, and words that appear only inside an "
        + "attachment are not searchable. Narrows by account, folder, sender address, recipient address, subject text, "
        + "received date range, remote seen state, and attachment presence. Reads the local copy only: it never contacts "
        + "a mail server, never marks mail as read, and never returns whole bodies, raw MIME, or attachment content. "
        + "Returns one window of at most 50 results that nothing continues, so narrow the filters or write a different "
        + "query to reach other mail. Matching nothing is a normal empty result rather than an error.")]
    public async Task<SearchEmailsToolResult> SearchEmailsAsync(
        [Description("The text to search for, up to 512 characters. Quoted phrases, OR, and a leading - to exclude a word are understood; every other punctuation mark is ordinary text. Required: a search with no text is a listing, which list_emails answers in a stable order and with a cursor.")]
        string queryText,
        [Description("Configured MailMcp account identifiers to search. Omit to search every account this deployment serves. At most 64 may be named, and an identifier this deployment does not serve is refused rather than answered with an empty window.")]
        string[]? accountIds = null,
        [Description("MailMcp folder aliases to search, such as INBOX. Omit to search every folder of the accounts in scope. At most 64 may be named. An alias is MailMcp's own name for a folder and is matched without regard to case.")]
        string[]? folderAliases = null,
        [Description("Return only emails sent from this mail address. Matched as a whole address rather than as a fragment, without regard to case; a value that is not a usable mail address is refused. Omit to match any sender.")]
        string? senderAddress = null,
        [Description("Return only emails addressed to this mail address in their To or Cc header. Matched as a whole address rather than as a fragment; Reply-To is not searched. Omit to match any recipient.")]
        string? recipientAddress = null,
        [Description("Return only emails whose subject contains this text, without regard to case, up to 256 characters. This narrows which emails are eligible before any of them is ranked and is unrelated to queryText, which is what the eligible ones are matched against. Omit to match any subject.")]
        string? subjectFragment = null,
        [Description("Return only emails received at or after this ISO 8601 timestamp. Emails whose received date is unknown are excluded whenever either bound is named. Omit for no lower bound.")]
        DateTimeOffset? receivedOnOrAfter = null,
        [Description("Return only emails received strictly before this ISO 8601 timestamp, so consecutive ranges built from one instant neither overlap nor leave a gap. Omit for no upper bound.")]
        DateTimeOffset? receivedBefore = null,
        [Description("Return only emails the mail server last reported as read (true) or unread (false). Omit to match either. Searching never changes this state. An email whose flags no run has observed yet counts as unread.")]
        bool? isRemotelySeen = null,
        [Description("Return only emails that carry attachments (true) or that carry none (false). Omit to match either. Inline images and cryptographic signature parts do not count as attachments.")]
        bool? hasAttachments = null,
        [Description("How many ranked results to return, from 1 to 50. Omit to take the default of 20. A value outside the range is refused rather than clamped, so a window is never smaller than it claims to be.")]
        int? resultLimit = null,
        CancellationToken cancellationToken = default)
    {
        var request = new SearchEmailsRequest
        {
            QueryText = queryText,
            AccountIds = MailboxScopeArguments.AccountIds(accountIds),
            FolderAliases = MailboxScopeArguments.FolderAliases(folderAliases),
            SenderAddress = senderAddress,
            RecipientAddress = recipientAddress,
            SubjectFragment = subjectFragment,
            ReceivedOnOrAfter = receivedOnOrAfter,
            ReceivedBefore = receivedBefore,
            IsRemotelySeen = isRemotelySeen,
            HasAttachments = hasAttachments,
            ResultLimit = resultLimit,
        };

        var result = await mailboxSearchReader.SearchEmailsAsync(request, cancellationToken);

        return SearchEmailsToolResult.From(result, snippetBounds);
    }
}
