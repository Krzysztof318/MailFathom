// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailMcp.Domain.Folders;

/// <summary>Counts how many times an alias has been bound to a remote folder.</summary>
/// <remarks>
/// The generation is what keeps a repointed alias safe. UIDVALIDITY is unique inside one mailbox and says nothing
/// across mailboxes, so two unrelated remote folders can advertise the same value. An alias that moved between them
/// while keeping one persistence identity would let the previous folder's checkpoint apply to the new one and skip
/// every message below its last-seen UID, silently and permanently. Each binding therefore synchronizes under its own
/// generation, with its own checkpoint and its own stored occurrences.
/// </remarks>
public readonly record struct MailFolderResolutionGeneration
{
    private MailFolderResolutionGeneration(int value) => this.Value = value;

    /// <summary>Gets the generation the first binding of an alias runs under.</summary>
    public static MailFolderResolutionGeneration First => new(1);

    /// <summary>Gets the generation number.</summary>
    public int Value { get; }

    /// <summary>Creates a generation from a persisted value.</summary>
    /// <param name="value">The stored generation number.</param>
    /// <returns>A validated generation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value" /> is not positive.</exception>
    public static MailFolderResolutionGeneration Create(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

        return new MailFolderResolutionGeneration(value);
    }

    /// <summary>Starts the generation that a new remote binding of the same alias synchronizes under.</summary>
    /// <returns>The next generation.</returns>
    public MailFolderResolutionGeneration Next() => new(checked(this.Value + 1));

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
