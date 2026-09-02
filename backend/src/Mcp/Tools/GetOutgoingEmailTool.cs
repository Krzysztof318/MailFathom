// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools;

/// <summary>Publishes the <c>get_outgoing_email</c> tool over the <see cref="OutgoingMailReader" /> use case.</summary>
/// <remarks>
/// <para>
/// It is what makes <c>queued</c> an acceptable answer from the sending tools. Delivery is asynchronous because a tool
/// call that waited for an SMTP exchange would be a tool call that blocks on a mail server, and the price of that is a
/// caller holding an identifier with no way to learn the outcome — which is the position an agent resolves by sending
/// again. So the sending tools name this one in their own descriptions, and a client reading the listing sees the pair.
/// </para>
/// <para>
/// Its annotations are the plain read-only set, and stating them beside the sending tools is half the point of the
/// pair. <c>readOnlyHint</c> and <c>idempotentHint</c> are <see langword="true" /> because it reads one durable record
/// and changes nothing, and <c>openWorldHint</c> is <see langword="false" /> because it reaches this deployment's own
/// database and nothing else — the same call that reports what a mail server said reaches no mail server to report it.
/// </para>
/// <para>
/// <b>It reads one send and cannot enumerate.</b> There is no listing of a mailbox's outgoing mail on this surface and
/// no argument here that could be widened into one: the call names a record the caller already holds an identifier for,
/// and a caller that kept none reaches nothing. The view of the whole outbox is the operator's, on the administrative
/// surface, which is a different endpoint reached with a different grant.
/// </para>
/// <para>
/// The grant is the sending one rather than the reading one, and what may be read is what this caller queued. A record
/// another caller queued is answered exactly as a record that does not exist, so neither the read grant nor an
/// identifier guessed at reaches somebody else's correspondence.
/// </para>
/// </remarks>
/// <param name="reader">Reads the record back, and decides which records this caller may see.</param>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class GetOutgoingEmailTool(OutgoingMailReader reader)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "get_outgoing_email";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>Reading back a send is part of sending rather than part of reading a mailbox: what it answers is what the caller itself asked to have sent, and a credential granted only to read mail must not learn what this mailbox has written to whom.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailSend;

    /// <summary>The kind of thing this tool is for, which is what a deployment publishes or withholds it by.</summary>
    /// <remarks>It belongs to the mail this deployment was asked to send, which a deployment that sends nothing publishes none of. A category decides what this endpoint offers rather than who may reach it, so it turns nothing on: the tool appears only where the capability behind it is available and the caller's grant reaches it.</remarks>
    public static McpToolCategory Category => McpToolCategory.Sending;

    /// <summary>Reports where one message this caller queued has got to.</summary>
    /// <param name="outgoingEmailId">The identifier a sending tool answered with.</param>
    /// <param name="cancellationToken">Cancels the read when the caller disconnects or the host shuts down.</param>
    /// <returns>The record as it stands.</returns>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a grant it refuses and for a send this caller may not be told about. The call-tool
    /// filter turns every one of them into the coded result a client reads, so this tool neither catches nor
    /// re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Get outgoing email",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Reports what became of a message you queued with send_email, reply_to_email, or forward_email: how far it has "
        + "got, how many delivery attempts it has taken, what a mail server has said about each person it is addressed "
        + "to, and the error code it stopped on if it stopped. Call this instead of sending again when you are unsure "
        + "whether a send went through — a second send is a second message in somebody's mailbox and cannot be "
        + "recalled. It reads a durable record this deployment already holds and speaks to no mail server, so the "
        + "answer is as fresh as the last delivery attempt rather than a live check with the provider. You can only "
        + "read back a message you queued yourself, and a message queued by anybody else reads as not found. There is "
        + "no way to list what a mailbox has sent: this tool answers about one identifier at a time and nothing here "
        + "enumerates. The answer says nothing about the message itself — no subject, no body, no attachments.")]
    public async Task<OutgoingEmailToolResult> GetOutgoingEmailAsync(
        [Description("The identifier of the queued message, exactly as the sending tool returned it in outgoingEmailId.")]
        string outgoingEmailId,
        CancellationToken cancellationToken = default)
    {
        var record = await reader.ReadAsync(
            AuthoredMailArguments.QueuedSend(outgoingEmailId),
            cancellationToken);

        return OutgoingEmailToolResult.From(record);
    }
}
