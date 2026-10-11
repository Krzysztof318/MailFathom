// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Policies;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Judges users' records under the policies a policy write or a move would leave them governed by.</summary>
/// <remarks>
/// <para>
/// A record is judged by resolving it through the policies and binding what resolves with the binder every other
/// reading of a user's record goes through, so a record this answers as servable is one a start would serve. Its
/// mail accounts are left out: no property of a user's own record decides whether one of their accounts binds, and
/// an account is governed by a section of its own.
/// </para>
/// <para>
/// What refuses a write is a record it breaks, never a record that was broken already. A record is therefore bound
/// twice where the candidate policies leave it unservable — once under those and once under the standing ones — and
/// answers a refusal only where the standing ones served it.
/// </para>
/// <para>
/// A record that cannot be resolved still has every switch written that a readable policy forces. A force outranks
/// the record, so its value is known without reading one, and leaving the row alone would keep somebody on an
/// endpoint the policy just closed for exactly as long as their record stays unreadable. The deployment's force is
/// read whenever its policy is; an organization's only beside a deployment policy that reads, since the
/// deployment's force is the one that would outrank it.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class UserRecordPolicyJudge(UserAccountDocumentBinder binder) : IUserRecordPolicyJudge
{
    /// <inheritdoc />
    public IReadOnlyList<UserRecordPolicyJudgement> Judge(IReadOnlyList<GovernedUserRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        // Keyed by the document itself rather than by its text: the records of one write share the policy documents
        // the write read, so the same instance arrives for every one of them and is judged as a policy once.
        var read = new Dictionary<string, SettingsPolicyStatements?>(ReferenceEqualityComparer.Instance);

        SettingsPolicyStatements? ReadOnce(string policyJson)
        {
            if (!read.TryGetValue(policyJson, out var statements))
            {
                statements = SettingsPolicyStatements.Read(policyJson, SettingsPolicySection.Users);
                read[policyJson] = statements;
            }

            return statements;
        }

        return
        [
            .. records.Select(record =>
            {
                var candidate = SettingsPolicyLayers.Read(record.Candidate, ReadOnce, out var unreadable);

                // Where only the organization's policy is unreadable, the deployment's still says what it forces.
                var forcing = candidate
                    ?? (unreadable == SettingsPolicyScope.Organization
                        && ReadOnce(record.Candidate.Deployment.Json) is { } deployment
                        ? new SettingsPolicyLayers(deployment, Organization: null)
                        : null);

                return this.JudgeUnder(
                    record,
                    SettingsPolicyLayers.Read(record.Standing, ReadOnce, out _),
                    candidate,
                    forcing);
            }),
        ];
    }

    private UserRecordPolicyJudgement JudgeUnder(
        GovernedUserRecord record,
        SettingsPolicyLayers? standing,
        SettingsPolicyLayers? candidate,
        SettingsPolicyLayers? forcing)
    {
        // A record or a policy this build does not read is one it cannot resolve, so the row keeps the switches it
        // has wherever nothing readable forces one: writing a guess over them would move somebody on or off an
        // endpoint on no reading at all.
        if (record.Json is not { } own || candidate is null)
        {
            return new UserRecordPolicyJudgement(WriteForcedOver(record.Held, forcing), []);
        }

        var (access, problems) = this.Bind(own, candidate);

        // A record that does not bind resolves to no switches of its own, and is held to the forces all the same.
        access ??= WriteForcedOver(record.Held, candidate);

        if (problems.Count == 0 || standing is null || this.Bind(own, standing).Problems.Count > 0)
        {
            return new UserRecordPolicyJudgement(access, []);
        }

        return new UserRecordPolicyJudgement(access, problems);
    }

    /// <summary>Writes every switch the policies force over the ones a row holds.</summary>
    /// <returns>The switches the row is to carry, or <see langword="null" /> where no policy read forces either, so the row is left as it stands.</returns>
    private static UserEndpointAccess? WriteForcedOver(UserEndpointAccess held, SettingsPolicyLayers? forcing)
    {
        if (forcing is null)
        {
            return null;
        }

        // Resolved over a record stating nothing: a forced value is the same whatever the record says.
        var resolved = GovernedRecordResolution.Resolve(SettingsPolicySection.Users, forcing, "{}");
        var forced = UserEndpointAccessOptions.ReadFrom(RedactedDocumentSave.Flatten(resolved.EffectiveJson));

        bool IsForced(string switchPath) => resolved.Find(switchPath).ForcedBy is not null;

        var mcpForced = IsForced(UserEndpointAccessOptions.McpEndpointKey);
        var clientForced = IsForced(UserEndpointAccessOptions.ClientEndpointKey);

        return mcpForced || clientForced
            ? new UserEndpointAccess(
                mcpForced ? forced.McpEndpoint : held.McpEndpoint,
                clientForced ? forced.ClientEndpoint : held.ClientEndpoint)
            : null;
    }

    private (UserEndpointAccess? Access, IReadOnlyList<string> Problems) Bind(string own, SettingsPolicyLayers layers)
    {
        var binding = binder.Bind(
            GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, own).EffectiveJson,
            UserRecordArrival.AlreadyHeld);

        return binding.User is { } bound
            ? (bound.EndpointAccess.Access, [.. bound.FindMissingLanguageError().Select(missing => missing.ErrorMessage!)])
            : (null, binding.Refusals);
    }
}
