// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools;

/// <summary>Publishes the <c>list_accounts</c> tool over the <see cref="MailAccountDirectoryReader" /> use case.</summary>
/// <param name="mailAccountDirectoryReader">Reads the served accounts and their synchronization freshness.</param>
/// <param name="accountCatalog">Names the accounts a result publishes, which is the outward half of what the scope arguments do inward.</param>
/// <remarks>
/// <para>
/// The tool translates and nothing more. It takes no argument at all, calls the use case, and maps its answer onto the
/// published contract; which accounts exist and what may be said about them is the use case's decision, checked there so
/// an entrypoint added later cannot widen it.
/// </para>
/// <para>
/// It is the one tool on this surface that publishes the account set rather than using it as a bound, and it exists
/// because the other tools take an account filter a caller would otherwise have no way to fill in. What it publishes is
/// MailFathom's own configured names and the progress of synchronization against them: no mail server, no port, no user
/// name, and no credential reaches a caller through it.
/// </para>
/// <para>
/// It reaches no mail server, because the use case it calls speaks no mail protocol. A protocol request therefore cannot
/// wait on IMAP and cannot set the remote <c>\Seen</c> flag.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class ListAccountsTool(
    MailAccountDirectoryReader mailAccountDirectoryReader,
    IMailAccountCatalog accountCatalog)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "list_accounts";

    /// <summary>Lists the accounts this deployment serves.</summary>
    /// <param name="cancellationToken">Cancels the read when the caller disconnects or the host shuts down.</param>
    /// <returns>The served accounts with their folders' freshness, and whether the deployment refreshes them.</returns>
    [McpServerTool(
        Name = ToolName,
        Title = "List accounts",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Lists the mail accounts this MailFathom deployment serves. Call it to learn which mailboxes exist and what to "
        + "call them before narrowing a listing, a search, or a question to one: every account carries a configured "
        + "identifier and a readable display name, and either may be used to name it. Also reports how current the local "
        + "copy of each folder is and whether synchronization is running at all, which is what tells an empty answer "
        + "about a mailbox apart from a mailbox nothing has synchronized. Reads the local copy only: it never contacts a "
        + "mail server, and it returns no mail, no mail server address, no user name, and no credential.")]
    public async Task<ListAccountsToolResult> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        var directory = await mailAccountDirectoryReader.ReadAsync(cancellationToken);

        return ListAccountsToolResult.From(directory, PublishedAccountNames.From(accountCatalog));
    }
}
