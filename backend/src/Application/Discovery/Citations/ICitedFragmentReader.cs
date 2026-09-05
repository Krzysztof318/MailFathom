// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Discovery.Citations;

/// <summary>Reads the persisted passages of one message that a set of citations names.</summary>
/// <remarks>
/// <para>
/// The message is a parameter rather than something the passage identifiers are looked up across, and that is the
/// port's own safety rather than a convenience: a passage is only ever returned for the message it hangs on, so a
/// citation naming a passage of somebody else's mail reads nothing however it was composed. The caller establishes that
/// the message may be read before it asks.
/// </para>
/// <para>
/// A passage the identifiers name and the store no longer holds is absent from the answer rather than reported, because
/// re-chunking replaces a changed message's rows and the caller has one thing to say about every way a passage can be
/// gone. Nothing here reaches a mail server: a citation is followed through what chunking already derived.
/// </para>
/// </remarks>
public interface ICitedFragmentReader
{
    /// <summary>Reads the passages one message holds among those named.</summary>
    /// <param name="storedEmailId">The message the passages must belong to.</param>
    /// <param name="fragments">The passages to read, which is never empty.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The passages found, keyed by their identifiers, holding no entry for one the store does not have.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fragments" /> is <see langword="null" />.</exception>
    Task<IReadOnlyDictionary<EmailChunkId, CitedFragment>> ReadFragmentsAsync(
        StoredEmailId storedEmailId,
        IReadOnlyCollection<EmailChunkId> fragments,
        CancellationToken cancellationToken);
}
