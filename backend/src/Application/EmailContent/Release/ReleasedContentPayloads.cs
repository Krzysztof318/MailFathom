// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Release;

/// <summary>How many retained database copies one bounded release freed, and how much they were holding.</summary>
/// <remarks>
/// Two figures rather than one, for the reason the move's backlog carries two: the count says how much of the walk is
/// behind the operator and the volume says what the database stopped carrying, and a mailbox of ten thousand
/// notifications and one of two hundred messages with attachments are the same job by the first and nothing alike by the
/// second. The volume is the whole point of the operation, so reporting only the count would hide what it achieved.
/// </remarks>
/// <param name="PayloadCount">How many payloads had their retained database copy freed.</param>
/// <param name="ByteCount">How many bytes of raw MIME those copies were holding.</param>
public sealed record ReleasedContentPayloads(long PayloadCount, long ByteCount)
{
    /// <summary>What a release that freed nothing reports.</summary>
    public static ReleasedContentPayloads None { get; } = new(0, 0);

    /// <summary>Adds what another payload kind's batch freed to this one.</summary>
    /// <param name="other">What that batch freed.</param>
    /// <returns>The two figures added together.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other" /> is <see langword="null" />.</exception>
    public ReleasedContentPayloads Add(ReleasedContentPayloads other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new ReleasedContentPayloads(
            this.PayloadCount + other.PayloadCount,
            this.ByteCount + other.ByteCount);
    }
}
