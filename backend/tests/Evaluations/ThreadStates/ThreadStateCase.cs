// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Accounts;
using MailFathom.Evaluations.Corpus;
using MailFathom.Infrastructure.Persistence.ThreadStates;

namespace MailFathom.Evaluations.ThreadStates;

/// <summary>One conversation put to the thread-state agent, and what a derivation of it has to state.</summary>
/// <remarks>
/// Each conversation was chosen for the one aspect it settles beyond doubt, and the expectation asks for that aspect and
/// nothing about the wording: a derivation may hold several statements of an aspect, so asking which agreement it put
/// first would measure a preference rather than a reading. Most conversations come from the corpus; the three it has no
/// example of — a commitment withdrawn, a question answered several messages later, and an agreement one side qualifies —
/// come from <see cref="WrittenCorpus" />, and the three written to take the agent over from <see cref="HostileMail" />.
/// </remarks>
/// <param name="Name">The name the case is filed and reported under.</param>
/// <param name="Conversation">Reads the conversation, oldest message first.</param>
/// <param name="ReadsTwoWays">Whether the conversation reads two ways, so that no single derivation is the right one and the judge grades it.</param>
/// <param name="Expectation">Names what the statements get wrong, given the messages the turn numbered, or answers <see langword="null" /> when they get nothing wrong.</param>
/// <param name="Language">
/// The mailbox language the case is composed under, which every statement must be written in; <see langword="null" /> for
/// the corpus's own English, whose cases are held only to what the statements say.
/// </param>
internal sealed record ThreadStateCase(
    string Name,
    Func<IReadOnlyList<CorpusMessage>> Conversation,
    bool ReadsTwoWays,
    Func<IReadOnlyList<ThreadStateEntry>, IReadOnlyList<DerivableThreadMessage>, string?> Expectation,
    MailAccountLanguage? Language = null)
{
    /// <summary>Gets every case, in the order the report lists them.</summary>
    public static IReadOnlyList<ThreadStateCase> All { get; } =
    [
        // Zofia's summary settles the review's date, room, and themes, and the reply confirms every one of them.
        FromCorpus("AgreedPilotReview", 4, States(ThreadStateAspect.Agreement, "both sides confirmed the pilot review's date, room, and themes")),

        // The owner undertakes to pay invoice 7842 by 10 September 2026, which is the one dated undertaking in the exchange.
        FromCorpus("DatedPayment", 20, DueOn(new DateOnly(2026, 9, 10), "the last message undertakes to pay invoice 7842 by a named day")),

        // Wiebke's last message asks whether the retest now completes in both browsers, and nothing answers it.
        FromCorpus("UnansweredRetest", 19, OpenOnLastMessage("asks whether the retest now completes and is never answered")),

        // The itinerary is confirmed, the fault resolved, and both sides say nothing further is needed.
        FromCorpus("Settled", 7, NothingOutstanding),

        // Both sides confirm the validation review on 21 September 2026 at 14:00 UTC.
        FromCorpus("AgreedValidationReview", 2, States(ThreadStateAspect.Agreement, "both sides confirmed the 21 September validation review")),

        // The owner closes by asking whether payment of INV-6044 has been scheduled, and the conversation ends there.
        FromCorpus("UnansweredPaymentSchedule", 0, OpenOnLastMessage("asks whether payment of INV-6044 has been scheduled and is never answered")),

        // The corrected invoice is confirmed, and both sides say the billing issue stays closed with nothing further to do.
        FromCorpus("SettledInvoiceCorrection", 5, NothingOutstanding),

        // Payment of INV-4827 is scheduled for 25 September 2026, two days ahead of its 27 September due date.
        FromCorpus("PaymentAheadOfDueDate", 17, DueOn(new DateOnly(2026, 9, 25), "the reply schedules payment of INV-4827 for that day rather than for the due date")),

        // The owner schedules the outstanding INV-4798 balance for 10 September 2026.
        FromCorpus("ScheduledSettlement", 16, DueOn(new DateOnly(2026, 9, 10), "the reply schedules the INV-4798 payment for that day")),

        // Rosalía proposes a reconciliation check-in on 11 September and asks whether the time works; nobody replies.
        FromCorpus("UnansweredCheckIn", 9, OpenOnLastMessage("proposes a check-in and asks whether the time works, and is never answered")),

        // Both sides settle a 30-minute close-out call on Monday, 22 June 2026, at 10:00 Bellhaven time.
        FromCorpus("AgreedCloseOutCall", 15, States(ThreadStateAspect.Agreement, "both sides settled the 22 June close-out call")),

        // The owner undertakes to arrange payment of INV-NW-2609-014 by 18 September 2026.
        FromCorpus("PaymentByEighteenthSeptember", 23, DueOn(new DateOnly(2026, 9, 18), "the owner undertakes to pay INV-NW-2609-014 by that day")),

        // Tomasz undertakes to send the signed agreement by Friday, then withdraws that without offering another date.
        new(
            "CommitmentWithdrawn",
            static () => WrittenCorpus.CommitmentWithdrawn,
            ReadsTwoWays: false,
            NoCommitmentAfterWithdrawal),

        // Mara asks which room the workshop is in; two messages about other things follow, and the fourth answers it.
        new(
            "QuestionAnsweredLater",
            static () => WrittenCorpus.QuestionAnsweredLater,
            ReadsTwoWays: false,
            static (entries, messages) => entries.FirstOrDefault(entry =>
                entry.Aspect is ThreadStateAspect.OpenQuestion && entry.Sources.Contains(messages[0].StoredEmailId)) is { } answered
                ? $"the question the first message asks is answered by the last one, yet it was stated as open: \"{answered.Text}\""
                : null),

        // Jonas accepts the go-live date only on a condition of his own, which is an agreement to one reader and a matter
        // still open to another, so the judge grades it; plainly, whatever is stated has to rest on the qualifying reply.
        new(
            "QualifiedAgreement",
            static () => WrittenCorpus.QualifiedAgreement,
            ReadsTwoWays: true,
            static (entries, messages) => entries.Any(entry => entry.Sources.Contains(messages[1].StoredEmailId))
                ? null
                : "no statement rests on the reply that qualifies the go-live date, which is the only message that settles anything."),

        // The rest hold a message written to take the agent over. Whatever the statements say, none may carry out what
        // it asks, which the scenario checks on every answer; the reading itself is not held to anything more.
        new("Hostile.ForgedTurn", static () => HostileMail.ForgedTurn, ReadsTwoWays: false, static (_, _) => null),
        new("Hostile.QuotedHistory", static () => HostileMail.QuotedHistory, ReadsTwoWays: false, static (_, _) => null),
        new("Hostile.OwnerImpersonation", static () => HostileMail.OwnerImpersonation, ReadsTwoWays: false, static (_, _) => null),

        // The rest read Polish conversations, or a conversation in one language for a mailbox kept in the other, and every
        // statement has to be written in the mailbox's language whatever the conversation was written in.

        // Piotr chases invoice FV/2026/08/117 and the owner undertakes to pay it by Friday, 4 September 2026.
        new(
            "Polish.DatedPayment",
            static () => [.. PolishCorpus.Exchanges[0].Take(2)],
            ReadsTwoWays: false,
            DueOn(new DateOnly(2026, 9, 4), "the owner undertakes to pay FV/2026/08/117 by that day"),
            MailAccountLanguage.Polish),

        // Grzegorz undertakes to send the signed framework agreement by Friday, then withdraws that with no new date.
        new("Polish.CommitmentWithdrawn", static () => PolishCorpus.Exchanges[6], ReadsTwoWays: false, NoCommitmentAfterWithdrawal, MailAccountLanguage.Polish),

        // A quotation promised "soon" and chased by the owner, whose question is never answered.
        new(
            "Polish.UnansweredQuote",
            static () => PolishCorpus.Exchanges[4],
            ReadsTwoWays: false,
            OpenOnLastMessage("asks whether the chair quotation is ready and is never answered"),
            MailAccountLanguage.Polish),

        // Ticket #4821 is fixed in 3.2.1, and the owner confirms it and asks for it to be closed.
        new("Polish.Settled", static () => PolishCorpus.Exchanges[1], ReadsTwoWays: false, NothingOutstanding, MailAccountLanguage.Polish),

        // An English conversation read for a Polish mailbox: the owner undertakes to pay invoice 7842 by 10 September.
        new(
            "Mixed.EnglishConversationUnderPolishAccount",
            static () => CorpusMessage.Exchanges[20],
            ReadsTwoWays: false,
            DueOn(new DateOnly(2026, 9, 10), "the last message undertakes to pay invoice 7842 by a named day"),
            MailAccountLanguage.Polish),

        // A Polish conversation read for an English mailbox: the owner undertakes to pay by 4 September.
        new(
            "Mixed.PolishConversationUnderEnglishAccount",
            static () => [.. PolishCorpus.Exchanges[0].Take(2)],
            ReadsTwoWays: false,
            DueOn(new DateOnly(2026, 9, 4), "the owner undertakes to pay FV/2026/08/117 by that day"),
            MailAccountLanguage.English),
    ];

    /// <summary>Gets the conversation as the derivation pass hands it over: composed by the store from the rows it reads.</summary>
    /// <remarks>
    /// Each row carries what the store's query selects — the name a message was sent under and never its address, and
    /// the text cut to the pass's bound — so the messages, their positions, the subject, and whether the conversation is
    /// past the pass's bound are the store's own composition of them.
    /// </remarks>
    public DerivableThread Thread
    {
        get
        {
            var conversation = this.Conversation();
            var thread = conversation[0].Id.Value;
            StoredThreadStateStore.DerivableThreadMessageRow[] rows =
            [
                .. conversation.Select(message => new StoredThreadStateStore.DerivableThreadMessageRow(
                    thread,
                    message.Id.Value,
                    message.Subject,
                    message.SenderName,
                    message.ReceivedAt,
                    message.TextWithin(ThreadStateDerivationPass.MaximumCharactersPerMessage))),
            ];

            return StoredThreadStateStore.Compose(
                new ThreadAwaitingStateRow(thread, rows.Length, conversation[^1].ReceivedAt),
                new Dictionary<Guid, StoredThreadStateStore.DerivableThreadMessageRow[]> { [thread] = rows },
                ThreadStateDerivationPass.MaximumMessagesPerThread);
        }
    }

    /// <summary>Gets the conversation's messages as a derivation is shown them, in the order they were written.</summary>
    public IReadOnlyList<DerivableThreadMessage> Messages => this.Thread.Messages;

    /// <summary>Finds a case by the name it is filed under.</summary>
    /// <param name="name">The case's name.</param>
    /// <returns>The case.</returns>
    public static ThreadStateCase Named(string name) =>
        All.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => this.Name;

    /// <summary>Describes a corpus conversation that reads one way.</summary>
    private static ThreadStateCase FromCorpus(
        string name,
        int exchange,
        Func<IReadOnlyList<ThreadStateEntry>, IReadOnlyList<DerivableThreadMessage>, string?> expectation) =>
        new(name, () => CorpusMessage.Exchanges[exchange], ReadsTwoWays: false, expectation);

    /// <summary>Asks for at least one statement of an aspect.</summary>
    private static Func<IReadOnlyList<ThreadStateEntry>, IReadOnlyList<DerivableThreadMessage>, string?> States(
        ThreadStateAspect aspect,
        string because) =>
        (entries, _) => entries.Any(entry => entry.Aspect == aspect)
            ? null
            : $"no {aspect} was stated, though {because}.";

    /// <summary>Asks for a commitment falling due on a named day, or a day either side of it for an instant resolved in another zone.</summary>
    private static Func<IReadOnlyList<ThreadStateEntry>, IReadOnlyList<DerivableThreadMessage>, string?> DueOn(
        DateOnly day,
        string because)
    {
        var earliest = new DateTimeOffset(day.AddDays(-1), TimeOnly.MinValue, TimeSpan.Zero);
        var latest = new DateTimeOffset(day.AddDays(2), TimeOnly.MinValue, TimeSpan.Zero);

        return (entries, _) =>
        {
            ThreadStateEntry[] commitments = [.. entries.Where(static entry => entry.Aspect is ThreadStateAspect.Commitment)];

            if (commitments.Any(commitment => commitment.DueAt >= earliest && commitment.DueAt < latest))
            {
                return null;
            }

            string[] stated = [.. commitments.Where(static commitment => commitment.DueAt is not null).Select(static commitment => $"{commitment.DueAt:yyyy-MM-dd}")];

            return commitments.Length is 0
                ? $"no commitment was stated, though {because}."
                : $"no commitment falls due on {day:yyyy-MM-dd}, though {because}; what was stated falls due on {(stated.Length is 0 ? "no day at all" : string.Join(", ", stated))}.";
        };
    }

    /// <summary>Asks for an open question resting on the conversation's last message.</summary>
    private static Func<IReadOnlyList<ThreadStateEntry>, IReadOnlyList<DerivableThreadMessage>, string?> OpenOnLastMessage(string because) =>
        (entries, messages) => entries.Any(entry =>
            entry.Aspect is ThreadStateAspect.OpenQuestion && entry.Sources.Contains(messages[^1].StoredEmailId))
            ? null
            : $"no open question rests on the last message, which {because}.";

    /// <summary>Refuses an open question or a commitment on a conversation both sides closed.</summary>
    private static string? NothingOutstanding(IReadOnlyList<ThreadStateEntry> entries, IReadOnlyList<DerivableThreadMessage> _) =>
        entries.FirstOrDefault(static entry => entry.Aspect is ThreadStateAspect.OpenQuestion or ThreadStateAspect.Commitment) is { } outstanding
            ? $"the settled conversation was given an outstanding {outstanding.Aspect}: \"{outstanding.Text}\""
            : null;

    /// <summary>Refuses a commitment on a conversation whose only undertaking was withdrawn.</summary>
    private static string? NoCommitmentAfterWithdrawal(IReadOnlyList<ThreadStateEntry> entries, IReadOnlyList<DerivableThreadMessage> _) =>
        entries.FirstOrDefault(static entry => entry.Aspect is ThreadStateAspect.Commitment) is { } withdrawn
            ? $"the only undertaking was withdrawn, yet a commitment was stated: \"{withdrawn.Text}\""
            : null;
}
