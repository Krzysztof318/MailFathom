// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.History;

/// <summary>Reads back what classification concluded about an account's mail, and what those conclusions asked for.</summary>
/// <remarks>
/// <para>
/// A reading port rather than a store, because nothing here writes and nothing here owns a table. What it composes is
/// the classification record beside the mutation records a verdict opened, which are two things that already exist and
/// are already erased with the mail they describe — so the read-back inherits every retention and erasure obligation it
/// would otherwise have had to be given.
/// </para>
/// <para>
/// The projection is what keeps it safe to serve over an administrative endpoint. A signal's observation is text a mail
/// server wrote and can carry a sending domain, so the reader returns signal names alone and never reaches a value; a
/// mutation is named and pointed at rather than described, so what became of it stays the mutation trail's one answer.
/// </para>
/// </remarks>
public interface ISpamClassificationHistoryReader
{
    /// <summary>Reads one bounded page of an account's classifications, newest first.</summary>
    /// <param name="query">The validated filters and the boundary the page continues from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, with the cursor for the following one where more records match.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    Task<SpamClassificationHistoryPage> ReadPageAsync(
        SpamClassificationHistoryQuery query,
        CancellationToken cancellationToken);
}
