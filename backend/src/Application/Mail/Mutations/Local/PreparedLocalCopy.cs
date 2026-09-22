// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Mutations.Local;

/// <summary>The payload and the metadata one held copy was placed with, before the transaction that writes its row.</summary>
/// <param name="Account">The account the copy is stored for.</param>
/// <param name="Source">The stored message being copied, whose flags and keywords the copy takes.</param>
/// <param name="Metadata">What was read out of the copied message's MIME, or <see langword="null" /> when nothing could be.</param>
/// <param name="Content">The payload, already under an object of its own.</param>
public sealed record PreparedLocalCopy(
    MailAccountId Account,
    StoredEmailId Source,
    ExtractedEmailMetadata? Metadata,
    PlacedEmailContent Content);

/// <summary>Every held copy one batch asks for, already placed.</summary>
/// <remarks>
/// <para>
/// It exists for the reason <see cref="Destinations.MailboxDestinations" /> exists: placing a payload must happen with
/// no transaction open across it, so the author is handed the answers rather than the collaborator that produces them
/// and cannot place one halfway through a commit.
/// </para>
/// <para>
/// The copies of one message are held as a list rather than keyed by the action that asked for one, because every copy
/// of a message carries the same payload and the same metadata: which of them an action takes decides nothing. An
/// author therefore walks the list in the order it submits its copies, and a transaction retried from the beginning
/// walks it from the beginning again and reaches the same answers.
/// </para>
/// <para>
/// A copy that is placed and never committed leaves an object nothing points at, which the content reclamation pass
/// takes exactly as it takes one left by a transaction that rolled back.
/// </para>
/// </remarks>
public sealed class PreparedLocalCopies
{
    private readonly IReadOnlyDictionary<StoredEmailId, IReadOnlyList<PreparedLocalCopy>> copies;

    internal PreparedLocalCopies(IReadOnlyDictionary<StoredEmailId, IReadOnlyList<PreparedLocalCopy>> copies) =>
        this.copies = copies;

    /// <summary>Gets the answers for a batch that asks for no held copy at all.</summary>
    public static PreparedLocalCopies None { get; } =
        new(new Dictionary<StoredEmailId, IReadOnlyList<PreparedLocalCopy>>());

    /// <summary>Finds the copies placed for one stored message.</summary>
    /// <param name="source">The message being copied.</param>
    /// <returns>The copies, in the order they were placed, or an empty list when none was.</returns>
    public IReadOnlyList<PreparedLocalCopy> Of(StoredEmailId source) =>
        this.copies.TryGetValue(source, out var placed) ? placed : [];
}
