// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.TestSupport;

/// <summary>Answers with one posture for every owner, for the paths that only read whether the feature is on.</summary>
/// <remarks>
/// The accounts are stated rather than derived because the deployed reader resolves them from the owner roster and the
/// mail section, neither of which a use-case test binds. Naming them is what lets a test reach the set-based half of the
/// gate: the scope covers exactly those accounts and, within each, exactly the folders the posture names.
/// </remarks>
internal sealed class StubSpamClassificationSettingsReader : ISpamClassificationSettingsReader
{
    private readonly SpamClassificationSettings settings;

    private readonly MailAccountId[] accounts;

    /// <summary>Builds a reader answering one posture for every owner, over the accounts that posture classifies.</summary>
    /// <param name="settings">The posture every owner is answered with.</param>
    /// <param name="accounts">The accounts whose owners classify, which the scope is composed from.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the posture is switched on and no account is named.</exception>
    /// <remarks>
    /// The refusal is what keeps the double honest about the pairing the deployed reader cannot produce: an owner whose
    /// settings are enabled contributes their accounts, so classification switched on beside an empty scope exists
    /// nowhere. A test reaching that pairing would read as having switched classification on while
    /// <see cref="DerivedWorkAdmissionTerms.IsApplied" /> stayed false and the gate admitted everything, and it would
    /// pass over an ungated pipeline.
    /// </remarks>
    public StubSpamClassificationSettingsReader(
        SpamClassificationSettings settings,
        params MailAccountId[] accounts)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(accounts);

        if (settings.IsEnabled && accounts.Length == 0)
        {
            throw new ArgumentException(
                "A posture that classifies names the accounts it classifies for, because a scope naming none reads as classification switched off.",
                nameof(accounts));
        }

        this.settings = settings;
        this.accounts = accounts;
    }

    /// <summary>Gets a reader for the deployment that configured nothing, which classifies no mail.</summary>
    public static StubSpamClassificationSettingsReader Disabled { get; } = new(SpamClassificationSettings.Disabled);

    /// <inheritdoc />
    public SpamClassificationScope ScopeInForce => this.settings.IsEnabled
        ? SpamClassificationScope.Create(
            this.accounts,
            this.accounts.SelectMany(account => this.settings.ScannedFolderAliases
                .Select(alias => new MailFolderIdentity(account, alias))))
        : SpamClassificationScope.None;

    /// <inheritdoc />
    public SpamClassificationSettings SettingsFor(MailOwnerId owner) => this.settings;
}
