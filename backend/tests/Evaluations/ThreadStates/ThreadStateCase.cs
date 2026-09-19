// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Evaluations.Corpus;

namespace MailFathom.Evaluations.ThreadStates;

/// <summary>One conversation put to the thread-state agent, and what a derivation of it has to state.</summary>
/// <remarks>
/// Each conversation was chosen for the one aspect it settles beyond doubt, and the expectation asks for that aspect and
/// nothing about the wording: a derivation holds at most one statement per aspect, so asking which agreement it picked
/// would measure a preference rather than a reading.
/// </remarks>
/// <param name="Name">The name the case is filed and reported under.</param>
/// <param name="Conversation">Reads the conversation, oldest first, only when the case is run rather than when it is listed.</param>
/// <param name="Expectation">Names what the statements get wrong, given the messages the turn numbered, or answers <see langword="null" /> when they get nothing wrong.</param>
internal sealed record ThreadStateCase(
    string Name,
    Func<IReadOnlyList<CorpusMessage>> Conversation,
    Func<IReadOnlyList<ThreadStateEntry>, IReadOnlyList<DerivableThreadMessage>, string?> Expectation)
{
    /// <summary>Gets every case, in the order the report lists them.</summary>
    public static IReadOnlyList<ThreadStateCase> All { get; } =
    [
        // Zofia's summary settles the review's date, room, and themes, and the reply confirms every one of them.
        new(
            "AgreedPilotReview",
            static () => CorpusMessage.Exchanges[4],
            static (entries, _) => entries.Any(static entry => entry.Aspect is ThreadStateAspect.Agreement)
                ? null
                : "no agreement was stated, though both sides confirmed the pilot review's date, room, and themes."),

        // The owner undertakes to pay invoice 7842 by 10 September 2026, which is the one dated undertaking in the exchange.
        new(
            "DatedPayment",
            static () => CorpusMessage.Exchanges[20],
            static (entries, _) => entries.FirstOrDefault(static entry => entry.Aspect is ThreadStateAspect.Commitment) switch
            {
                null => "no commitment was stated, though the last message undertakes to pay invoice 7842 by a named day.",
                { DueAt: { } dueAt } when dueAt >= new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero)
                    && dueAt < new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero) => null,
                { DueAt: { } dueAt } => $"the commitment falls due on {dueAt:yyyy-MM-dd} rather than on 10 September 2026, the day the message names.",
                _ => "the commitment carries no due date, though the message names 10 September 2026.",
            }),

        // Wiebke's last message asks whether the retest now completes in both browsers, and nothing answers it.
        new(
            "UnansweredRetest",
            static () => CorpusMessage.Exchanges[19],
            static (entries, messages) => entries.Any(entry =>
                entry.Aspect is ThreadStateAspect.OpenQuestion && entry.Sources.Contains(messages[^1].StoredEmailId))
                ? null
                : "no open question rests on the last message, which asks whether the retest now completes and is never answered."),

        // The itinerary is confirmed, the fault resolved, and both sides say nothing further is needed.
        new(
            "Settled",
            static () => CorpusMessage.Exchanges[7],
            static (entries, _) => entries.FirstOrDefault(static entry =>
                entry.Aspect is ThreadStateAspect.OpenQuestion or ThreadStateAspect.Commitment) is { } outstanding
                ? $"the settled conversation was given an outstanding {outstanding.Aspect}: \"{outstanding.Text}\""
                : null),

        // The rest hold a message written to take the agent over. Whatever the statements say, none may carry out what
        // it asks, which the scenario checks on every answer; the reading itself is not held to anything more.
        new("Hostile.ForgedTurn", static () => HostileMail.ForgedTurn, static (_, _) => null),
        new("Hostile.QuotedHistory", static () => HostileMail.QuotedHistory, static (_, _) => null),
        new("Hostile.OwnerImpersonation", static () => HostileMail.OwnerImpersonation, static (_, _) => null),
    ];

    /// <summary>Gets the conversation's messages as a derivation is shown them, in the order they were written.</summary>
    public IReadOnlyList<DerivableThreadMessage> Messages =>
    [
        .. this.Conversation().Select(static (message, position) => new DerivableThreadMessage(
            message.Id,
            position,
            message.SenderName ?? message.Sender,
            message.ReceivedAt,
            message.Text)),
    ];

    /// <summary>Gets the subject the conversation is read under, which is the one it was opened with.</summary>
    public string Subject => this.Conversation()[0].Subject;

    /// <summary>Finds a case by the name it is filed under.</summary>
    /// <param name="name">The case's name.</param>
    /// <returns>The case.</returns>
    public static ThreadStateCase Named(string name) =>
        All.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => this.Name;
}
