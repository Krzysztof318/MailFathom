// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>Stores or rotates material a user's record will reach through a database reference.</summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class StoredSecretAdministration(
    AccessAuthorization authorization,
    IUserSettingsDocumentReader users,
    IStoredSecretStore secrets,
    OptimisticConcurrencyRetryPolicy retry)
{
    /// <summary>Stores one named secret for a user, retaining its reference when the name already exists.</summary>
    /// <param name="user">The user whose deletion removes the material.</param>
    /// <param name="name">The declared secret name, which is the stable rotation identity within the user.</param>
    /// <param name="material">The caller-owned material, which is never retained.</param>
    /// <param name="cancellationToken">Cancels the user read, sealing, or commit.</param>
    /// <returns>The outcome and the reference when material was stored.</returns>
    internal async Task<StoredSecretProvisioning> StoreAsync(
        MailUserId user,
        SecretName name,
        ResolvedSecret material,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (!user.IsSpecified)
        {
            throw new ArgumentException("A stored secret belongs to a named user.", nameof(user));
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

        if (await users.ReadAsync(user, cancellationToken) is null)
        {
            return StoredSecretProvisioning.UnknownUser();
        }

        var suggestedReference = DatabaseSecretReference.Create();
        var storedReference = await retry.CommitAsync(
            (session, token) => secrets.StoreAsync(
                session,
                suggestedReference,
                user,
                name,
                material,
                token),
            cancellationToken);

        return StoredSecretProvisioning.Stored(storedReference);
    }
}
