// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Chat;

/// <summary>Names one piece of work a deployment generates text for, so the model it runs on can be declared separately from every other.</summary>
/// <remarks>
/// <para>
/// One value per agent rather than per boundary, because what an operator routes is the work: the passes that run on
/// every arriving message are cheap judgements worth a small fast model, answering a question and drafting a reply are
/// rare and worth the best model the deployment pays for, and describing a picture needs a model that can be shown one
/// at all. A single model for all of them makes every one of those choices for the operator.
/// </para>
/// <para>
/// It is also the key each capability's plan is registered under, which is what keeps the routing one lookup at
/// composition rather than a branch inside an agent. Nothing here names a configuration key: which section declares a
/// capability's model belongs to the host, and this boundary only knows that the capabilities differ.
/// </para>
/// </remarks>
public enum ChatCapability
{
    /// <summary>Answering a question about the mailbox from what the run retrieves while answering it.</summary>
    MailAnswering = 0,

    /// <summary>Reading a discovery request into the searches that would answer it.</summary>
    DiscoveryPlanning = 1,

    /// <summary>Composing what a discovery run found into the answer a reader is given.</summary>
    DiscoveryComposition = 2,

    /// <summary>Reading an arriving message into the marks a list row draws.</summary>
    Enrichment = 3,

    /// <summary>Reading a conversation into the state a client draws beside it.</summary>
    ThreadState = 4,

    /// <summary>Drafting a reply to a conversation somebody is answering.</summary>
    ReplyDrafting = 5,

    /// <summary>Reading an opened contact into a note about where the correspondence with them stands.</summary>
    ContactRelationship = 6,

    /// <summary>Reading a sentence typed into the search field into the filters it describes.</summary>
    SearchPhrasing = 7,

    /// <summary>Judging each candidate a retrieval produced before an answer is written from them.</summary>
    RelevanceFilter = 8,

    /// <summary>Describing an image attachment so its content can be searched.</summary>
    ImageDescription = 9,

    /// <summary>Deciding which blocks of a message body the third rendering keeps.</summary>
    BodyCleanup = 10,
}
