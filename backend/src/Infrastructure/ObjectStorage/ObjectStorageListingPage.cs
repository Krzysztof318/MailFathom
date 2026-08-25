// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>One page of the objects held beneath this deployment's own key prefix.</summary>
/// <remarks>
/// <para>
/// A page rather than a listing, because a bucket holding a mailbox holds as many objects as the mailbox holds
/// messages and nothing may read that into memory. The reclamation that consumes this asks for one page, decides about
/// what is on it, and asks for the next one — so the largest thing it ever holds is the page.
/// </para>
/// <para>
/// The continuation is the endpoint's own opaque token rather than a key this system composed. It is what makes a
/// sweep resumable across attempts without the sweep having to know how the endpoint orders its keys.
/// </para>
/// </remarks>
/// <param name="Objects">The objects on this page, in the order the endpoint listed them.</param>
/// <param name="ContinuationToken">The token the next page is asked for with, or <see langword="null" /> when this page ended the listing.</param>
internal sealed record ObjectStorageListingPage(
    IReadOnlyList<ListedObject> Objects,
    string? ContinuationToken)
{
    /// <summary>Gets the page an endpoint holding nothing beneath the prefix answers with.</summary>
    public static ObjectStorageListingPage Empty { get; } = new([], ContinuationToken: null);
}

/// <summary>One object a listing named, described by what reclamation has to decide about it.</summary>
/// <remarks>
/// <para>
/// The three properties are exactly what the decision needs: the key to compare against the rows, the moment the
/// endpoint recorded the object at so a write still in flight is left alone, and the size so what a sweep freed can be
/// reported. Nothing here is logged — a key names one message.
/// </para>
/// <para>
/// <b>An absent moment is an object no age floor can clear.</b> Every comparison against the floor is lifted, so an
/// object whose age cannot be read fails the floor rather than passing it — which is the direction that keeps mail: an
/// unreadable moment on a payload still being written would otherwise be indistinguishable from one written last year.
/// The cost is an orphan such an endpoint never reclaims, which is a bucket to look at rather than mail lost.
/// </para>
/// </remarks>
/// <param name="Key">The whole key, exactly as a row would carry it.</param>
/// <param name="WrittenAt">When the endpoint recorded the object, or <see langword="null" /> where it reported no moment at all.</param>
/// <param name="ByteLength">How many bytes the object holds.</param>
internal readonly record struct ListedObject(string Key, DateTimeOffset? WrittenAt, long ByteLength);
