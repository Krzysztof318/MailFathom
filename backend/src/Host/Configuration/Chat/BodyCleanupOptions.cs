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
/// <c>Chat:Model</c>, because what each of them does is the same kind of work a question is. This is not: deciding which
/// blocks of an outline to keep is a judgement a small fast model makes well and quickly, and a reader is waiting for it
/// in front of a message — so an operator can put this pass on a cheaper model without moving the one that answers
/// questions. Left empty it routes to <c>Chat:Model</c> like everything else.
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
    /// <summary>Gets or sets the model identifier this pass is routed to, and empty to route it to <c>Chat:Model</c>.</summary>
    /// <remarks>
    /// Read the same way <c>Chat:Model</c> is: for a cloud deployment this is the name the operator gave the deployment
    /// rather than the vendor's model identifier, because that is the string the endpoint recognizes. It names a model on
    /// the same endpoint, under the same credential and over the same transport — a second provider would be a second
    /// endpoint declaration, which this block is not.
    /// </remarks>
    public string Model { get; set; } = string.Empty;
}
