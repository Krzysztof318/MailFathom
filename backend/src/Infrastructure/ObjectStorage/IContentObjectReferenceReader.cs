// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Answers which of a page of object keys a stored payload still points at.</summary>
/// <remarks>
/// <para>
/// The one question reclamation asks of the database, and it is asked of a page rather than of the table: the answer
/// needed is a set difference over what a listing just named, and reading every locator this deployment holds would be
/// the materialization the sweep exists to avoid.
/// </para>
/// <para>
/// All four payload kinds are asked at once, because an object gives no kind away. A key carries the kind as a segment
/// for a reader's sake, and nothing derives one from a key — so a sweep that trusted the segment would delete a
/// message the moment somebody renamed a group of keys.
/// </para>
/// <para>
/// The seam is here rather than in the application because both of its sides are storage: one store lists and the other
/// answers what it points at, and no use case above them has an opinion about either.
/// </para>
/// </remarks>
internal interface IContentObjectReferenceReader
{
    /// <summary>Reads which of the given keys a stored payload of any kind names.</summary>
    /// <param name="objectLocators">The keys a listing named, which is at most one page of them.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The subset a row points at, compared by exactly the text the row carries.</returns>
    Task<IReadOnlySet<string>> FindReferencedAsync(
        IReadOnlyCollection<string> objectLocators,
        CancellationToken cancellationToken);
}
