// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Chunking;

/// <summary>Identifies one persisted passage of a message.</summary>
/// <remarks>
/// A derived chunk carries no identifier — it is a pure function of the text and the rules, and
/// <see cref="EmailChunkContentHash" /> is what decides whether two derivations are the same passage. This is what a
/// passage is called once it has been written down, so that a vector can be attributed to it without a caller passing
/// bare UUIDs across a port.
/// </remarks>
public readonly record struct EmailChunkId
{
    private EmailChunkId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a chunk identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated chunk identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static EmailChunkId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An email chunk identifier cannot be empty.", nameof(value));
        }

        return new EmailChunkId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
