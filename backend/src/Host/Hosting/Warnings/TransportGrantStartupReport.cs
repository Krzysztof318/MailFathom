// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Mcp;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup what every credential entry admits, and what a surface with no entry grants.</summary>
/// <remarks>
/// <para>
/// A grant nobody wrote down reaches the whole of its surface, which is what makes a deployment work before it is
/// governed and is also the one thing about it an operator would otherwise never see. Reporting each entry's resolved
/// grant is what turns that default into a posture somebody chose: they meet it in the log on the first run rather than
/// inferring it later from what a credential turned out to be able to do.
/// </para>
/// <para>
/// No entry holds a grant to read back: what a caller holds is what its user's roles grant, kept to what the credential
/// an administrator provisioned names — on the administrative endpoint, to the administrative half of those roles. So
/// each line states which method an entry accepts and where the grant behind it is kept, which is the part an operator
/// cannot infer from the section, and nothing here names a key, a public key, a token, an authorization server, or a
/// subject. The one line that differs per surface is the one about configuring no entry at all: a mail-serving surface
/// then serves the user the deployment serves, and the administrative one serves the default administrator.
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
            this.ReportUserFacing(
                McpEndpointName,
                McpEndpointRoute.Path,
                McpEndpointOptions.SectionName,
                McpEndpointOptions.GrantedSurface,
                [.. this.mcpEndpointSettings.Authentication]);
        }

        if (this.adminEndpointSettings.Enabled)
        {
            this.ReportAdministrative([.. this.adminEndpointSettings.Authentication]);
        }

        if (this.clientEndpointSettings.Enabled)
        {
            this.ReportUserFacing(
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

    /// <summary>States which methods the administrative endpoint accepts, or who its callers are where it accepts none.</summary>
    private void ReportAdministrative(IReadOnlyList<UserFacingAuthenticationOptions> methods)
    {
        const string sectionName = AdminEndpointOptions.SectionName;
        var enforcement = EnforcementOn(AdminEndpointOptions.GrantedSurface);

        if (methods.Count == 0)
        {
            this.LogAdministrativeSurfaceServedAsTheDefaultAdministrator(
                AdminEndpointOptions.RoutePrefix,
                $"{sectionName}:{UserFacingAuthenticationConfiguration.SettingName}",
                enforcement);

            return;
        }

        foreach (var (index, method) in methods.Index())
        {
            var entryPath = UserFacingAuthenticationConfiguration.SettingPathOf(sectionName, method, index);

            if (method.PermissionsFromTokenScopes)
            {
                this.LogAdministrativeEntryNarrowedByTokenScopes(entryPath, method.AcceptedMethod.Name, enforcement);
            }
            else
            {
                this.LogAdministrativeEntry(entryPath, method.AcceptedMethod.Name, enforcement);
            }
        }
    }

    /// <summary>States which methods a mail-serving endpoint accepts, and where the grant behind each of them lives.</summary>
    /// <remarks>
    /// The line an operator needs here is a different one, because there is no written grant to read back: what an
    /// admitted caller holds is what its user's roles grant, kept to what its credential names, and both are records
    /// that change while the process runs, so a report that printed a ceiling would be printing a number this section
    /// does not hold. What is worth stating is which methods are open and where to go and read what each credential
    /// names.
    /// </remarks>
    private void ReportUserFacing(
        string endpointName,
        string endpointPath,
        string sectionName,
        ProtectedSurface surface,
        IReadOnlyList<UserFacingAuthenticationOptions> methods)
    {
        var settingPath = $"{sectionName}:{UserFacingAuthenticationConfiguration.SettingName}";
        var enforcement = EnforcementOn(surface);

        if (methods.Count == 0)
        {
            var wholeSurface = Describe(MailFathomPermission.PublishedFor(surface));

            this.LogUserFacingSurfaceGrantedWithoutAnyEntry(
                endpointName,
                endpointPath,
                wholeSurface,
                settingPath,
                enforcement);

            return;
        }

        foreach (var (index, method) in methods.Index())
        {
            var entryPath = UserFacingAuthenticationConfiguration.SettingPathOf(sectionName, method, index);

            if (method.PermissionsFromTokenScopes)
            {
                this.LogUserFacingEntryNarrowedByTokenScopes(
                    endpointName,
                    entryPath,
                    method.AcceptedMethod.Name,
                    enforcement);
            }
            else
            {
                this.LogUserFacingEntry(endpointName, entryPath, method.AcceptedMethod.Name, enforcement);
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
        Message = "The administrative endpoint entry {EntrySettingPath} accepts {AcceptedMethod} from a credential "
            + "listing the 'admin' surface, and each caller it admits is that credential's user, holding what their "
            + "administrative roles grant, kept to the permissions its credential names. A credential left holding none "
            + "is refused. Read a credential's names with 'mfctl credential list'. {GrantEnforcement}")]
    private partial void LogAdministrativeEntry(
        string entrySettingPath,
        string acceptedMethod,
        string grantEnforcement);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The administrative endpoint entry {EntrySettingPath} accepts {AcceptedMethod} from a credential "
            + "listing the 'admin' surface, and each token holds what its user's administrative roles grant, kept to "
            + "the permissions its credential names and then to those its own scopes carry. A credential left holding "
            + "none is refused. Read a credential's names with 'mfctl credential list'. {GrantEnforcement}")]
    private partial void LogAdministrativeEntryNarrowedByTokenScopes(
        string entrySettingPath,
        string acceptedMethod,
        string grantEnforcement);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint entry {EntrySettingPath} accepts {AcceptedMethod}, and each caller it "
            + "admits holds what its user's roles grant, kept to the permissions its credential names. Read a "
            + "credential's names with 'mfctl credential list'. {GrantEnforcement}")]
    private partial void LogUserFacingEntry(
        string endpointName,
        string entrySettingPath,
        string acceptedMethod,
        string grantEnforcement);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint entry {EntrySettingPath} accepts {AcceptedMethod}, and each token holds "
            + "what its user's roles grant, kept to the permissions its credential names and then to those its own "
            + "scopes carry. Read a credential's names with 'mfctl credential list'. {GrantEnforcement}")]
    private partial void LogUserFacingEntryNarrowedByTokenScopes(
        string endpointName,
        string entrySettingPath,
        string acceptedMethod,
        string grantEnforcement);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint on {EndpointPath} configures no credential entry, so every caller it "
            + "serves acts for the user this deployment serves and holds whatever of {GrantedPermissions} that user's "
            + "roles grant. Add an entry under {AuthenticationSettingPath} naming a method this endpoint accepts, and "
            + "provision each user's credentials with 'mfctl credential create'; an entry here carries no permissions "
            + "of its own. {GrantEnforcement}")]
    private partial void LogUserFacingSurfaceGrantedWithoutAnyEntry(
        string endpointName,
        string endpointPath,
        string grantedPermissions,
        string authenticationSettingPath,
        string grantEnforcement);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The administrative endpoint on {EndpointPath} configures no credential entry, so every caller it "
            + "serves acts as the default administrator 'admin' and holds what that user's administrative roles "
            + "grant. Add an entry under {AuthenticationSettingPath} naming a method this endpoint accepts. "
            + "{GrantEnforcement}")]
    private partial void LogAdministrativeSurfaceServedAsTheDefaultAdministrator(
        string endpointPath,
        string authenticationSettingPath,
        string grantEnforcement);
}
