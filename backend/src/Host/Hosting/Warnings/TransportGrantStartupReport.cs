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
/// Every configured endpoint is reported by one service and each entry separately, because an operator who narrowed one
/// credential has to be able to read back that they narrowed the one they meant. Two of the three surfaces draw from
/// the same half of the vocabulary, which is what makes the endpoint each line names the part that cannot be inferred.
/// An entry is named by its configuration path, which is the position they would edit; nothing here
/// names a key, a public key, a token, an authorization server, or a subject, because a grant is what the deployment
/// wrote and never who presented something.
/// </para>
/// <para>
/// The two mail-serving endpoints are reported differently, because their entries hold no grant to read back: what a
/// caller there holds is recorded on the credential an administrator provisioned. Their lines state which method each
/// entry accepts and where the grant behind it is kept, which is the part an operator cannot infer from the section.
/// The one line they share with the administrative endpoint is the one about configuring no entry at all — a surface
/// admitting everybody grants the whole of itself whichever axis it would otherwise have used.
/// </para>
/// <para>
/// It records rather than warns, including for the surface that configures no entry at all. That posture is already a
/// warning — <see cref="McpTransportAuthenticationWarning" />, <see cref="AdminTransportSecurityWarning" />, and
/// <see cref="ClientTransportSecurityWarning" /> each say
/// what it means that anything reaching the address is served — and what this adds is the half those cannot state,
/// which is how much such a caller then holds. Saying it twice at warning level would make the second one noise and the
/// first one easier to scroll past.
/// </para>
/// <para>
/// It runs as a hosted service so it appears among the other startup diagnostics, and it is registered whether or not
/// any endpoint is enabled, because it is the report that decides whether it has anything to say.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class TransportGrantStartupReport : IHostedService
{
    private const string McpEndpointName = "MCP";

    private const string AdminEndpointName = "administrative";

    private const string ClientEndpointName = "client";

    private const string NothingGranted = "nothing";

    private readonly McpEndpointOptions mcpEndpointSettings;
    private readonly AdminEndpointOptions adminEndpointSettings;
    private readonly ClientEndpointOptions clientEndpointSettings;
    private readonly ILogger<TransportGrantStartupReport> logger;

    /// <summary>Initializes a new startup report.</summary>
    /// <param name="mcpEndpointSettings">The MCP endpoint settings startup was composed from.</param>
    /// <param name="adminEndpointSettings">The administrative endpoint settings startup was composed from.</param>
    /// <param name="clientEndpointSettings">The client endpoint settings startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an endpoint settings argument is <see langword="null" />.</exception>
    public TransportGrantStartupReport(
        IOptions<McpEndpointOptions> mcpEndpointSettings,
        IOptions<AdminEndpointOptions> adminEndpointSettings,
        IOptions<ClientEndpointOptions> clientEndpointSettings,
        ILogger<TransportGrantStartupReport> logger)
    {
        ArgumentNullException.ThrowIfNull(mcpEndpointSettings);
        ArgumentNullException.ThrowIfNull(adminEndpointSettings);
        ArgumentNullException.ThrowIfNull(clientEndpointSettings);

        this.mcpEndpointSettings = mcpEndpointSettings.Value;
        this.adminEndpointSettings = adminEndpointSettings.Value;
        this.clientEndpointSettings = clientEndpointSettings.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.mcpEndpointSettings.Enabled)
        {
            this.ReportOwnerFacing(
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

        if (this.clientEndpointSettings.Enabled)
        {
            this.ReportOwnerFacing(
                ClientEndpointName,
                ClientEndpointOptions.RoutePrefix,
                ClientEndpointOptions.SectionName,
                ClientEndpointOptions.GrantedSurface,
                [.. this.clientEndpointSettings.Authentication]);
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
        var enforcement = EnforcementOn(surface);

        if (methods.Count == 0)
        {
            var wholeSurface = Describe(MailFathomPermission.PublishedFor(surface));

            this.LogSurfaceGrantedWithoutAnyEntry(
                endpointName,
                endpointPath,
                wholeSurface,
                settingPath,
                enforcement);

            return;
        }

        foreach (var (index, method) in methods.Index())
        {
            var grant = Describe(method.GrantedPermissions(surface));
            var entryPath = TransportAuthenticationConfiguration.SettingPathOf(sectionName, method, index);

            // The narrowing setting is asked first because it holds whether or not a list was written: an entry whose
            // sole block is OAuth may set it and state no ceiling, and what each token there holds is still its own
            // scopes rather than the whole surface the line would otherwise report.
            if (method.PermissionsFromTokenScopes)
            {
                this.LogEntryGrantNarrowedByTokenScopes(endpointName, entryPath, grant, enforcement);
            }
            else if (method.GrantsTheWholeSurface)
            {
                this.LogEntryGrantedWithoutBeingNarrowed(endpointName, entryPath, grant, enforcement);
            }
            else
            {
                this.LogEntryGrant(endpointName, entryPath, grant, enforcement);
            }
        }
    }

    /// <summary>States which methods a mail-serving endpoint accepts, and where the grant behind each of them lives.</summary>
    /// <remarks>
    /// The line an operator needs here is a different one, because there is no written grant to read back: what an
    /// admitted caller holds is recorded on the credential the administrative surface provisioned, per owner and per
    /// credential, so a report that printed a ceiling would be printing a number this section does not hold. What is
    /// worth stating is which methods are open and where to go and read what each credential may do.
    /// </remarks>
    private void ReportOwnerFacing(
        string endpointName,
        string endpointPath,
        string sectionName,
        ProtectedSurface surface,
        IReadOnlyList<OwnerFacingAuthenticationOptions> methods)
    {
        var settingPath = $"{sectionName}:{OwnerFacingAuthenticationConfiguration.SettingName}";
        var enforcement = EnforcementOn(surface);

        if (methods.Count == 0)
        {
            var wholeSurface = Describe(MailFathomPermission.PublishedFor(surface));

            this.LogSurfaceGrantedWithoutAnyEntry(
                endpointName,
                endpointPath,
                wholeSurface,
                settingPath,
                enforcement);

            return;
        }

        foreach (var (index, method) in methods.Index())
        {
            var entryPath = OwnerFacingAuthenticationConfiguration.SettingPathOf(sectionName, method, index);

            if (method.PermissionsFromTokenScopes)
            {
                this.LogOwnerFacingEntryNarrowedByTokenScopes(
                    endpointName,
                    entryPath,
                    method.AcceptedMethod.Name,
                    enforcement);
            }
            else
            {
                this.LogOwnerFacingEntry(endpointName, entryPath, method.AcceptedMethod.Name, enforcement);
            }
        }
    }

    /// <summary>States what a written grant does on this surface, which is not the same on both.</summary>
    /// <remarks>
    /// Carried on every line rather than reported once per endpoint, because these lines are read by searching for the
    /// entry path somebody edited: a posture stated on a line of its own is one a filtered log leaves out, which costs
    /// the operator the same thing as not stating it. The two surfaces differ in what a refusal looks like rather than
    /// in whether the grant is enforced, so a single wording would be wrong about one of them.
    /// </remarks>
    private static string EnforcementOn(ProtectedSurface surface) => surface switch
    {
        ProtectedSurface.Mail =>
            "A caller here is served only the tools its grant permits, and a call naming any other is answered as a "
            + "tool that does not exist.",
        ProtectedSurface.Administration =>
            "A route here is served only to a caller whose grant holds the one permission that route publishes, and "
            + "every other caller is refused with that permission named.",
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>Renders a resolved grant for a log line, naming emptiness rather than printing nothing.</summary>
    /// <remarks>An empty list would otherwise read as a message that lost its argument, which is exactly the grant worth being unambiguous about: it is how a credential is retired without its entry being deleted.</remarks>
    private static string Describe(IReadOnlyList<MailFathomPermission> permissions) => permissions.Count == 0
        ? NothingGranted
        : string.Join(", ", permissions.Select(permission => permission.Name));

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint entry {EntrySettingPath} grants {GrantedPermissions} to every credential "
            + "it admits. {GrantEnforcement}")]
    private partial void LogEntryGrant(
        string endpointName,
        string entrySettingPath,
        string grantedPermissions,
        string grantEnforcement);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint entry {EntrySettingPath} grants at most {GrantedPermissions}, and each "
            + "token holds whichever of those its own scopes carry. {GrantEnforcement}")]
    private partial void LogEntryGrantNarrowedByTokenScopes(
        string endpointName,
        string entrySettingPath,
        string grantedPermissions,
        string grantEnforcement);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint entry {EntrySettingPath} writes down no grant, so every credential it "
            + "admits holds {GrantedPermissions} — everything this surface publishes. Write a 'Permissions' list on the "
            + "entry to narrow it, or an empty one to grant nothing. {GrantEnforcement}")]
    private partial void LogEntryGrantedWithoutBeingNarrowed(
        string endpointName,
        string entrySettingPath,
        string grantedPermissions,
        string grantEnforcement);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint entry {EntrySettingPath} accepts {AcceptedMethod}, and what each "
            + "credential of that method grants is recorded beside the owner it resolves rather than here. Read it "
            + "with 'mfctl credential list'. {GrantEnforcement}")]
    private partial void LogOwnerFacingEntry(
        string endpointName,
        string entrySettingPath,
        string acceptedMethod,
        string grantEnforcement);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint entry {EntrySettingPath} accepts {AcceptedMethod}, and each token holds "
            + "whichever of its credential's recorded permissions its own scopes carry. Read the ceiling with "
            + "'mfctl credential list'. {GrantEnforcement}")]
    private partial void LogOwnerFacingEntryNarrowedByTokenScopes(
        string endpointName,
        string entrySettingPath,
        string acceptedMethod,
        string grantEnforcement);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint on {EndpointPath} configures no credential entry, so every caller it "
            + "serves holds {GrantedPermissions} — everything this surface publishes. There is no entry for a grant to "
            + "be written on until one is added under {AuthenticationSettingPath}. {GrantEnforcement}")]
    private partial void LogSurfaceGrantedWithoutAnyEntry(
        string endpointName,
        string endpointPath,
        string grantedPermissions,
        string authenticationSettingPath,
        string grantEnforcement);
}
