// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>Reads the conversations whose state is missing or out of date, and writes down what one derivation produced.</summary>
/// <remarks>
/// <para>
/// The selection is the whole contract. A conversation leaves it by having a state recorded against the shape it
/// currently has, so a pass needs no cursor of its own, an interrupted pass repeats nothing and skips nothing, and what
/// one pass's bound leaves behind is the next run's. A conversation that gains a reply re-enters it, which is how a
/// state is kept current without anything watching for a change.
/// </para>
/// <para>
/// What puts a conversation into the selection is stated by the implementation rather than by the caller, so the
/// conditions the pipeline already decided — the classification gate, the rules having finished, a message not still on
/// its way out of a folder — are one predicate the pass cannot get out of order.
/// </para>
/// </remarks>
public interface IStoredThreadStateStore
{
    /// <summary>Reads one bounded batch of the account's conversations whose state is missing or out of date.</summary>
    /// <param name="account">The account whose conversations are walked.</param>
    /// <param name="batchSize">How many conversations the batch may hold.</param>
    /// <param name="maximumMessagesPerThread">How many messages one derivation may take in before the conversation is recorded as too large instead.</param>
    /// <param name="maximumCharactersPerMessage">How much of one message travels with the batch.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conversations, each carrying the shape it was read at and the messages a derivation reads — or no messages at all where it is past the bound.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any bound is below one.</exception>
    Task<IReadOnlyList<DerivableThread>> GetThreadsAwaitingStateAsync(
        MailAccountIdentity account,
        int batchSize,
        int maximumMessagesPerThread,
        int maximumCharactersPerMessage,
        CancellationToken cancellationToken);

    /// <summary>Stages what one derivation settled about one conversation, inside the caller's session.</summary>
    /// <param name="session">The transaction the write joins.</param>
    /// <param name="state">What was derived, which may hold no statements at all.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the record is staged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Idempotent from a fresh read: the record is keyed by the conversation, so two runs reaching one conversation
    /// converge on one row rather than on a history nobody asked for, and the statements a second write brings replace
    /// the first's rather than standing beside them.
    /// </remarks>
    Task SaveAsync(IPersistenceSession session, EmailThreadState state, CancellationToken cancellationToken);
}
