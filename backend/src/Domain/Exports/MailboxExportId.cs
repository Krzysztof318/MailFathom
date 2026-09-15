// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Exports;

/// <summary>Identifies one export of a mailbox, which is what a caller follows, downloads, cancels, and deletes by.</summary>
public readonly record struct MailboxExportId
{
    private MailboxExportId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an export identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated export identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static MailboxExportId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A mailbox export identifier cannot be empty.", nameof(value));
        }

        return new MailboxExportId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
