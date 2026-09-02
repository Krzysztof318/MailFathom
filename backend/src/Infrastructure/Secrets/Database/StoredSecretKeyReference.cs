// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Secrets.Database;

/// <summary>Names one stored secret still sealed under a data-encryption key.</summary>
/// <param name="Reference">The reference a document carries.</param>
/// <param name="Owner">The subject whose deletion removes the secret.</param>
/// <param name="Name">The safe declared name used for rotation and audit.</param>
public sealed record StoredSecretKeyReference(
    DatabaseSecretReference Reference,
    MailOwnerId Owner,
    SecretName Name);
