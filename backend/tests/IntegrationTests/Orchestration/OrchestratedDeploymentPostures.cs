// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Accounts;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>The two settings a composition root reads beside a mailbox's posture, as this suite's deployment made them.</summary>
/// <remarks>
/// <para>
/// Both are answered the same way whichever mailbox or user is asked about, because no test here exercises a
/// deployment where two of either decided differently: what the suite needs is that an account's posture resolves at
/// all, since the resolution the classifier and the filing both go through composes these two beside the
/// sensitive-content posture.
/// </para>
/// <para>
/// The answers are the shipped defaults — no action taken on a spam verdict, and English — which is what a deployment
/// that changed neither setting holds. A test about either states its own reader rather than moving these.
/// </para>
/// </remarks>
internal sealed class OrchestratedDeploymentPostures : ISpamActionSettingsReader, IMailAccountLanguages
{
    /// <inheritdoc />
    public SpamActionSettings ActionsFor(MailAccountId account) => SpamActionSettings.None;

    /// <inheritdoc />
    public MailAccountLanguage LanguageOf(MailAccountId account) => MailAccountLanguage.English;
}
