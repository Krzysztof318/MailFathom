// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What this deployment decided about the author one message authenticated as.</summary>
/// <remarks>
/// <para>
/// It is the second of a message's two sender verdicts and reads the first. <see cref="SenderAuthentication" /> says
/// what the receiving server established about the message, which is a fact about the message; this says whether the
/// authenticated author is one this deployment recognizes, which is a decision about a list an operator and a reader
/// both write to. Keeping them apart is what lets the list change without the facts changing under it, and it is why
/// this carries no value for <em>the author was not authenticated</em> — that is the other verdict's to state.
/// </para>
/// <para>
/// It is stored rather than computed on read, so a message keeps the answer it was judged with. What makes that
/// legible is <see cref="PolicyRevision" />: it names the list the answer came from, and a verdict nothing judged
/// carries <see cref="SenderTrustPolicyRevision.None" />.
/// </para>
/// </remarks>
public sealed record SenderTrust
{
    private SenderTrust(
        SenderTrustLevel level,
        SenderTrustSource grantedBy,
        SenderTrustPolicyRevision policyRevision)
    {
        this.Level = level;
        this.GrantedBy = grantedBy;
        this.PolicyRevision = policyRevision;
    }

    /// <summary>Gets the verdict a message reaching persistence without a policy having spoken carries.</summary>
    /// <remarks>
    /// Unknown, because that is the answer that claims nothing, and carrying no revision, which is what says a policy
    /// never ran rather than that one ran and recognized nobody. It is what an extraction produces before the
    /// deployment's policy is applied to it, and what a message whose payload was never stored keeps.
    /// </remarks>
    public static SenderTrust NotEvaluated { get; } = new(
        SenderTrustLevel.Unknown,
        SenderTrustSource.None,
        SenderTrustPolicyRevision.None);

    /// <summary>Gets what this deployment decided, which is the value everything above this reads first.</summary>
    public SenderTrustLevel Level { get; }

    /// <summary>Gets which half of what this deployment knows recognized the author, or that none did.</summary>
    public SenderTrustSource GrantedBy { get; }

    /// <summary>Gets the trusted-sender policy the decision was reached under.</summary>
    public SenderTrustPolicyRevision PolicyRevision { get; }

    /// <summary>Gets whether this deployment recognized the message's authenticated author.</summary>
    public bool IsTrusted => this.Level == SenderTrustLevel.Trusted;

    /// <summary>Records that this deployment recognizes nobody in the message.</summary>
    /// <param name="policyRevision">The policy that was applied.</param>
    /// <returns>The verdict.</returns>
    /// <remarks>
    /// It covers every way of arriving there: no author was authenticated, the author's authentication failed, or an
    /// author authenticated and nothing this deployment knows names them. Those differ in what the receiving server
    /// established rather than in what this deployment decided, so they stay on <see cref="SenderAuthentication" />
    /// instead of being folded in here.
    /// </remarks>
    public static SenderTrust Unknown(SenderTrustPolicyRevision policyRevision) =>
        new(SenderTrustLevel.Unknown, SenderTrustSource.None, policyRevision);

    /// <summary>Records that this deployment recognized the message's authenticated author.</summary>
    /// <param name="grantedBy">Which half named the author.</param>
    /// <param name="policyRevision">The policy that was applied.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentException">Thrown when no half is named, which is not a trusted message.</exception>
    public static SenderTrust Trusted(
        SenderTrustSource grantedBy,
        SenderTrustPolicyRevision policyRevision)
    {
        if (grantedBy == SenderTrustSource.None)
        {
            throw new ArgumentException(
                "A trusted verdict names what recognized the author, so it cannot be reached by nothing.",
                nameof(grantedBy));
        }

        return new SenderTrust(SenderTrustLevel.Trusted, grantedBy, policyRevision);
    }
}
