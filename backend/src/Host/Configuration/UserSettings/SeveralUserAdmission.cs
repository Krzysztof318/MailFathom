// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Decides whether this deployment's endpoints could tell one user's caller from another's.</summary>
/// <remarks>
/// <para>
/// A deployment serves several users only where every caller it admits on a user-facing surface says which user it
/// is acting for. Otherwise the surface composes each caller against whichever user the deployment happens to name,
/// which is how one person is handed another person's mail — so the roster is held to one user instead, and the two
/// places that decide it ask this rather than each carrying its own reading of the endpoint settings.
/// </para>
/// <para>
/// Every credential a mail-serving surface admits is a record naming the user it belongs to, whichever method presents
/// it, so the one remaining way a caller arrives naming nobody is a surface that requires no authentication at all. An
/// entry on such a surface therefore says nothing about this reading — what it accepts is a method, and the user comes
/// from the row the credential resolves — and what the bound turns on is the enablement and the requirement.
/// </para>
/// <para>
/// The administrative surface is deliberately not among them. An administrator's acts are the deployment's rather than
/// one person's, so a caller there is admitted for no user and every user-scoped route names the user it is for —
/// which is what makes provisioning a second user something an operator can do at all.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reading.")]
internal sealed class SeveralUserAdmission
{
    private readonly McpEndpointOptions mcpEndpointSettings;
    private readonly ClientEndpointOptions clientEndpointSettings;

    /// <summary>Initializes the reading over the endpoint settings startup was composed from.</summary>
    /// <param name="mcpEndpointSettings">The MCP endpoint settings.</param>
    /// <param name="clientEndpointSettings">The client endpoint settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>The startup snapshot rather than a reloaded value, because it is the posture the authentication schemes were registered from and a later reading would answer for one no scheme was composed against.</remarks>
    public SeveralUserAdmission(
        IOptions<McpEndpointOptions> mcpEndpointSettings,
        IOptions<ClientEndpointOptions> clientEndpointSettings)
    {
        ArgumentNullException.ThrowIfNull(mcpEndpointSettings);
        ArgumentNullException.ThrowIfNull(clientEndpointSettings);

        this.mcpEndpointSettings = mcpEndpointSettings.Value;
        this.clientEndpointSettings = clientEndpointSettings.Value;
    }

    /// <summary>Gets whether an enabled user-facing surface admits a caller that names no user.</summary>
    /// <remarks>The one reason this deployment may serve one user and no more; <see cref="Refusal" /> is the sentence it is refused with.</remarks>
    public bool AdmitsACallerNamingNoUser =>
        (this.mcpEndpointSettings.Enabled && !this.mcpEndpointSettings.RequiresAuthentication)
        || (this.clientEndpointSettings.Enabled && !this.clientEndpointSettings.RequiresAuthentication);

    /// <summary>Gets the sentence a deployment that may serve one user only is refused a second one with.</summary>
    /// <remarks>It names the correction rather than the state, because the state is a posture an operator chose and the correction is the one thing they can do about it.</remarks>
    public string Refusal =>
        "This deployment serves a user-facing endpoint that requires no authentication, so every caller reaching it is composed against the one user the deployment holds, and a second user would leave that endpoint serving one person another person's mail. Require a credential on the MCP and client endpoints, or switch them off, before this deployment serves more than one user.";
}
