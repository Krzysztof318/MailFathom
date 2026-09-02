// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MailFathom.AppHost;

/// <summary>Provisions the synthetic Basic credential after the normal local host is ready.</summary>
/// <remarks>
/// The write goes through the existing administrative API rather than through persistence, so the same password policy,
/// hashing, audit, and ownership rules apply here as to an operator provisioning the credential. An existing credential
/// is left alone, which preserves a local rotation across restarts of the persistent database.
/// </remarks>
internal sealed partial class DevelopmentCredentialProvisioningWorker(
    ResourceNotificationService resourceNotifications,
    IHttpClientFactory httpClientFactory,
    EndpointReference healthEndpoint,
    EndpointReference adminEndpoint,
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

        var created = await provisioner.EnsureAsync(
            new Uri(healthAddress, "started"),
            adminAddress,
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
}
