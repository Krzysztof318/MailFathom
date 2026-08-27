// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation.Mailboxes;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A stand-in for the platform store the tree's arrangement outlives a run in.</summary>
/// <remarks>
/// What is behind the real one is <c>ApplicationData.LocalSettings</c>, which no unit test can reach. Holding the last
/// value in memory is enough to assert both halves of what the store is for: what a run keeps, and what the next one
/// opens on.
/// </remarks>
internal sealed class StubMailboxTreeMemory : IMailboxTreeMemory
{
    /// <summary>Builds a store already holding what a previous run left, or holding nothing.</summary>
    /// <param name="remembered">What the next read answers with.</param>
    internal StubMailboxTreeMemory(RememberedMailboxes? remembered = null) =>
        this.Remembered = remembered ?? RememberedMailboxes.Nothing;

    /// <summary>Gets what the store holds right now.</summary>
    internal RememberedMailboxes Remembered { get; private set; }

    /// <summary>Gets how many times the store was written to.</summary>
    internal int Writes { get; private set; }

    /// <inheritdoc />
    public RememberedMailboxes Read() => this.Remembered;

    /// <inheritdoc />
    public void Write(RememberedMailboxes remembered)
    {
        this.Remembered = remembered;
        this.Writes++;
    }
}
