// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>Reads the mail the arrival pipeline still owes a derivation, and writes down what one produced.</summary>
/// <remarks>
/// <para>
/// The selection is the whole contract, exactly as it is for the cut. A message leaves it by being enriched, so a pass
/// needs no cursor of its own, an interrupted pass repeats nothing and skips nothing, and what one pass's bound leaves
/// behind is the next run's. That is also what makes backfilling an existing mailbox resumable without a second
/// mechanism: a mailbox enriched for the first time drains over successive runs, a bound at a time.
/// </para>
/// <para>
/// What puts a message into the selection is stated by the implementation rather than by the caller, so the conditions
/// the pipeline already decided — the classification gate, the rules having finished, a message not still on its way
/// out of a folder — are one predicate the pass cannot get out of order. The one condition this selection adds is that
/// the message has passages, because a mark cites passages and a message with none has nothing to rest on.
/// </para>
/// </remarks>
public interface IStoredEmailEnrichmentStore
{
    /// <summary>Reads one bounded batch of the account's mail that is ready to be derived from and has not been.</summary>
    /// <param name="account">The account whose mail is walked.</param>
    /// <param name="batchSize">How many messages the batch may hold.</param>
    /// <param name="maximumPassagesPerEmail">How many of each message's leading passages travel with it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The messages, ordered by their identifier, each carrying the passages a derivation reads.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either bound is below one.</exception>
    /// <remarks>
    /// The passages travel with the batch rather than being fetched per message, because the batch is small by
    /// construction — one derivation is one provider call — and a second query per message would cost a round trip to
    /// save nothing.
    /// </remarks>
    Task<IReadOnlyList<EnrichableEmail>> GetEmailsAwaitingEnrichmentAsync(
        MailAccountIdentity account,
        int batchSize,
        int maximumPassagesPerEmail,
        CancellationToken cancellationToken);

    /// <summary>Stages what one derivation settled about one message, inside the caller's session.</summary>
    /// <param name="session">The transaction the write joins.</param>
    /// <param name="enrichment">What was derived, which may hold no marks at all.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the record is staged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="enrichment" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Idempotent from a fresh read: the record is keyed by the message, so two runs reaching one message converge on
    /// one row rather than on a history nobody asked for, and the marks a second write brings replace the first's.
    /// </remarks>
    Task SaveAsync(
        IPersistenceSession session,
        EmailEnrichment enrichment,
        CancellationToken cancellationToken);
}
