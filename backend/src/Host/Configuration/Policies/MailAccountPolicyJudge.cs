// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Infrastructure.Persistence.Policies.MailAccounts;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Judges mail accounts under the policies a policy write or a move would leave them governed by.</summary>
/// <remarks>
/// <para>
/// An account is judged by resolving it through the policies and binding what resolves with the binder every write
/// of an account goes through, as an account being written: a policy that moves an account's effective settings is
/// stating them, so the rules a declaration is held to — that a mailbox states the language it is written in, that
/// a scanner the deployment requires is never switched off — hold however the halves of the declaration were
/// supplied.
/// </para>
/// <para>
/// What refuses a write is what it breaks, never what was broken already. The account is bound under the policies
/// that stand as well, and a write answers only the sentences the candidate policies add. An account whose effective
/// settings the write leaves exactly as they were is not bound at all, which is what keeps a write that states one
/// default from binding every account that states that property itself.
/// </para>
/// <para>
/// A write made under a grant held below the deployment is held to the bound an account's own write is held to: it
/// may not move the effective settings of an account naming a secret that was not provisioned for a user the
/// account is assigned to, because a forced host presents that credential somewhere new exactly as a written one
/// does. Which settings decide that is not enumerated, for the reason the per-account bound does not enumerate them.
/// </para>
/// <para>
/// An account one of whose candidate policies this build does not read keeps its columns: there is nothing to
/// derive them from. It refuses nothing to a grant held at the deployment. To a grant held below it, it refuses an
/// account naming a secret that is not its users', because what the write would move on that account cannot be told
/// and a replica that does read the policy would serve whatever it moved.
/// </para>
/// </remarks>
// ponytail: an account whose effective settings a write moves is bound four times, twice for what it refuses and
// twice for its columns. Read the columns off the first two bindings if a policy write over many accounts measures slow.
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class MailAccountPolicyJudge(UserAccountDocumentBinder binder) : IMailAccountPolicyJudge
{
    private static readonly MailAccountPolicyJudgement Unmoved = new(Settings: null, []);

    /// <inheritdoc />
    public IReadOnlyList<MailAccountPolicyJudgement> Judge(
        IReadOnlyList<GovernedMailAccount> accounts,
        bool underANarrowerGrant)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        // Keyed by the document itself rather than by its text: the accounts of one write share the policy documents
        // the write read, so the same instance arrives for every one of them and is judged as a policy once.
        var read = new Dictionary<string, SettingsPolicyStatements?>(ReferenceEqualityComparer.Instance);

        SettingsPolicyStatements? ReadOnce(string policyJson)
        {
            if (!read.TryGetValue(policyJson, out var statements))
            {
                statements = SettingsPolicyStatements.Read(policyJson, SettingsPolicySection.MailAccounts);
                read[policyJson] = statements;
            }

            return statements;
        }

        return
        [
            .. accounts.Select(account => this.JudgeUnder(
                account,
                SettingsPolicyLayers.Read(account.Standing, ReadOnce, out _),
                SettingsPolicyLayers.Read(account.Candidate, ReadOnce, out _),
                underANarrowerGrant)),
        ];
    }

    private MailAccountPolicyJudgement JudgeUnder(
        GovernedMailAccount governed,
        SettingsPolicyLayers? standing,
        SettingsPolicyLayers? candidate,
        bool underANarrowerGrant)
    {
        if (candidate is null)
        {
            // Compared against settings stating nothing, so every secret the declaration itself names is asked about:
            // a secret block takes no policy value, which makes the declaration's the ones the account is served.
            return underANarrowerGrant
                && UserRecordAdministration.FindSecretsANarrowerScopeMayNotName(governed.Users, "{}", governed.Account.Document)
                    is { Count: > 0 } namedBeyondTheGrant
                    ? new MailAccountPolicyJudgement(Settings: null, namedBeyondTheGrant)
                    : Unmoved;
        }

        var account = governed.Account;
        var served = MailAccountPolicyResolution.Resolve(account, candidate).EffectiveJson;
        var stood = standing is null ? null : MailAccountPolicyResolution.Resolve(account, standing).EffectiveJson;

        if (string.Equals(stood, served, StringComparison.Ordinal))
        {
            return Unmoved;
        }

        // Asked before anything is bound, and against settings stating nothing where the standing policy is not one
        // this build reads: what the account was served with is then unknown, and a grant that may not move a foreign
        // credential is not given the benefit of that doubt.
        if (underANarrowerGrant
            && UserRecordAdministration.FindSecretsANarrowerScopeMayNotName(governed.Users, stood ?? "{}", served)
                is { Count: > 0 } beyondTheGrant)
        {
            return new MailAccountPolicyJudgement(Settings: null, beyondTheGrant);
        }

        // Where the standing policy is not one this build reads, what the account was refused under it is unknown,
        // so everything the candidate leaves wrong counts as the write's own.
        IReadOnlyList<string> refusedBefore = standing is null ? [] : this.FindRefusals(governed, standing);

        if (this.FindRefusals(governed, candidate).Except(refusedBefore, StringComparer.Ordinal).ToArray() is { Length: > 0 } introduced)
        {
            return new MailAccountPolicyJudgement(Settings: null, introduced);
        }

        var settings = MailAccountQueryableSettingsReading.Of(account, candidate);

        return standing is not null
            && MailAccountQueryableSettingsReading.StateTheSame(settings, MailAccountQueryableSettingsReading.Of(account, standing))
                ? Unmoved
                : new MailAccountPolicyJudgement(settings, []);
    }

    /// <summary>Binds one account on its own, as a declaration being written, under one reading of its policies.</summary>
    /// <remarks>
    /// Composed into a record stating nothing else, so a rule over the whole set a user is served — a display name two
    /// of their mailboxes would share — is not asked here: no policy states an account's name, and such a rule
    /// answers the same under the standing policies and the candidate ones.
    /// </remarks>
    private IReadOnlyList<string> FindRefusals(GovernedMailAccount governed, SettingsPolicyLayers layers)
    {
        var declaration = MailAccountRecordComposition.DeclarationOf(governed.Account, layers);

        return binder.Bind(MailAccountRecordComposition.ComposeAlone(declaration), UserRecordArrival.BeingWritten).Refusals;
    }
}
