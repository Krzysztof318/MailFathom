// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>Stores or rotates material an owner's record will reach through a database reference.</summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class StoredSecretAdministration(
    AccessAuthorization authorization,
    IOwnerSettingsDocumentReader owners,
    IStoredSecretStore secrets,
    OptimisticConcurrencyRetryPolicy retry)
{
    /// <summary>Stores one named secret for an owner, retaining its reference when the name already exists.</summary>
    /// <param name="owner">The owner whose deletion removes the material.</param>
    /// <param name="name">The declared secret name, which is the stable rotation identity within the owner.</param>
    /// <param name="material">The caller-owned material, which is never retained.</param>
    /// <param name="cancellationToken">Cancels the owner read, sealing, or commit.</param>
    /// <returns>The outcome and the reference when material was stored.</returns>
    internal async Task<StoredSecretProvisioning> StoreAsync(
        MailOwnerId owner,
        SecretName name,
        ResolvedSecret material,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (!owner.IsSpecified)
        {
            throw new ArgumentException("A stored secret belongs to a named owner.", nameof(owner));
        }

        if (!name.IsSpecified)
        {
            throw new ArgumentException("A stored secret has a declared name.", nameof(name));
        }

        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        if (!secrets.CanStore)
        {
            return StoredSecretProvisioning.KeyRingUnavailable();
        }

        if (await owners.ReadAsync(owner, cancellationToken) is null)
        {
            return StoredSecretProvisioning.UnknownOwner();
        }

        var suggestedReference = DatabaseSecretReference.Create();
        var storedReference = await retry.CommitAsync(
            (session, token) => secrets.StoreAsync(
                session,
                suggestedReference,
                owner,
                name,
                material,
                token),
            cancellationToken);

        return StoredSecretProvisioning.Stored(storedReference);
    }
}
