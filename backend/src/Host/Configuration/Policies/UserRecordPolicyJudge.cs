// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
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
            .. records.Select(record => this.JudgeUnder(
                record,
                SettingsPolicyLayers.Read(record.Standing, ReadOnce, out _),
                SettingsPolicyLayers.Read(record.Candidate, ReadOnce, out _))),
        ];
    }

    private UserRecordPolicyJudgement JudgeUnder(
        GovernedUserRecord record,
        SettingsPolicyLayers? standing,
        SettingsPolicyLayers? candidate)
    {
        // A record or a policy this build does not read is one it cannot resolve, so the row keeps the switches it
        // has: writing a guess over them would move somebody on or off an endpoint on no reading at all.
        if (record.Json is not { } own || candidate is null)
        {
            return new UserRecordPolicyJudgement(EndpointAccess: null, []);
        }

        var (access, problems) = this.Bind(own, candidate);

        if (problems.Count == 0 || standing is null || this.Bind(own, standing).Problems.Count > 0)
        {
            return new UserRecordPolicyJudgement(access, []);
        }

        return new UserRecordPolicyJudgement(access, problems);
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
