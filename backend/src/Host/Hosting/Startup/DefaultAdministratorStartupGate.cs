// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.DefaultAdministrator;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Records the default administrator on a deployment's first start, applies its password setting once, and reports what an operator has to act on.</summary>
/// <remarks>
/// <para>
/// It runs behind the schema gate, because it writes tables that migration creates, and under the process identity,
/// because nobody has signed in yet. Every replica of a deployment starting at once runs it, and only one of them
/// writes: <see cref="IDefaultAdministratorStore" /> claims a row every replica contends for.
/// </para>
/// <para>
/// A start it refuses is one applying a password setting that fails the password policy, or whose username <c>admin</c>
/// belongs to another user's credential. Both are the operator's to correct before anybody can administer the
/// deployment, so neither is left for a sign-in to discover; a setting already applied is ignored and refuses nothing.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class DefaultAdministratorStartupGate(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IOptions<AdminEndpointOptions> adminEndpointSettings,
    IOptions<ReverseProxyOptions> reverseProxySettings,
    RecordedDefaultAdministrator recorded,
    HostStartupGates startupGates,
    ILogger<DefaultAdministratorStartupGate> logger) : IHostedService
{
    /// <summary>The environment variable carrying the password the default administrator is given on the first start that finds it.</summary>
    internal const string PasswordVariableName = "MAILFATHOM_ADMIN_PASSWORD";

    /// <inheritdoc />
    /// <exception cref="DefaultAdministratorUnusableException">Thrown when the password setting fails the policy, or the username belongs to another user.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var passwordSetting = configuration[PasswordVariableName];
        var start = await scope.ServiceProvider
            .GetRequiredService<DefaultAdministratorBootstrap>()
            .StartAsync(passwordSetting, PasswordVariableName, cancellationToken);

        recorded.Record(start.Administrator);
        this.Report(start, settingPresent: !string.IsNullOrEmpty(passwordSetting));

        if (!reverseProxySettings.Value.ForwardsTheClientAddress()
            && await scope.ServiceProvider
                .GetRequiredService<IUserCredentialStore>()
                .AnyRestrictedToSourceNetworksAsync(cancellationToken))
        {
            this.LogSourceNetworksJudgedAgainstThePeer();
        }

        startupGates.MarkCompleted(HostStartupGate.DefaultAdministrator);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Report(DefaultAdministratorStart start, bool settingPresent)
    {
        var endpoint = adminEndpointSettings.Value;

        if (start.Administrator is null)
        {
            if (endpoint.Enabled && !endpoint.RequiresAuthentication)
            {
                this.LogRemovedAdministratorLeavesTheEndpointUnserved();
            }

            return;
        }

        switch (start.PasswordSetting)
        {
            case DefaultAdministratorPasswordOutcome.Applied:
                this.LogPasswordApplied();
                break;
            case DefaultAdministratorPasswordOutcome.AlreadyHeld:
                this.LogPasswordAlreadyHeld();
                break;
            case null when settingPresent:
                this.LogPasswordSettingIgnored();
                break;
        }

        if (start.SignsInWithShippedPassword)
        {
            this.LogShippedPasswordStillSet();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The default administrator 'admin' was given the password " + PasswordVariableName + " carried. Later starts ignore the setting; change the password with 'mfctl credential rotate'.")]
    private partial void LogPasswordApplied();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The default administrator 'admin' already holds a password credential, so " + PasswordVariableName + " was recorded as applied and changed nothing.")]
    private partial void LogPasswordAlreadyHeld();

    /// <remarks>Information rather than a warning: the shipped assets keep setting it, and a deployment that changed the password since is the one this reports.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = PasswordVariableName + " is ignored: it was applied on an earlier start, and it never changes a password afterwards.")]
    private partial void LogPasswordSettingIgnored();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The default administrator 'admin' still signs in with the password every copy of the deployment assets ships with. Change it with 'mfctl credential rotate'.")]
    private partial void LogShippedPasswordStillSet();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The administrative endpoint authenticates nobody and serves its callers as the default administrator 'admin', which this deployment removed. It serves no caller until it names an authentication method.")]
    private partial void LogRemovedAdministratorLeavesTheEndpointUnserved();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A credential is restricted to source networks while ReverseProxy names no proxy narrower than an address family, so each restriction is judged against the address that opened the connection. Behind a proxy that is the proxy's own, which every client shares; name the proxy under ReverseProxy:TrustedProxies.")]
    private partial void LogSourceNetworksJudgedAgainstThePeer();
}
