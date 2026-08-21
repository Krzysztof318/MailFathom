// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>Represents a positive IMAP UID within one UIDVALIDITY scope.</summary>
public readonly record struct ImapUid
{
    private ImapUid(uint value) => this.Value = value;

    /// <summary>Gets the UID value.</summary>
    public uint Value { get; }

    /// <summary>Creates a validated IMAP UID.</summary>
    /// <param name="value">The server-provided UID.</param>
    /// <returns>A validated UID.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value" /> is zero.</exception>
    public static ImapUid Create(uint value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);

        return new ImapUid(value);
    }
}
