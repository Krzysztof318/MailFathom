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

/// <summary>Publishes the <c>send_draft</c> tool over <see cref="MailDraftPromotion" />.</summary>
/// <param name="promotion">Queues the draft as an ordinary send, and refuses it where it cannot be queued.</param>
/// <remarks>
/// <para>
/// This is the tool the other three exist to be safe without. It sends real mail, so it carries every requirement a
/// sending tool carries and is annotated exactly as <c>send_email</c> is:
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0013-what-a-caller-must-do-before-mail-leaves.md">ADR 0013</see>
/// settles all four values, and the description says what leaves and that it cannot be recalled. It is admitted under
/// the sending grant rather than the drafting one, which is what makes an agent granted only the drafting half unable
/// to reach it and unable to see it in a listing.
/// </para>
/// <para>
/// <b>The call never transmits.</b> The promotion writes an ordinary outgoing record carrying the bytes the draft
/// already is, and the account's delivery pass offers it to a submission server afterwards — so the result is the
/// send's own, in the send's own words, and a caller reading it must not report that mail arrived.
/// </para>
/// <para>
/// <b>There is no idempotency key, and that is not an omission.</b> A draft is promoted once, so the draft itself is
/// the identity: two callers promoting one draft compose one request, and the second is answered with the record the
/// first wrote. A key whoever asked supplied would make their two asks two requests and put the message in the
/// recipient's mailbox twice, which nothing downstream can withdraw.
/// </para>
/// <para>
/// Every bound this deployment sets is asked again at the moment the message would leave rather than only when the
/// draft was written, so a draft composed before an operator tightened the recipient policy, lowered a ceiling, or
/// turned sending off for the account is refused by what holds now. A promotion that fails leaves the draft exactly as
/// it was.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class SendDraftTool(MailDraftPromotion promotion)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "send_draft";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>
    /// It is the sending grant and nothing weaker, because what this does is send: the drafting grant admits writing a
    /// message into the owner's own folder, and no amount of that adds up to permission for one to leave. A caller
    /// holding the drafting grant alone is not offered this tool at all, and a call naming it is answered as a call
    /// naming a tool that does not exist.
    /// </remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailSend;

    /// <summary>Queues the message one draft holds, exactly as it stands.</summary>
    /// <param name="draftId">The draft to send, as <c>save_draft</c> returned it.</param>
    /// <param name="cancellationToken">Cancels the write when the caller disconnects or the host shuts down.</param>
    /// <returns>The record the message was queued as.</returns>
    /// <exception cref="MailDraftRefusedException">Thrown when no draft this deployment holds is one to send under that identifier, the draft names nobody to send it to, or the stored message exceeds what this deployment sends.</exception>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a grant, an account, a recipient, or a ceiling it refuses. The call-tool filter turns
    /// every one of them into the coded result a client reads, so this tool neither catches nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Send draft",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Sends a real email: the message a draft holds, exactly as the owner would read it in their Drafts folder. It "
        + "reaches strangers' mailboxes and CANNOT be recalled, edited, or deleted once it has left — treat every call "
        + "as final, and ask the person you are acting for before sending on their behalf. This is the one draft tool "
        + "that causes mail to leave; save_draft, update_draft, and delete_draft send nothing. The call itself "
        + "transmits nothing: the message is written down durably and a delivery pass offers it to a mail server "
        + "seconds later, so the result says queued and never that anything was delivered. Call get_outgoing_email "
        + "with the outgoingEmailId it answers to learn what became of the message, and cancel_outgoing_email to stop "
        + "it while it is still waiting. "
        + "Nothing is recomposed and nothing may be changed here: what goes out is what the draft says, so edit it "
        + "with update_draft first and read what you wrote before sending. There is no idempotencyKey, because the "
        + "draft is the identity: promoting one draft sends ONE message however many times you call, and a repeated "
        + "call answers with the record the first one wrote rather than sending a second message. "
        + "A draft addressed to nobody is refused naming that, and the remedy is update_draft rather than a second "
        + "save. Everything this deployment refuses a send for is asked again now rather than when the draft was "
        + "written, so a draft composed before a limit was tightened is refused by the limit that holds today, and a "
        + "refusal leaves the draft exactly as it was. "
        + "The draft is not deleted when this answers: the message is queued rather than sent, so the copy stands in "
        + "the owner's folder until the message has actually been delivered and is taken out in the same pass that "
        + "files the sent copy.")]
    public async Task<SendEmailToolResult> SendDraftAsync(
        [Description("The draftId save_draft returned for the draft you are sending. A UUID, and the whole of what this call takes: the message, the recipients, and the account are the draft's.")]
        string draftId,
        CancellationToken cancellationToken = default)
    {
        var record = await promotion.PromoteAsync(
            AuthoredMailArguments.HeldDraft(draftId),
            cancellationToken);

        return SendEmailToolResult.From(record);
    }
}
