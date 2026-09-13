// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.StoredFiles;

/// <summary>Names one binary file a user supplied, which a record links to rather than holding the octets itself.</summary>
/// <remarks>
/// The deployment mints it when the file is written, and nothing about the file or its owner determines it, which is
/// what lets a record link to a file written before the record changed. Being a struct, <see langword="default" /> is
/// reachable and names no file; it reports itself through <see cref="IsSpecified" />.
/// </remarks>
public readonly record struct StoredFileId
{
    private StoredFileId(Guid value) => this.Value = value;

    /// <summary>Gets the identifier the file's row is keyed by.</summary>
    public Guid Value { get; }

    /// <summary>Gets whether this value names a file rather than being the unusable struct default.</summary>
    public bool IsSpecified => this.Value != Guid.Empty;

    /// <summary>Names a file by the identifier the deployment minted for it.</summary>
    /// <param name="value">The identifier.</param>
    /// <returns>The file's name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is the empty identifier.</exception>
    public static StoredFileId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A stored file is named by the identifier minted for it, and the empty identifier names none.", nameof(value));
        }

        return new StoredFileId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString("D");
}
