// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Messages;

/// <summary>Where every place one run has read mail in is remembered, bounded, for as long as the run lasts.</summary>
/// <remarks>
/// <para>
/// Moving between two folders and back has to return to each of them, which is several places; starting the client
/// again has to return to the one somebody was in, which is one. This is the first of those, and it is deliberately
/// not the second: keeping every folder a run walked through in a store on somebody's disk would be writing a list of
/// their mailbox out to serve a value only one entry of it is ever read for.
/// </para>
/// <para>
/// It is bounded because a run has no other bound. Somebody walking a deeply nested mailbox visits as many places as
/// it has folders, and every one of them would otherwise be held for as long as the process lives.
/// </para>
/// <para>
/// Its own type rather than a field on the store, so the bound and the order it drops in are reachable by an ordinary
/// unit test — what is behind the store is a platform API no test can reach.
/// </para>
/// </remarks>
internal sealed class VisitedPlaces
{
    /// <summary>How many places are kept before the one written longest ago is dropped.</summary>
    /// <remarks>Enough that moving between the folders somebody actually works in never loses one, and bounded whatever their mailbox holds.</remarks>
    internal const int Maximum = 16;

    private readonly Lock guard = new();
    private readonly Dictionary<string, RememberedMessageList> remembered = new(StringComparer.Ordinal);
    private readonly List<string> inWriteOrder = [];

    /// <summary>Gets how many places are being remembered, which never passes <see cref="Maximum" />.</summary>
    internal int Count
    {
        get
        {
            lock (this.guard)
            {
                return this.inWriteOrder.Count;
            }
        }
    }

    /// <summary>Reads where a place was left in this run.</summary>
    /// <param name="placeKey">The place, as <see cref="MessagePlace.RememberedAs" /> composes it.</param>
    /// <returns>What was kept, or <see langword="null" /> where this run has not been there or has dropped it.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="placeKey" /> is <see langword="null" /> or empty.</exception>
    internal RememberedMessageList? Read(string placeKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(placeKey);

        lock (this.guard)
        {
            return this.remembered.GetValueOrDefault(placeKey);
        }
    }

    /// <summary>Keeps where a place is now, dropping the one written longest ago once the bound is reached.</summary>
    /// <param name="visited">The position and arrangement to keep.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="visited" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Writing a place again moves it to the newest end rather than leaving it where it first appeared, so the folder
    /// somebody keeps returning to is never the one dropped to make room for one they passed through once.
    /// </remarks>
    internal void Keep(RememberedMessageList visited)
    {
        ArgumentNullException.ThrowIfNull(visited);

        lock (this.guard)
        {
            if (this.remembered.Remove(visited.PlaceKey))
            {
                this.inWriteOrder.Remove(visited.PlaceKey);
            }

            this.remembered[visited.PlaceKey] = visited;
            this.inWriteOrder.Add(visited.PlaceKey);

            if (this.inWriteOrder.Count <= Maximum)
            {
                return;
            }

            this.remembered.Remove(this.inWriteOrder[0]);
            this.inWriteOrder.RemoveAt(0);
        }
    }
}
