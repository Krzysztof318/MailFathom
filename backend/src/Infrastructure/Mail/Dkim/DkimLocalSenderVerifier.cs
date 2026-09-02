// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail;
using MailFathom.Domain.Emails.Authentication;
using MimeKit;
using MimeKit.Cryptography;

namespace MailFathom.Infrastructure.Mail.Dkim;

/// <summary>Verifies a delivered message's DKIM signatures against the keys their domains publish.</summary>
/// <remarks>
/// <para>
/// DKIM is the one of the four checks a message still answers after delivery: the signature is in the stored bytes and
/// verifying it needs the key its domain publishes and nothing else. SPF is not attempted, and no
/// <c>Received</c>-chain heuristic stands in for it — it authenticates an envelope sender against a connecting address,
/// and after delivery this process has neither.
/// </para>
/// <para>
/// Every signature the message carries is verified rather than only the first, up to the bound the reading sets. A
/// message legitimately carries a delivery provider's signature beside its author's, and taking whichever came first
/// would leave the author unestablished on ordinary mail while establishing nothing an attacker could not also arrange.
/// </para>
/// <para>
/// Nothing a message can contain raises out of here. A malformed signature, a signature naming no usable domain, an
/// unpublished key, an unreadable key record, and a nameserver that will not answer all leave that signature
/// contributing nothing — and a message where that is all of them carries the not-established verdict, which is exactly
/// what it carried before this deployment verified anything. What still propagates is the caller's cancellation.
/// </para>
/// <para>
/// What a message may cost is bounded twice, because both the number of lookups and the names they ask for are written
/// by whoever sent it: the reading caps how many signatures are verified at all, and the budget below caps how long the
/// whole of one message's verification may take whatever those lookups do.
/// </para>
/// <para>
/// Nothing here is logged. A signing domain, the displayed author, and the fact that a particular message failed are
/// all personal data about correspondence, and the verdict is where they are recorded.
/// </para>
/// </remarks>
internal sealed class DkimLocalSenderVerifier : ILocalSenderVerifier
{
    /// <summary>How long the whole of one message's verification may take before the rest is left unchecked.</summary>
    /// <remarks>
    /// The resolver bounds one lookup; this bounds the message, and both are needed because the number of lookups is
    /// written by whoever sent the mail. Without it a message carrying the maximum number of signatures, each naming a
    /// domain whose nameserver accepts a query and never answers, would hold the folder run for as many deadlines as it
    /// carried signatures — which is a stall an attacker composes rather than one the network causes. Past the budget
    /// every remaining signature is left unchecked, which is the same outcome an unresolvable key already has.
    /// </remarks>
    private static readonly TimeSpan MessageVerificationBudget = TimeSpan.FromSeconds(10);

    private readonly IDkimPublicKeyRecordResolver resolver;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a verifier over the port that resolves published keys.</summary>
    /// <param name="resolver">Resolves what a signing domain publishes for a selector.</param>
    /// <param name="timeProvider">Measures the budget one message's verification is given.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public DkimLocalSenderVerifier(IDkimPublicKeyRecordResolver resolver, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.resolver = resolver;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<SenderAuthentication> VerifyAsync(
        MimeMessage message,
        string? displayedSenderAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var signatures = message.Headers
            .Where(static header => header.Id is HeaderId.DkimSignature)
            .Take(LocalSenderAuthenticationReading.MaximumVerifiedSignaturesPerMessage)
            .ToArray();

        var verifier = new DkimVerifier(new ResolvedDkimPublicKeyLocator(this.resolver));
        List<SenderDomain> verifiedSigningDomains = [];
        var anySignatureRejected = false;

        using var budget = new CancellationTokenSource(MessageVerificationBudget, this.timeProvider);
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);

        foreach (var signature in signatures)
        {
            if (!DkimSignatureTags.TryReadSigningDomain(signature.Value, out var signingDomain))
            {
                continue;
            }

            switch (await VerifiedAsync(verifier, message, signature, work.Token, cancellationToken))
            {
                case true:
                    verifiedSigningDomains.Add(signingDomain);
                    break;

                case false:
                    anySignatureRejected = true;
                    break;

                default:
                    // The key never arrived, so nothing was checked. It is deliberately not counted as a rejection:
                    // this deployment's own network trouble must not read as a statement against somebody's mail.
                    break;
            }
        }

        return LocalSenderAuthenticationReading.Read(
            verifiedSigningDomains,
            anySignatureRejected,
            displayedSenderAddress);
    }

    /// <summary>Verifies one signature, answering with nothing where no key could be obtained to check it against.</summary>
    /// <param name="verifier">Performs the verification.</param>
    /// <param name="message">The message the signature was written over.</param>
    /// <param name="signature">The signature header being checked.</param>
    /// <param name="budgetToken">Ends the work when this message's verification has taken long enough.</param>
    /// <param name="cancellationToken">The caller's own cancellation, which is the one that still propagates.</param>
    private static async Task<bool?> VerifiedAsync(
        DkimVerifier verifier,
        MimeMessage message,
        Header signature,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await verifier.VerifyAsync(message, signature, budgetToken);
        }
        catch (DkimPublicKeyUnavailableException)
        {
            return null;
        }
        catch (FormatException)
        {
            // The signature header itself is malformed, so there was never a claim to check. It contributes nothing
            // rather than failing the extraction, exactly as an unparseable trusted header does.
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The message's own budget ran out, which is this deployment declining to spend more rather than the caller
            // giving up. Nothing was checked, so the signature contributes nothing.
            return null;
        }
    }
}
