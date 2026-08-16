// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Mcp;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup what every configured credential entry may do, and what a surface with no entry grants.</summary>
/// <remarks>
/// <para>
/// A grant nobody wrote down reaches the whole of its surface, which is what makes a deployment work before it is
/// governed and is also the one thing about it an operator would otherwise never see. Reporting each entry's resolved
/// grant is what turns that default into a posture somebody chose: they meet it in the log on the first run rather than
/// inferring it later from what a credential turned out to be able to do.
/// </para>
/// <para>
/// Both endpoints are reported by one service and each entry separately, because the two surfaces draw from disjoint
/// halves of the vocabulary and an operator who narrowed one credential has to be able to read back that they narrowed
/// the one they meant. An entry is named by its configuration path, which is the position they would edit; nothing here
/// names a key, a public key, a token, an authorization server, or a subject, because a grant is what the deployment
/// wrote and never who presented something.
/// </para>
/// <para>
/// It records rather than warns, including for the surface that configures no entry at all. That posture is already a
/// warning — <see cref="McpTransportAuthenticationWarning" /> and <see cref="AdminTransportSecurityWarning" /> each say
/// what it means that anything reaching the address is served — and what this adds is the half those cannot state,
/// which is how much such a caller then holds. Saying it twice at warning level would make the second one noise and the
/// first one easier to scroll past.
/// </para>
/// <para>
/// It runs as a hosted service so it appears among the other startup diagnostics, and it is registered whether or not
/// either endpoint is enabled, because it is the report that decides whether it has anything to say.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class TransportGrantStartupReport : IHostedService
{
    private const string McpEndpointName = "MCP";

    private const string AdminEndpointName = "administrative";

    private const string NothingGranted = "nothing";

    private readonly McpEndpointOptions mcpEndpointSettings;
    private readonly AdminEndpointOptions adminEndpointSettings;
    private readonly ILogger<TransportGrantStartupReport> logger;

    /// <summary>Initializes a new startup report.</summary>
    /// <param name="mcpEndpointSettings">The MCP endpoint settings startup was composed from.</param>
    /// <param name="adminEndpointSettings">The administrative endpoint settings startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mcpEndpointSettings" /> or <paramref name="adminEndpointSettings" /> is <see langword="null" />.</exception>
    public TransportGrantStartupReport(
        IOptions<McpEndpointOptions> mcpEndpointSettings,
        IOptions<AdminEndpointOptions> adminEndpointSettings,
        ILogger<TransportGrantStartupReport> logger)
    {
        ArgumentNullException.ThrowIfNull(mcpEndpointSettings);
        ArgumentNullException.ThrowIfNull(adminEndpointSettings);

        this.mcpEndpointSettings = mcpEndpointSettings.Value;
        this.adminEndpointSettings = adminEndpointSettings.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.mcpEndpointSettings.Enabled)
        {
            this.Report(
                McpEndpointName,
                McpEndpointRoute.Path,
                McpEndpointOptions.SectionName,
                McpEndpointOptions.GrantedSurface,
                [.. this.mcpEndpointSettings.Authentication]);
        }

        if (this.adminEndpointSettings.Enabled)
        {
            this.Report(
                AdminEndpointName,
                AdminEndpointOptions.RoutePrefix,
                AdminEndpointOptions.SectionName,
                AdminEndpointOptions.GrantedSurface,
                [.. this.adminEndpointSettings.Authentication]);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>States what one endpoint's entries resolved to, or what a caller holds where it configures none.</summary>
    private void Report(
        string endpointName,
        string endpointPath,
        string sectionName,
        ProtectedSurface surface,
        IReadOnlyList<TransportAuthenticationOptions> methods)
    {
        var settingPath = $"{sectionName}:{TransportAuthenticationConfiguration.SettingName}";

        if (methods.Count == 0)
        {
            var wholeSurface = Describe(MailFathomPermission.PublishedFor(surface));

            this.LogSurfaceGrantedWithoutAnyEntry(endpointName, endpointPath, wholeSurface, settingPath);

            return;
        }

        foreach (var (index, method) in methods.Index())
        {
            var grant = Describe(method.GrantedPermissions(surface));
            var entryPath = $"{settingPath}:{index}";

            // The narrowing setting is asked first because it holds whether or not a list was written: an entry whose
            // sole block is OAuth may set it and state no ceiling, and what each token there holds is still its own
            // scopes rather than the whole surface the line would otherwise report.
            if (method.PermissionsFromTokenScopes)
            {
                this.LogEntryGrantNarrowedByTokenScopes(endpointName, entryPath, grant);
            }
            else if (method.GrantsTheWholeSurface)
            {
                this.LogEntryGrantedWithoutBeingNarrowed(endpointName, entryPath, grant);
            }
            else
            {
                this.LogEntryGrant(endpointName, entryPath, grant);
            }
        }
    }

    /// <summary>Renders a resolved grant for a log line, naming emptiness rather than printing nothing.</summary>
    /// <remarks>An empty list would otherwise read as a message that lost its argument, which is exactly the grant worth being unambiguous about: it is how a credential is retired without its entry being deleted.</remarks>
    private static string Describe(IReadOnlyList<MailFathomPermission> permissions) => permissions.Count == 0
        ? NothingGranted
        : string.Join(", ", permissions.Select(permission => permission.Name));

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint entry {EntrySettingPath} grants {GrantedPermissions} to every credential "
            + "it admits.")]
    private partial void LogEntryGrant(string endpointName, string entrySettingPath, string grantedPermissions);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint entry {EntrySettingPath} grants at most {GrantedPermissions}, and each "
            + "token holds whichever of those its own scopes carry.")]
    private partial void LogEntryGrantNarrowedByTokenScopes(
        string endpointName,
        string entrySettingPath,
        string grantedPermissions);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint entry {EntrySettingPath} writes down no grant, so every credential it "
            + "admits holds {GrantedPermissions} — everything this surface publishes. Write a 'Permissions' list on the "
            + "entry to narrow it, or an empty one to grant nothing.")]
    private partial void LogEntryGrantedWithoutBeingNarrowed(
        string endpointName,
        string entrySettingPath,
        string grantedPermissions);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint on {EndpointPath} configures no credential entry, so every caller it "
            + "serves holds {GrantedPermissions} — everything this surface publishes. There is no entry for a grant to "
            + "be written on until one is added under {AuthenticationSettingPath}.")]
    private partial void LogSurfaceGrantedWithoutAnyEntry(
        string endpointName,
        string endpointPath,
        string grantedPermissions,
        string authenticationSettingPath);
}
