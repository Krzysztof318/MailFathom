// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Database;

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>The outcome of an administrative stored-secret write and its reference on success.</summary>
/// <param name="Outcome">What the write did.</param>
/// <param name="Reference">The stable reference when the write committed; otherwise the unspecified default.</param>
internal sealed record StoredSecretProvisioning(
    StoredSecretProvisioningOutcome Outcome,
    DatabaseSecretReference Reference)
{
    internal static StoredSecretProvisioning Stored(DatabaseSecretReference reference) =>
        new(StoredSecretProvisioningOutcome.Stored, reference);

    internal static StoredSecretProvisioning UnknownOwner() =>
        new(StoredSecretProvisioningOutcome.UnknownOwner, default);

    internal static StoredSecretProvisioning KeyRingUnavailable() =>
        new(StoredSecretProvisioningOutcome.KeyRingUnavailable, default);
}
