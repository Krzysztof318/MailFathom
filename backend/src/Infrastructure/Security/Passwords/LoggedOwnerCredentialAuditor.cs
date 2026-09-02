// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Security.Passwords;

/// <summary>Writes credential changes to the structured log as the deployment's audit record.</summary>
/// <remarks>
/// <para>
/// One line per act, at a level that says how much it matters: provisioning and enabling are information, and rotating,
/// disabling, and deleting are warnings, because those three are what an operator looks for when somebody reports that
/// a sign-in stopped working. A durable audit store replaces this implementation without any caller changing.
/// </para>
/// <para>
/// What every line carries is the act, the credential's identifier, the owner's identifier, the administrator the
/// request was admitted as, and the instant. What no line carries is the username or anything derived from the
/// password — the identifier is the handle, and resolving it to a username is a listing only somebody entitled to read
/// one can take.
/// </para>
/// </remarks>
internal sealed partial class LoggedOwnerCredentialAuditor(ILogger<LoggedOwnerCredentialAuditor> logger)
    : IOwnerCredentialAuditor
{
    /// <inheritdoc />
    public Task RecordCredentialChangeAsync(OwnerCredentialChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        switch (change.Act)
        {
            case OwnerCredentialAct.Provisioned:
            case OwnerCredentialAct.Enabled:
                this.LogCredentialOpened(
                    change.Act,
                    change.CredentialId,
                    change.Owner.Value,
                    change.ActingAdministrator,
                    change.OccurredAt);

                break;

            default:
                this.LogCredentialWithdrawn(
                    change.Act,
                    change.CredentialId,
                    change.Owner.Value,
                    change.ActingAdministrator,
                    change.OccurredAt);

                break;
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Owner credential {CredentialId} of owner {OwnerId} was {CredentialAct} by {ActingAdministrator} at "
            + "{OccurredAt}. The username it carries is deliberately not recorded; read it from the owner's credential "
            + "listing.")]
    private partial void LogCredentialOpened(
        OwnerCredentialAct credentialAct,
        Guid credentialId,
        Guid ownerId,
        string actingAdministrator,
        DateTimeOffset occurredAt);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Owner credential {CredentialId} of owner {OwnerId} was {CredentialAct} by {ActingAdministrator} at "
            + "{OccurredAt}, so whatever was signing in with it stops at that instant. The username it carried is "
            + "deliberately not recorded; read it from the owner's credential listing where the credential still "
            + "exists.")]
    private partial void LogCredentialWithdrawn(
        OwnerCredentialAct credentialAct,
        Guid credentialId,
        Guid ownerId,
        string actingAdministrator,
        DateTimeOffset occurredAt);
}
