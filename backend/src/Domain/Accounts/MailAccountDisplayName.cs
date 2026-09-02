// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Accounts;

/// <summary>Names a configured mail account the way a person reading an answer about it recognizes it.</summary>
/// <remarks>
/// <para>
/// It stands beside <see cref="MailAccountId" /> rather than replacing it. The identifier is the stable identity every
/// stored row, cursor, and log line is expressed in, and it survives a rename of the mailbox it points at; this is the
/// text published to a caller so that "which mailbox is this" has an answer somebody other than the operator can read.
/// </para>
/// <para>
/// The operator's casing is kept rather than normalized away, because the value exists to be read and
/// <c>Work mail</c> is not <c>WORK MAIL</c> to the person who wrote it. Matching it is therefore a case-insensitive
/// comparison at the point of use rather than a canonical form stored here, which is the opposite of the decision
/// <see cref="Folders.MailFolderAlias" /> takes — an alias is compared inside a database whose
/// collation MailFathom does not control, and this value is compared only in process.
/// </para>
/// </remarks>
public readonly record struct MailAccountDisplayName
{
    /// <summary>The greatest length a display name may carry.</summary>
    /// <remarks>
    /// Generous against any name written to be read at a glance, and short enough that the value cannot become a way to
    /// place a paragraph of operator-chosen text into every result that names the account.
    /// </remarks>
    public const int MaximumLength = 128;

    private MailAccountDisplayName(string value) => this.Value = value;

    /// <summary>Gets the display name as the operator wrote it, trimmed.</summary>
    public string Value { get; }

    /// <summary>Creates a display name from configuration-owned text.</summary>
    /// <param name="value">The configured display name.</param>
    /// <returns>A validated display name, trimmed and otherwise as written.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank, longer than <see cref="MaximumLength" />, or contains a control character.</exception>
    /// <remarks>
    /// Control characters are refused because the value is published in every result that names its account and is
    /// written to operator-facing messages; a newline in it would let configuration write arbitrary lines into both.
    /// </remarks>
    public static MailAccountDisplayName Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();

        if (trimmed.Length > MaximumLength)
        {
            throw new ArgumentException($"An account display name cannot be longer than {MaximumLength} characters.", nameof(value));
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("An account display name cannot contain control characters.", nameof(value));
        }

        return new MailAccountDisplayName(trimmed);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
