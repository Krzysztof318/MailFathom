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
/// sent — each list newest first and cut to the index's <see cref="ContactCorrespondenceBounds.Threads" /> and
/// <see cref="ContactCorrespondenceBounds.Documents" />. The index's recency window is deliberately not applied: it is
/// measured back from the moment a contact is opened, so applying it here would make what a case measures depend on the
/// day the run happens and would empty every case once the corpus is a year old. Its message-scan bound is not applied
/// either, because the whole corpus is smaller than it. The turn carries subjects, file names, and instants and no message
/// text, so an expectation asks only what those can settle.
/// </para>
/// <para>
/// The mailbox the index reads is the corpus beside <see cref="WrittenCorpus" />, whose people share no address with the
/// corpus's: the corpus's correspondences all sound alike, and a tone that turns over the months, a person only ever
/// copied, a weekly report, a negotiated document, and a newsletter are what the written conversations add.
/// </para>
/// <para>
/// Every address here is under a reserved domain and every name belongs to nobody.
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
        new("SeveralMatters", "wiebke.jankowski@harbourline.test", NamesItsCases),

        // One of the four conversations is named for an outstanding balance, which is the one thing left open.
        new(
            "OutstandingBalance",
            "vasco.iversen@saltmarsh.test",
            static (card, correspondence) => PointsAt(card, correspondence.Threads.Single(static thread =>
                thread.Subject?.Contains("outstanding balance", StringComparison.OrdinalIgnoreCase) is true))
                ? null
                : "neither a next action nor an open item rests on the conversation named for the outstanding balance."),

        // A confirmed itinerary and a resolved ticket: nothing the subjects leave for anybody to do.
        new("Settled", "lubomir.jankowski@saltmarsh.test", NoNextAction),

        // One conversation, and nothing the subject says that a reader would not already see in the list.
        new(
            "TooThin",
            "frida.almqvist@quietfjord.test",
            static (card, _) => card.WasDerived
                ? $"a card was derived from a single conversation: \"{card.Note!.Text}\""
                : null),

        // A support ticket, a pilot review, and an invoice correction across three conversations.
        new("ThreeMatters", "zofia.iversen@quietfjord.test", NamesItsCases),

        // One itinerary conversation and the file sent with it, which a list of the two already says everything about.
        new("OneItinerary", "halina.pettersen@latticeworks.test", NoCard),

        // One billing conversation and one file, too thin for a card for the same reason.
        new("OneBillingThread", "soren.esposito@northreach.test", NoCard),

        // One ticket whose subject says it is resolved, which leaves the owner nothing to do.
        new("ResolvedTicketOnly", "ada.zielinska@harbourline.test", NoNextAction),

        // Welcome, thanks, an overdue invoice, a second reminder, and a final notice: the tone turned, and the last is open.
        new(
            "ToneTurnedToFinalNotice",
            "oskar.brandt@fernwick.test",
            static (card, correspondence) => PointsAt(card, correspondence.Threads.MaxBy(static thread => thread.LastCorrespondedAt)!)
                ? null
                : "neither a next action nor an open item rests on the final notice, which is the one thing left open."),

        // Copied on minutes, an address change, and a signed agreement for the record — never asked anything.
        new("OnlyInCopy", "greta.holm@lindenrow.test", NoNextAction),

        // A status report every Monday morning, four weeks running.
        new(
            "WeeklyReports",
            "priya.nair@quaymark.test",
            static (card, _) => card.Observations.Any(static observation => observation.Aspect is ContactRelationshipAspect.ActivePeriod)
                ? null
                : "the card says nothing about when this person's messages arrive, though every report came on a Monday morning."),

        // A contract negotiated through two drafts and a signed copy, each sent as a file.
        new(
            "NegotiatedContract",
            "hanna.kowal@fernwick.test",
            static (card, _) => Statements(card).Any(static statement => statement.Sources.Any(static source => source.AttachmentPosition is not null))
                ? null
                : "no line of the card rests on any of the three contract files this person sent, which are the whole correspondence."),

        // Three issues of a product newsletter, which ask nothing of anybody.
        new(
            "NewsletterOnly",
            "digest@harbourline.test",
            static (card, _) => card.NextAction is not null || card.Observations.Any(static observation => observation.Aspect is ContactRelationshipAspect.OpenItem)
                ? "a newsletter's correspondence was given something to do or something left open."
                : null),

        // A quotation asked for and acknowledged, then chased in a conversation of its own.
        new(
            "UnansweredQuote",
            "emil.varga@birchline.test",
            static (card, correspondence) => PointsAt(card, correspondence.Threads.Single(static thread =>
                thread.Subject?.StartsWith("Still waiting", StringComparison.Ordinal) is true))
                ? null
                : "neither a next action nor an open item rests on the conversation chasing the quote."),

        // A flight, a hotel, and a final itinerary, which leave nothing to do.
        new("FinalisedItinerary", "nadia.rossi@birchline.test", NoNextAction),
    ];

    /// <summary>Gets every conversation the mailbox holds, the corpus's first and the written ones after them.</summary>
    private static IEnumerable<IReadOnlyList<CorpusMessage>> Mailbox => CorpusMessage.Exchanges.Concat(WrittenCorpus.Exchanges);

    /// <summary>Gets the correspondence as the correspondence index would answer it for this address.</summary>
    public ContactCorrespondence Correspondence
    {
        get
        {
            var threads = Mailbox
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

            var documents = Mailbox
                .SelectMany(static exchange => exchange)
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

    private static string? NamesItsCases(ContactRelationship card, ContactCorrespondence correspondence) => card switch
    {
        { WasDerived: false } => $"no card was derived from {correspondence.Threads.Count} conversations across several kinds of matter.",
        _ when card.Observations.All(static observation => observation.Aspect is not ContactRelationshipAspect.Case)
            => "the card names no cases, though the conversations span several kinds of matter.",
        _ => null,
    };

    private static string? NoCard(ContactRelationship card, ContactCorrespondence correspondence) =>
        card.WasDerived
            ? $"a card was derived from one conversation and {correspondence.Documents.Count} file: \"{card.Note!.Text}\""
            : null;

    private static string? NoNextAction(ContactRelationship card, ContactCorrespondence _) =>
        card.NextAction is { } invented
            ? $"a correspondence that leaves the owner nothing to do was given a next action: \"{invented.Text}\""
            : null;

    private static IEnumerable<ContactRelationshipStatement> Statements(ContactRelationship card) =>
        [.. new[] { card.Note, card.NextAction }.OfType<ContactRelationshipStatement>(), .. card.Observations.Select(static observation => observation.Statement)];

    /// <summary>Whether a next action or an open item rests on one conversation.</summary>
    private static bool PointsAt(ContactRelationship card, CorrespondingThread thread)
    {
        ContactRelationshipStatement?[] pointing =
        [
            card.NextAction,
            .. card.Observations
                .Where(static observation => observation.Aspect is ContactRelationshipAspect.OpenItem)
                .Select(static observation => observation.Statement),
        ];

        return pointing.Any(statement => statement?.Sources.Contains(ContactRelationshipSource.Conversation(thread)) is true);
    }

    private bool Names(CorpusMessage message) =>
        string.Equals(message.Sender, this.Address, StringComparison.OrdinalIgnoreCase)
        || message.Recipients.Contains(this.Address, StringComparer.OrdinalIgnoreCase);
}
