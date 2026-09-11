// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
using MailFathom.Domain.Access;

namespace MailFathom.AI.Enrichment;

/// <summary>Derives nothing, on a deployment that has not turned enrichment on.</summary>
/// <remarks>
/// <para>
/// The default state of an instance, and what every deployment gets until an operator says otherwise. It exists as a
/// registration rather than as an absent one because the answer the pass needs is a reason it can act on, and a missing
/// service would have made the pass carry a null check and decide for itself what an absence meant.
/// </para>
/// <para>
/// Nothing is read and nothing is sent. The passages are not touched at all, so an instance in this state cannot
/// disclose a message however often it is asked, which is the property the switch exists to give and would not have if
/// the refusal came after the turn had been composed.
/// </para>
/// </remarks>
internal sealed class InactiveEmailEnricher : IEmailEnricher
{
    /// <summary>The one instance, since it holds nothing and answers everything the same way.</summary>
    internal static readonly InactiveEmailEnricher Instance = new();

    private InactiveEmailEnricher()
    {
    }

    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public Task<EmailEnrichmentDerivation> DeriveAsync(
        EnrichableEmail email,
        MailUserLanguage language,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            EmailEnrichmentDerivation.Withholding(EmailEnrichmentWithholding.NotActivated));
    }
}
