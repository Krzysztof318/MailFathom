// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Preferences;
using MailFathom.Infrastructure.Persistence.Policies;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>That a settings policy forces whether a person's client reports telemetry, and whose policy it is.</summary>
/// <param name="Enabled">The answer the policy forces, whatever the person stored as their own.</param>
/// <param name="HeldBy">The scope whose policy forces it.</param>
/// <remarks>
/// <para>
/// The one client preference a policy says anything about, and the only thing it says is a forced value: no default
/// and no editing restriction reaches it.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0037-a-settings-policy-at-the-deployment-and-at-each-organization.md">ADR 0037</see>
/// § <em>Client preferences, and what forcing telemetry costs a person</em> holds why, and what a deployment owes
/// somebody whose answer was taken from them.
/// </para>
/// <para>
/// It overrides the person's stored answer without replacing it. Nothing here is written to their preferences, so the
/// answer they gave is the one in force again the moment no policy forces the switch.
/// </para>
/// </remarks>
internal sealed record ForcedClientTelemetry(bool Enabled, SettingsPolicyScope HeldBy)
{
    /// <summary>Reads what the policies governing one person force their telemetry switch to.</summary>
    /// <param name="policies">The policies as their rows hold them.</param>
    /// <returns>The forced answer and who holds it, or <see langword="null" /> where neither policy forces the switch or this build does not read one of them as a settings policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="policies" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A policy this build does not read answers <see langword="null" /> here, and that is not a statement that it
    /// forces nothing. The record it governs is held back whole, so no composition carries this answer, while the
    /// credential of the person it governs still authenticates — which is why the telemetry routes and the session
    /// route treat somebody this replica holds no composition of as held off rather than as unforced, and a
    /// preferences write leaves their stored answer as it was.
    /// </remarks>
    internal static ForcedClientTelemetry? Read(GoverningSettingsPolicies policies) =>
        SettingsPolicyLayers.Read(policies, SettingsPolicySection.Users, out _)?.ForcedClientTelemetry;

    /// <summary>Says why an answer other than the forced one is refused.</summary>
    /// <returns>One sentence naming the switch and who holds it.</returns>
    internal string DescribeRefusal()
    {
        var holder = this.HeldBy == SettingsPolicyScope.Deployment ? "the deployment" : "the organization";

        return $"{nameof(ClientPreferences.TelemetryEnabled)} is held by {holder}, whose settings policy forces it {(this.Enabled ? "on" : "off")}, so your own answer is kept and not applied. Leave it as it was answered, or ask whoever administers {holder}.";
    }
}
