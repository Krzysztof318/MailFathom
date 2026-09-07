// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.DataEncryption;

namespace MailFathom.Infrastructure.Secrets.Database;

/// <summary>Composes the authenticated identity of one database-backed secret.</summary>
internal static class StoredSecretBinding
{
    /// <summary>Binds material to its user, row, name, and stored-secret purpose.</summary>
    internal static DataEncryptionBinding Create(
        MailUserId user,
        DatabaseSecretReference reference,
        SecretName name)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A stored secret belongs to a user, and the value names nobody.", nameof(user));
        }

        if (!reference.IsSpecified)
        {
            throw new ArgumentException("A stored secret binding requires a database reference.", nameof(reference));
        }

        if (!name.IsSpecified)
        {
            throw new ArgumentException("A stored secret binding requires the secret's declared name.", nameof(name));
        }

        // The GUIDs use their fixed 36-character D form and SecretName admits no slash, so this separator cannot
        // occur in any part and distinct user/reference/name triples cannot compose the same subject.
        return DataEncryptionBinding.Create(
            DataEncryptionPurpose.StoredSecret,
            $"{user.Value:D}/{reference.Id:D}/{name.Value}");
    }
}
