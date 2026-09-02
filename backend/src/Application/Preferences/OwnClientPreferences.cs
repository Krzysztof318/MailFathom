// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Preferences;

/// <summary>Reads and replaces the signed-in person's own client preferences.</summary>
/// <remarks>
/// <para>
/// Whose preferences these are comes from the principal rather than from the request, exactly as it does for the owner
/// record: there is no argument here for another owner's identifier, so a request about somebody else is something a
/// caller cannot express rather than something a surface has to refuse.
/// </para>
/// <para>
/// Both acts are admitted under <see cref="MailFathomPermission.MailRead" />, which is the grant a signed-in person
/// already holds, and neither adds a name to the published set. The write is deliberately not
/// <see cref="MailFathomPermission.MailAccountsWrite" />: that grant decides which mailboxes this deployment connects
/// to, an administrator maintains it for somebody whose accounts are declared in the deployment's own files, and
/// hanging a person's own telemetry switch on it would let a grant they do not hold over their mail configuration
/// decide what may be said about them. Nor is it an administrative grant, since nothing here reaches beyond the caller.
/// </para>
/// <para>
/// A read answers a document whatever the deployment holds, because a person who has set nothing wants the defaults
/// drawn rather than an error. A write reports whether there was an owner to write for, which is the one case the two
/// differ on and is an owner erased under a credential that has not yet been withdrawn.
/// </para>
/// </remarks>
public sealed class OwnClientPreferences
{
    private readonly AccessAuthorization authorization;
    private readonly IClientPreferencesStore store;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the owner it acts for.</param>
    /// <param name="store">Holds one person's preferences.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnClientPreferences(AccessAuthorization authorization, IClientPreferencesStore store)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(store);

        this.authorization = authorization;
        this.store = store;
    }

    /// <summary>Reads what the signed-in person set about their own client.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What they set, or <see cref="ClientPreferences.Unset" /> where they have set nothing.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no owner, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the stored row is not a document of preferences.</exception>
    public async Task<ClientPreferences> ReadAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var stored = await this.store.ReadAsync(this.authorization.RequireOwner(), cancellationToken);

        return stored ?? ClientPreferences.Unset;
    }

    /// <summary>Replaces what the signed-in person set about their own client.</summary>
    /// <param name="preferences">The whole set, since the document is closed and a write states all of it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the write landed, and <see langword="false" /> when this deployment holds no record for the caller.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="preferences" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no owner, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public Task<bool> SaveAsync(ClientPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return this.store.SaveAsync(this.authorization.RequireOwner(), preferences, cancellationToken);
    }
}
