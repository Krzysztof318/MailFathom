// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Identifies a folder MailFathom keeps for an account whose mailbox it holds, for the folder's whole life.</summary>
/// <remarks>Renaming or moving the folder changes nothing about it, which is why arrivals and messages refer to it rather than to its path.</remarks>
public readonly record struct LocalMailFolderId
{
    private LocalMailFolderId(Guid value) => this.Value = value;

    /// <summary>Gets the identifier.</summary>
    public Guid Value { get; }

    /// <summary>Wraps a stored identifier.</summary>
    /// <param name="value">The identifier.</param>
    /// <returns>The folder identity.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static LocalMailFolderId Create(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("A local folder is identified by a non-empty identifier.", nameof(value))
            : new LocalMailFolderId(value);

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
