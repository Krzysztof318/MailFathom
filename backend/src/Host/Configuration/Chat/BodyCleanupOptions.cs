// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Declares which model the third rendering of a message body is decided by.</summary>
/// <remarks>
/// <para>
/// A block inside <c>Chat</c> rather than a root of its own, for the reason every block beside it is one: the pass runs
/// against the declared chat endpoint and has nowhere to send an outline without one.
/// </para>
/// <para>
/// <b>It carries no switch, and that is what separates it from every block beside it.</b> Each of those decides work
/// nobody asked for — a message arriving is derived, a conversation is read, a search is phrased — so an operator has to
/// be able to refuse the spend. This pass runs only where a reader has chosen the rendering in their own preferences,
/// which is a per-person answer the client already holds and this deployment already serves; a second switch here would
/// be an operator's copy of a decision that is not theirs, and a deployment would then have to explain why a setting the
/// reader turned on does nothing. A declared chat endpoint is therefore the whole of what turns this on, and no chat
/// endpoint is what turns it off — in which case a reader who chose the rendering is shown the reduced document with a
/// sentence saying this deployment does not clean a body.
/// </para>
/// <para>
/// <b>It is the first block under <c>Chat</c> that routes one feature to a model of its own.</b> Every other one runs on
/// <c>Chat:MainModel</c>, because what each of them does is the same kind of work a question is. This is not: deciding
/// which blocks of an outline to keep is a judgement a small fast model makes well and quickly, and a reader is waiting
/// for it in front of a message — so an operator can put this pass on a cheaper model without moving the one that
/// answers questions. Left empty it routes to <c>Chat:MainModel</c> like everything else.
/// </para>
/// <para>
/// There are no numbers here. What a deployment may spend on provider calls in total is declared once, in
/// <c>MailAnswering</c>, and every cleaning is admitted against those period ceilings and counted by the same ledgers — so
/// this competes for one allowance with the questions somebody asks, and raising those ceilings is the operator's decision
/// because it is their provider bill.
/// </para>
/// </remarks>
internal sealed class BodyCleanupOptions
{
    /// <summary>Gets or sets which declared model this pass is routed to, and empty to route it to <c>Chat:MainModel</c>.</summary>
    /// <remarks>
    /// An alias out of <c>Chat:Models</c> rather than a routed model name, which is what changed when the section became
    /// an array: a model named here is a declaration of its own, so the cheap model this pass runs on may sit at another
    /// address, under another credential, with bounds and a timeout of its own. It carries a <c>Fallback</c> like any
    /// other reference, because a pass a reader is waiting in front of is exactly the one worth answering from a second
    /// model rather than not at all.
    /// </remarks>
    public ChatModelReferenceOptions Model { get; set; } = new();
}
