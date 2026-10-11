// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Preferences;

/// <summary>Reads and replaces the signed-in person's own client preferences.</summary>
/// <remarks>
/// <para>
/// Whose preferences these are comes from the principal rather than from the request, exactly as it does for the user
/// record: there is no argument here for another user's identifier, so a request about somebody else is something a
/// caller cannot express rather than something a surface has to refuse.
/// </para>
/// <para>
/// Reading is admitted under <see cref="MailFathomPermission.ProfileRead" /> and the write under
/// <see cref="MailFathomPermission.ProfileWrite" />. The write is deliberately not
/// <see cref="MailFathomPermission.MailAccountsWrite" />: that grant decides what this deployment reads out of
/// somebody's mailboxes, somebody whose mail configuration an administrator maintains in full does not hold it, and
/// hanging a person's own telemetry switch on it would let a grant they do not hold over their mail configuration
/// decide what may be said about them. Nor is it an administrative grant, since nothing here reaches beyond the caller.
/// </para>
/// <para>
/// A read answers a document whatever the deployment holds, because a person who has set nothing wants the defaults
/// drawn rather than an error. A write reports whether there was a user to write for, which is the one case the two
/// differ on and is a user erased under a credential that has not yet been withdrawn.
/// </para>
/// </remarks>
public sealed class OwnClientPreferences
{
    private readonly AccessAuthorization authorization;
    private readonly IClientPreferencesStore store;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the user it acts for.</param>
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
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ProfileRead" />.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the stored row is not a document of preferences.</exception>
    public async Task<ClientPreferences> ReadAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ProfileRead);

        var stored = await this.store.ReadAsync(this.authorization.RequireUser(), cancellationToken);

        return stored ?? ClientPreferences.Unset;
    }

    /// <summary>Reads the answer the signed-in person stored for the telemetry switch, for a write that carries it forward rather than deciding it.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The stored answer, or the unset one where they have set nothing.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ProfileWrite" />.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the stored row is not a document of preferences.</exception>
    /// <remarks>
    /// Asked under the writing grant, because it is a step of the write: the document is replaced whole, so a write
    /// that may not decide the switch has to state the answer already stored. No permission implies another, and a
    /// caller holding the writing grant alone would otherwise be refused an ordinary save by a read they never asked
    /// for. It hands back the one value such a write restates and nothing else of the document.
    /// </remarks>
    public async Task<bool> ReadTelemetryAnswerToCarryAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ProfileWrite);

        var stored = await this.store.ReadAsync(this.authorization.RequireUser(), cancellationToken);

        return (stored ?? ClientPreferences.Unset).TelemetryEnabled;
    }

    /// <summary>Replaces what the signed-in person set about their own client.</summary>
    /// <param name="preferences">The whole set, since the document is closed and a write states all of it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the write landed, and <see langword="false" /> when this deployment holds no record for the caller.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="preferences" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ProfileWrite" />.</exception>
    public Task<bool> SaveAsync(ClientPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        this.authorization.RequirePermission(MailFathomPermission.ProfileWrite);

        return this.store.SaveAsync(this.authorization.RequireUser(), preferences, cancellationToken);
    }
}
