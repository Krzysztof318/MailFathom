// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Names a mailbox folder the way MailFathom and its operator refer to it.</summary>
/// <remarks>
/// The alias is owned by MailFathom rather than by the mail server: it appears in configuration, in logs, and in future
/// MCP filters, and it keeps its meaning when the server renames or recreates the folder behind it. The path the
/// server advertises is <see cref="RemoteFolderPath" /> and is never used in its place.
/// </remarks>
public readonly record struct MailFolderAlias
{
    private MailFolderAlias(string value) => this.Value = value;

    /// <summary>Gets the normalized alias.</summary>
    public string Value { get; }

    /// <summary>Creates an alias from configuration-owned text.</summary>
    /// <param name="value">The configured alias.</param>
    /// <returns>A validated alias, trimmed and upper-cased.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank or contains a control character.</exception>
    /// <remarks>
    /// Casing is normalized rather than compared away, so the same alias is one value everywhere it is read, written,
    /// or queried — including in a database whose collation MailFathom does not control. Upper case is the canonical
    /// form because it round-trips in every culture, which is also why an operator who recased an alias in
    /// configuration does not silently create a second binding of it.
    /// </remarks>
    public static MailFolderAlias Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();
        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("A folder alias cannot contain control characters.", nameof(value));
        }

        return new MailFolderAlias(trimmed.ToUpperInvariant());
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
