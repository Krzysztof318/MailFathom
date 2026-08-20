// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools;

/// <summary>Publishes the <c>cancel_outgoing_email</c> tool over the <see cref="OutgoingMailCancellation" /> use case.</summary>
/// <remarks>
/// <para>
/// This is the only point at which sending is reversible at all, and the window it acts in is the one between the
/// record being written and the first byte of the body going out. Past that the message is in somebody else's mailbox
/// and the call says so rather than reporting a withdrawal it did not perform.
/// </para>
/// <para>
/// Its annotations are the third shape this surface publishes, and the reason it is worth having all three is here.
/// <c>destructiveHint</c> is <see langword="true" /> because the call destroys something the caller created and no
/// further call brings it back, while <c>openWorldHint</c> is <see langword="false" /> because the whole act happens
/// inside this process: it stops a message from leaving rather than reaching out to anybody. A sending tool is the
/// opposite pair — destructive because it cannot be undone, open-world because it reaches a stranger's server — and a
/// client that reads the four annotations rather than one of them can tell those two apart.
/// </para>
/// <para>
/// <c>idempotentHint</c> is <see langword="true" /> and is true of the tool rather than of a careful caller: a send
/// already withdrawn is answered with itself and nothing is written a second time.
/// </para>
/// <para>
/// What may be withdrawn is what this caller queued. A record another caller queued is answered exactly as a record
/// that does not exist, so nothing here reaches a message this caller did not ask for.
/// </para>
/// </remarks>
/// <param name="cancellation">Withdraws the send, and refuses one that can no longer be withdrawn.</param>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class CancelOutgoingEmailTool(OutgoingMailCancellation cancellation)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "cancel_outgoing_email";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>Stopping a send is part of sending: what a caller may withdraw is exactly what it was allowed to start, so no grant of its own is minted for taking back what one grant already permitted.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailSend;

    /// <summary>Withdraws one message this caller queued, while nothing has begun transmitting it.</summary>
    /// <param name="outgoingEmailId">The identifier a sending tool answered with.</param>
    /// <param name="cancellationToken">Cancels the call when the caller disconnects or the host shuts down.</param>
    /// <returns>The record as it stands after the call.</returns>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a grant it refuses, for a send this caller may not be told about, and for a send that
    /// can no longer be withdrawn. The call-tool filter turns every one of them into the coded result a client reads,
    /// so this tool neither catches nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Cancel outgoing email",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Stops a message you queued from being sent, while it is still waiting. It CANNOT recall a message that has "
        + "already been transmitted: once the delivery pass has begun offering the message to a mail server the call "
        + "is refused and nothing is withdrawn, and that window is ordinarily seconds long. A message that was already "
        + "cancelled is answered with its state and nothing happens twice, so repeating the call is safe. The message "
        + "is destroyed rather than paused — nothing here reschedules a send, and no further call brings a cancelled "
        + "message back; queue it again with a sending tool and a new idempotencyKey if you still want it sent. It "
        + "reaches no mail server and nobody outside this deployment. You can only cancel a message you queued "
        + "yourself, and a message queued by anybody else reads as not found. Check the state it answers with, or call "
        + "get_outgoing_email, rather than assuming the message is gone.")]
    public async Task<OutgoingEmailToolResult> CancelOutgoingEmailAsync(
        [Description("The identifier of the queued message, exactly as the sending tool returned it in outgoingEmailId.")]
        string outgoingEmailId,
        CancellationToken cancellationToken = default)
    {
        var record = await cancellation.CancelAsync(
            AuthoredMailArguments.QueuedSend(outgoingEmailId),
            cancellationToken);

        return OutgoingEmailToolResult.From(record);
    }
}
