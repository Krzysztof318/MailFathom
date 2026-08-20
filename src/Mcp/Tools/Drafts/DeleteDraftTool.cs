// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools.Drafts;

/// <summary>Publishes the <c>delete_draft</c> tool over <see cref="MailDraftBook" />.</summary>
/// <param name="drafts">Gives the draft up and takes the copies of it back out of the mailbox.</param>
/// <remarks>
/// <para>
/// It is the destructive one of the three drafting tools, and it is destructive in the plain sense rather than the
/// widened one <c>send_email</c> uses: the message the owner wrote is gone and no call here brings it back. It is
/// idempotent all the same, because the state a second call asks for is the state the first one left — and the second
/// call is answered as a draft this deployment does not hold, since a given-up draft is exactly that.
/// </para>
/// <para>
/// <b>Only a draft this system created can be given up.</b> The identifier names a record MailFathom wrote, and the
/// copies that record names are the only messages the removal ever reaches — so a draft the owner wrote in their own
/// mail client is not refused by a check, it is unreachable, because nothing holds it under an identifier this tool
/// accepts.
/// </para>
/// <para>
/// A promoted draft is refused rather than given up. Its message is a queued send this would leave untouched, so
/// removing the draft would answer a caller asking for the message not to exist by sending it anyway; what stops such a
/// send is <c>cancel_outgoing_email</c>.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class DeleteDraftTool(MailDraftBook drafts)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "delete_draft";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>The drafting grant, for the reason the contact book's erasure travels with its writing grant: a caller that may put a message into the owner's drafts folder is the one that has to be able to take it back out again.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailDraftsWrite;

    /// <summary>Gives up one draft this deployment holds.</summary>
    /// <param name="draftId">The draft to give up, as <c>save_draft</c> returned it.</param>
    /// <param name="cancellationToken">Cancels the write when the caller disconnects or the host shuts down.</param>
    /// <returns>What giving the draft up did to the copy in the owner's mailbox.</returns>
    /// <exception cref="MailDraftRefusedException">Thrown when no draft this deployment holds is one to give up under that identifier.</exception>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a grant or a draft it refuses. The call-tool filter turns every one of them into the
    /// coded result a client reads, so this tool neither catches nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Delete draft",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Gives up a draft this deployment holds and takes the copy of it back out of the owner's Drafts folder. The "
        + "message the owner wrote is GONE and no call here brings it back, so ask the person you are acting for "
        + "before deleting something they wrote. Nothing is sent by this call and nothing was ever sent by the draft: "
        + "a draft reaches nobody. "
        + "Only a draft this deployment created can be deleted, named by the draftId save_draft answered. A message "
        + "the owner drafted in their own mail client is not one of them and is never touched. A draft that has "
        + "already been sent with send_draft is refused too, in the same way a draft that never existed is: the "
        + "message is a queued send that deleting the draft would leave running, and cancel_outgoing_email is what "
        + "stops it. Asking twice is safe and the second call is refused as a draft this deployment does not hold, "
        + "which is what a deleted draft is. "
        + "The result says whether the copy left the mailbox with it: a mail server may refuse to give a copy up, and "
        + "the folder a copy was put in may no longer be the one the account means by drafts — in both cases the "
        + "message is left as the owner's to delete themselves, and nothing here touches it again.")]
    public async Task<DeleteDraftToolResult> DeleteDraftAsync(
        [Description("The draftId save_draft returned for the draft you are giving up. A UUID that names nothing after this call.")]
        string draftId,
        CancellationToken cancellationToken = default)
    {
        var result = await drafts.DiscardAsync(
            AuthoredMailArguments.HeldDraft(draftId),
            cancellationToken);

        return DeleteDraftToolResult.From(result);
    }
}
