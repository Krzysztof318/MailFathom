// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    /// <summary>Reads an alias a caller outside this system wrote, without raising on text that is not one.</summary>
    /// <param name="value">The text the caller wrote.</param>
    /// <param name="alias">The alias when the text is one; otherwise the struct default.</param>
    /// <returns><see langword="true" /> when the text is an alias.</returns>
    /// <remarks>
    /// <see cref="Create" /> raises because configuration is refused rather than negotiated with, which is the wrong
    /// shape at a boundary an operator types into: an administrative route reading a request body owes a stated refusal
    /// rather than a failure the process reports as its own. Both admit exactly the same text, so an alias that reaches
    /// one reaches the other.
    /// </remarks>
    public static bool TryCreate(string? value, out MailFolderAlias alias)
    {
        // Trimmed before the control characters are looked for, in that order, because that is the order
        // Create applies and a tab is both a control character and whitespace: checking the untrimmed text would
        // refuse padding Create accepts, which is the one way these two could disagree about what an alias is.
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Any(char.IsControl))
        {
            alias = default;

            return false;
        }

        alias = Create(value);

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
