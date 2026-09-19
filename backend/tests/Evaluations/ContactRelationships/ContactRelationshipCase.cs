// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Contacts.Relationship;
using MailFathom.Domain.Emails;
using MailFathom.Evaluations.Corpus;

namespace MailFathom.Evaluations.ContactRelationships;

/// <summary>One person's correspondence in the corpus put to the relationship agent, and what the card read from it has to say.</summary>
/// <remarks>
/// <para>
/// The correspondence is built the way the correspondence index answers for an opened contact: every conversation a
/// message from or to the address belongs to, named by the most recent of those messages, and every file the address
/// sent — each list newest first and held to the bounds a deployment holds it to. The turn carries subjects, file names,
/// and instants and no message text, so an expectation asks only what those can settle.
/// </para>
/// <para>
/// Every address here is under the corpus's reserved domain and every name belongs to nobody.
/// </para>
/// </remarks>
/// <param name="Name">The name the case is filed and reported under.</param>
/// <param name="Address">The address the correspondence is read for.</param>
/// <param name="Expectation">Names what the card gets wrong, given the correspondence the turn numbered, or answers <see langword="null" /> when it gets nothing wrong.</param>
internal sealed record ContactRelationshipCase(
    string Name,
    string Address,
    Func<ContactRelationship, ContactCorrespondence, string?> Expectation)
{
    /// <summary>Gets every case, in the order the report lists them.</summary>
    public static IReadOnlyList<ContactRelationshipCase> All { get; } =
    [
        // Nine conversations across support tickets, invoices, and travel: enough for a card that reads across them.
        new(
            "SeveralMatters",
            "wiebke.jankowski@harbourline.test",
            static (card, _) => card switch
            {
                { WasDerived: false } => "no card was derived from nine conversations across three kinds of matter.",
                _ when card.Observations.All(static observation => observation.Aspect is not ContactRelationshipAspect.Case)
                    => "the card names no cases, though the conversations span tickets, invoices, and travel.",
                _ => null,
            }),

        // One of the four conversations is named for an outstanding balance, which is the one thing left open.
        new(
            "OutstandingBalance",
            "vasco.iversen@saltmarsh.test",
            static (card, correspondence) =>
            {
                var outstanding = correspondence.Threads.Single(static thread =>
                    thread.Subject?.Contains("outstanding balance", StringComparison.OrdinalIgnoreCase) is true);

                ContactRelationshipStatement?[] pointing =
                [
                    card.NextAction,
                    .. card.Observations
                        .Where(static observation => observation.Aspect is ContactRelationshipAspect.OpenItem)
                        .Select(static observation => observation.Statement),
                ];

                return pointing.Any(statement => statement?.Sources.Contains(ContactRelationshipSource.Conversation(outstanding)) is true)
                    ? null
                    : "neither a next action nor an open item rests on the conversation named for the outstanding balance.";
            }),

        // A confirmed itinerary and a resolved ticket: nothing the subjects leave for anybody to do.
        new(
            "Settled",
            "lubomir.jankowski@saltmarsh.test",
            static (card, _) => card.NextAction is { } invented
                ? $"a settled correspondence was given a next action: \"{invented.Text}\""
                : null),

        // One conversation, and nothing the subject says that a reader would not already see in the list.
        new(
            "TooThin",
            "frida.almqvist@quietfjord.test",
            static (card, _) => card.WasDerived
                ? $"a card was derived from a single conversation: \"{card.Note!.Text}\""
                : null),
    ];

    /// <summary>Gets the correspondence as the correspondence index would answer it for this address.</summary>
    public ContactCorrespondence Correspondence
    {
        get
        {
            var threads = CorpusMessage.Exchanges
                .Select(static (exchange, index) => (exchange, index))
                .Where(conversation => conversation.exchange.Any(this.Names))
                .Select(conversation =>
                {
                    var latest = conversation.exchange.Where(this.Names).MaxBy(static message => message.ReceivedAt)!;

                    return new CorrespondingThread(
                        EmailThreadId.Create(new Guid(conversation.index + 1, 1, 0, new byte[8])),
                        latest.Id,
                        latest.Subject,
                        latest.ReceivedAt);
                })
                .OrderByDescending(static thread => thread.LastCorrespondedAt)
                .Take(ContactCorrespondenceBounds.Threads);

            var documents = CorpusMessage.All
                .Where(message => string.Equals(message.Sender, this.Address, StringComparison.OrdinalIgnoreCase))
                .SelectMany(static message => message.Attachments.Select((attachment, position) => new CorrespondingDocument(
                    message.Id,
                    position,
                    attachment.FileName,
                    attachment.DeclaredMediaType,
                    message.ReceivedAt)))
                .OrderByDescending(static document => document.ReceivedAt)
                .Take(ContactCorrespondenceBounds.Documents);

            return new ContactCorrespondence([.. threads], [.. documents]);
        }
    }

    /// <summary>Finds a case by the name it is filed under.</summary>
    /// <param name="name">The case's name.</param>
    /// <returns>The case.</returns>
    public static ContactRelationshipCase Named(string name) =>
        All.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => this.Name;

    private bool Names(CorpusMessage message) =>
        string.Equals(message.Sender, this.Address, StringComparison.OrdinalIgnoreCase)
        || message.Recipients.Contains(this.Address, StringComparer.OrdinalIgnoreCase);
}
