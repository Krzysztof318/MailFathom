// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation.Messages;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A stand-in for where a list's position outlives the folder being left and the run itself.</summary>
/// <remarks>
/// Half of the real one is <c>ApplicationData.LocalSettings</c>, which no unit test can reach; the other half is the
/// run's own bounded map, which <c>VisitedPlaces</c> owns and its own tests cover. Holding every place in memory here
/// is what a test of the list needs of both: what a run keeps, and what a return to a folder opens on.
/// </remarks>
internal sealed class StubMessageListMemory : IMessageListMemory
{
    private readonly Dictionary<string, RememberedMessageList> remembered = new(StringComparer.Ordinal);

    /// <summary>Builds a store already holding what previous runs left, or holding nothing.</summary>
    /// <param name="remembered">What a read of each of those places answers with.</param>
    internal StubMessageListMemory(params RememberedMessageList[] remembered)
    {
        foreach (var place in remembered)
        {
            this.remembered[place.PlaceKey] = place;
        }
    }

    /// <summary>Gets everything the store was asked to keep, in order.</summary>
    internal List<RememberedMessageList> Written { get; } = [];

    /// <inheritdoc />
    public RememberedMessageList Read(string placeKey) =>
        this.remembered.GetValueOrDefault(placeKey) ?? RememberedMessageList.Nothing(placeKey);

    /// <inheritdoc />
    public void Write(RememberedMessageList remembered)
    {
        this.remembered[remembered.PlaceKey] = remembered;
        this.Written.Add(remembered);
    }
}
