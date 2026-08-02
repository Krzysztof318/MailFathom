// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>Represents an IMAP UIDVALIDITY value for a folder.</summary>
public readonly record struct ImapUidValidity
{
    private ImapUidValidity(uint value) => this.Value = value;

    /// <summary>Gets the UIDVALIDITY value.</summary>
    public uint Value { get; }

    /// <summary>Creates a validated UIDVALIDITY value.</summary>
    /// <param name="value">The server-provided UIDVALIDITY.</param>
    /// <returns>A validated UIDVALIDITY value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value" /> is zero.</exception>
    public static ImapUidValidity Create(uint value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);

        return new ImapUidValidity(value);
    }
}
