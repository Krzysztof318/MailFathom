// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>Derives what one message is about, why it may matter, and any commitment it contains.</summary>
/// <remarks>
/// <para>
/// The one way a message becomes a set of marks. An implementation is registered for every deployment, including one
/// that has turned enrichment off — the answer a caller needs is a reason it can act on, and a missing registration
/// would have made every caller carry a null check and decide for itself what an absence meant.
/// </para>
/// <para>
/// It never throws for a provider that failed. Every way a derivation can come to nothing is a member of
/// <see cref="EmailEnrichmentWithholding" />, because the caller's response to each of them is the same shape — leave
/// the message outstanding and stop — and an exception would have made an ordinary outage look like a defect in the
/// pass. Caller cancellation is the exception to that and propagates, being the caller withdrawing the work rather than
/// anything about the message.
/// </para>
/// </remarks>
public interface IEmailEnricher
{
    /// <summary>Gets whether this deployment derives marks at all.</summary>
    /// <remarks>
    /// Asked so that a pass on a deployment that has not turned enrichment on answers in one comparison and issues no
    /// query — the selection it would otherwise run costs a scan per account per run for work nothing will do. It is
    /// the one place the answer to "does this instance derive marks" is given, which is why it is a member here rather
    /// than a second reading of the configuration beside the registration that already decided it.
    /// </remarks>
    bool IsActive { get; }

    /// <summary>Derives one message's marks, written in the language the person they are for reads.</summary>
    /// <param name="email">The message and the passages the derivation reads.</param>
    /// <param name="language">The language every sentence the derivation produces is written in.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The marks that were settled, or the reason nothing was derived this time.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="email" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The language is the caller's rather than the message's, and it is an argument rather than a property of
    /// <see cref="EnrichableEmail" /> because it is a fact about whom the derivation is for. What that record carries
    /// stays what a derivation reads, which is what keeps the person out of the text sent to a provider.
    /// </remarks>
    Task<EmailEnrichmentDerivation> DeriveAsync(
        EnrichableEmail email,
        MailUserLanguage language,
        CancellationToken cancellationToken);
}
