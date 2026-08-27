// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>Names a point at which text crosses out of this deployment and is therefore scanned before it does.</summary>
/// <remarks>
/// <para>
/// <b>This enumeration is the register of guarded egress.</b> A prompt is the point everybody thinks of and the least
/// likely to be the leak: a credential reaches a third party just as completely through an embedding request or a tool
/// result, and those paths are written by people who are not thinking about redaction at the time. A path that hands
/// text to somebody else and is not a member here is unguarded, so adding one is part of adding the path rather than a
/// follow-up to it.
/// </para>
/// <para>
/// Three paths the design names carry no member, and each absence is a fact about this deployment rather than an
/// omission. <b>Logs</b> and <b>audit events</b> carry no message text at all — every event of both is composed from
/// identifiers, this deployment's own configured aliases, counts, and outcomes, which is a rule the whole repository is
/// written under and the reason a finding is recorded here by category and rule rather than by value. Recording what was
/// found, where, and in which message would recreate the leak inside the record written to prevent it. <b>Webhook
/// payloads</b> have no member because MailFathom sends no webhook; the day it does, the payload is composed from mail
/// and the member arrives with it.
/// </para>
/// <para>
/// A member is a metric tag rather than a stored value, so the numbers are free to be reassigned only in the sense every
/// enum here is: allocated once, in declaration order, and never reordered or reused.
/// </para>
/// </remarks>
public enum SensitiveContentEgressPoint
{
    /// <summary>Text composed into a request to a chat provider, including a retrieved extract and a tool result.</summary>
    ChatPrompt = 0,

    /// <summary>Text sent to an embedding provider, which is a configured endpoint whether or not it is inside the deployment.</summary>
    HostedEmbeddingInput = 1,

    /// <summary>Text an MCP tool returns: a search snippet, a subject a listing publishes, and an answer a run produced.</summary>
    McpSnippet = 2,

    /// <summary>The message an MCP client asked for by identity: its body representations, its subject, and the display names its headers wrote.</summary>
    /// <remarks>
    /// Apart from the snippets above because it is the one point that publishes a whole body rather than an extract of
    /// one, and therefore the one whose latency an operator reads as the cost of scanning a read. Sharing a tag with
    /// the listing would average the two into a number describing neither.
    /// </remarks>
    McpEmailContent = 3,

    /// <summary>A message this deployment is about to put on a mail server: one being queued for transmission, and one being filed as a draft.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is the one member a redaction never reaches.</b> Every point above publishes something a reader is shown,
    /// so removing what a scanner found still answers the question that was asked. Here the text is a message somebody
    /// wrote, and rewriting it would transmit words its author never chose under their own address — so this point is
    /// screened by <see cref="SensitiveContentEgressScreen" /> and the act is refused instead. A deployment reads that
    /// difference on the instruments, which is why the point carries a member rather than being folded into one above.
    /// </para>
    /// <para>
    /// A draft shares it with a send rather than taking a member of its own, because what both do is put the author's
    /// text on a server this deployment does not own. That the drafts folder is the owner's own mailbox narrows who
    /// reads it and changes nothing about where the bytes end up, and a message written into it is one <c>send_draft</c>
    /// call away from a recipient.
    /// </para>
    /// </remarks>
    OutgoingMail = 4,

    /// <summary>Text the client API answers a message list with: the subjects, the sender display names, and the preview of the message's own text on every row.</summary>
    /// <remarks>
    /// Apart from the MCP listing rather than folded into it, because the two publish different amounts of a message
    /// and to different readers. A row carries the opening of the body and a tool listing carries none, so what a
    /// scanner finds here is found in text no other listing point ever sees — and sharing a tag would leave an operator
    /// unable to tell which surface a finding crossed, which is the one thing every tag on this register is for.
    /// </remarks>
    ClientMailListing = 5,

    /// <summary>Text the client API answers a search with: the subjects, the sender display names, the preview, and every highlighted extract of a result.</summary>
    /// <remarks>
    /// Apart from the client listing above rather than folded into it, because a search publishes text a list never
    /// does. An extract is cut around what somebody was looking for, so what crosses here is chosen by the query rather
    /// than by where a message sits in a folder — and a redaction rate that averaged the two would tell an operator
    /// nothing about either. It is also the point whose cost is paid per result and per extract rather than per row,
    /// which is the other thing a tag on this register is read for.
    /// </remarks>
    ClientMailSearch = 6,
}
