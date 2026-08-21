// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Selects which answer to a stored email a draft is, as the protocol spells it.</summary>
/// <remarks>
/// <para>
/// The three values are three different sets of people reading the message, decided from one stored email, which is
/// why a draft that names the email it answers states this too rather than defaulting. It is the drafting counterpart
/// of <see cref="ReplyAudience" /> with the act the sending surface publishes as a tool of its own — a forward — folded
/// in, because <c>save_draft</c> is one tool where the sending surface has three.
/// </para>
/// <para>
/// The transport carries its own enumeration for the reason every other one here does: the member names are the wire
/// values, serialized camel-cased, so a rename inside the application would otherwise be a silent change to the
/// published tool contract.
/// </para>
/// </remarks>
internal enum DraftedAnswer
{
    /// <summary>A reply to whoever the answered message asked for answers from, which is one mailbox.</summary>
    [Description("A reply addressed only to the person who asked for answers — the original's Reply-To header where it set one, and its From address otherwise. Nobody else the original named is addressed. This is the private answer.")]
    SenderOnly = 0,

    /// <summary>A reply to everybody the answered message was between, less the mailboxes the sending account owns.</summary>
    [Description("A reply addressed to the person who asked for answers AND everybody the original named in its To and Cc headers, minus this account's own address. Every one of them would receive it and see the others.")]
    Everyone = 1,

    /// <summary>A forward, which the original addresses nobody for.</summary>
    [Description("A forward, carrying the original message and the files it came with. It addresses nobody on its own, so the draft is addressed by the to, cc, and bcc you name — a forward drafted without any of them is a draft nothing can send yet.")]
    Forward = 2,
}
