// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Reads whether this deployment holds nobody, one user, or several, and refuses a deployment whose surfaces could not tell several apart.</summary>
/// <remarks>
/// <para>
/// It reads no user's record. A user is composed from their own record the first time something asks for them, so what
/// a start costs does not grow with the people a deployment serves; what it has to know before the listener is admitted
/// to is only how many there are, as far as telling one from several, because a caller naming no user acts for the sole
/// one. Whether any served account exists at all is read the same way — one row or none — so a deployment with
/// synchronization switched on and no mailbox to read says so.
/// </para>
/// <para>
/// <b>A deployment holding nobody is a state a start admits.</b> A fresh database holds no user, so a first start —
/// like one of a deployment whose every user was erased — finds no row, serves nobody, and says so. A user recorded
/// afterwards, through <c>mfctl</c> or the administrative routes behind it, is served without a restart.
/// </para>
/// <para>
/// It runs behind the schema gate, because it reads a table that migration creates, and ahead of the workers. It is not
/// ahead of the listener: the web host registers its own hosted service while the builder runs and therefore starts it
/// first, so the port is already open while this gate runs, and the startup probe is what reports the deployment
/// unstarted until it completes.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class ServedUsersStartupGate(
    ServedUsers servedUsers,
    IServedMailAccountReader servedAccounts,
    IConfiguration configuration,
    HostStartupGates startupGates,
    SeveralUserAdmission admission,
    ILogger<ServedUsersStartupGate> logger) : IHostedService
{
    /// <inheritdoc />
    /// <exception cref="DeploymentUserUnresolvedException">Thrown when this deployment holds several users while a surface admits a caller naming none.</exception>
    /// <remarks>
    /// <para>
    /// A user-facing surface answers one person about their own mail, so it may be served beside several users only
    /// where every caller it admits says which user it is acting for. The reading that decides that is shared with the
    /// provisioning this refusal is the start-time half of, because a deployment refused a second user over a route and
    /// one refused it at its next start are the same operator correcting the same setting.
    /// </para>
    /// <para>
    /// One user and none are both admissible, which is what lets a first run reach an unauthenticated surface with no
    /// credential and record the person it is for. The administrative surface is deliberately outside it, which is what
    /// makes a second user reachable at all: an administrator's acts are the deployment's rather than one person's.
    /// </para>
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var held = await servedUsers.CountAsync(cancellationToken);

        if (held > 1 && admission.AdmitsACallerNamingNoUser)
        {
            throw DeploymentUserUnresolvedException.SeveralUsersOnAUserFacingSurface(admission.Refusal);
        }

        if (held == 0)
        {
            this.LogNoUserHeld();
        }
        else if (MailSynchronizationOptions.IsEnabledIn(configuration)
            && (await servedAccounts.ReadServedVersionsAsync(after: null, limit: 1, cancellationToken)).Count == 0)
        {
            this.LogNothingToSynchronize();
        }

        startupGates.MarkCompleted(HostStartupGate.ServedUsers);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <remarks>Reached on the first start of a fresh database, which holds no user, and on any start of a deployment whose every user was erased.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment holds no user and therefore serves nobody. Record one with 'mfctl user add', then give them a mailbox with 'mfctl account add'.")]
    private partial void LogNoUserHeld();

    /// <remarks>A report rather than a refusal, because a deployment with the switch on and nothing recorded yet is the ordinary shape of a first run.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mail synchronization is switched on and no user this deployment serves is assigned a mail account, so there is nothing to synchronize. Record one with 'mfctl account add'.")]
    private partial void LogNothingToSynchronize();
}
