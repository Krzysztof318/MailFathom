// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MailFathom.AppHost;

/// <summary>Records the local mailbox and provisions the synthetic Basic credential once the normal local host is ready.</summary>
/// <remarks>
/// Both writes go through the existing administrative API rather than through persistence, so the same validation,
/// password policy, hashing, audit, and ownership rules apply here as to an operator performing them. Each is skipped
/// where the deployment already holds it, which preserves a local rotation, and a mailbox an operator edited, across
/// restarts of the persistent database.
/// </remarks>
internal sealed partial class DevelopmentCredentialProvisioningWorker(
    ResourceNotificationService resourceNotifications,
    IHttpClientFactory httpClientFactory,
    EndpointReference healthEndpoint,
    EndpointReference adminEndpoint,
    ParameterResource mailAccountHost,
    ParameterResource mailAccountUserName,
    ParameterResource mailAccountPassword,
    TimeProvider timeProvider,
    ILogger<DevelopmentCredentialProvisioningWorker> logger) : BackgroundService
{
    internal const string HttpClientName = "mailfathom-development-provisioning";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await resourceNotifications.WaitForResourceHealthyAsync(
            OrchestrationContract.HostResourceName,
            WaitBehavior.StopOnResourceUnavailable,
            stoppingToken);

        var healthAddress = await ResolveHttpAddressAsync(healthEndpoint, stoppingToken);
        var adminAddress = await ResolveHttpAddressAsync(adminEndpoint, stoppingToken);
        using var client = httpClientFactory.CreateClient(HttpClientName);
        var provisioner = new DevelopmentCredentialProvisioner(client, timeProvider);

        // A user is recorded before anything is declared for them, and never by this worker: a fresh database holds
        // nobody until the developer records somebody, and this run provisions for them on the next launch.
        if (await provisioner.WaitForSoleServedUserAsync(new Uri(healthAddress, "started"), adminAddress, stoppingToken)
            is not { } user)
        {
            NoUserToProvisionFor(logger);

            return;
        }

        // The mailbox first, because it is what the deployment exists to read: a credential provisioned against a
        // record declaring no account would sign a client in to an empty deployment.
        var declared = await provisioner.EnsureMailAccountAsync(
            adminAddress,
            user,
            await RequiredValueAsync(mailAccountHost, stoppingToken),
            await RequiredValueAsync(mailAccountUserName, stoppingToken),
            await RequiredValueAsync(mailAccountPassword, stoppingToken),
            stoppingToken);

        if (declared)
        {
            MailAccountDeclared(logger, OrchestrationContract.DevelopmentMailAccountId);
        }
        else
        {
            MailAccountAlreadyDeclared(logger, OrchestrationContract.DevelopmentMailAccountId);
        }

        var created = await provisioner.EnsureCredentialAsync(
            adminAddress,
            user,
            OrchestrationContract.DevelopmentBasicUsername,
            OrchestrationContract.DevelopmentBasicPassword,
            stoppingToken);

        if (created)
        {
            CredentialProvisioned(logger, OrchestrationContract.DevelopmentBasicUsername);
        }
        else
        {
            CredentialAlreadyExists(logger, OrchestrationContract.DevelopmentBasicUsername);
        }
    }

    private static async Task<string> RequiredValueAsync(
        ParameterResource parameter,
        CancellationToken cancellationToken) =>
        await parameter.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException($"The {parameter.Name} parameter was not supplied.");

    private static async Task<Uri> ResolveHttpAddressAsync(
        EndpointReference endpoint,
        CancellationToken cancellationToken)
    {
        var resolvedAddress = await endpoint.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException($"The {endpoint.EndpointName} endpoint was not allocated.");
        var allocatedAddress = new Uri(resolvedAddress, UriKind.Absolute);

        return new UriBuilder(allocatedAddress) { Scheme = Uri.UriSchemeHttp }.Uri;
    }

    [LoggerMessage(1, LogLevel.Information, "Provisioned the local Basic credential named {Username}.")]
    private static partial void CredentialProvisioned(ILogger logger, string username);

    [LoggerMessage(2, LogLevel.Information, "The local Basic credential named {Username} already exists and was left unchanged.")]
    private static partial void CredentialAlreadyExists(ILogger logger, string username);

    [LoggerMessage(3, LogLevel.Information, "Declared the mail account {AccountId} in the local user's record.")]
    private static partial void MailAccountDeclared(ILogger logger, string accountId);

    [LoggerMessage(4, LogLevel.Information, "The local user's record already declares the mail account {AccountId} and was left unchanged.")]
    private static partial void MailAccountAlreadyDeclared(ILogger logger, string accountId);

    [LoggerMessage(5, LogLevel.Warning, "The local host holds no user, so no mailbox or credential was provisioned. Record one with 'mfctl user add', then restart the AppHost.")]
    private static partial void NoUserToProvisionFor(ILogger logger);
}
