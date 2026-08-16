// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What the receiving mail server established about one message's sender.</summary>
/// <remarks>
/// <para>
/// Every message carries one of these, including the messages nothing could be established about: not established is a
/// verdict here rather than a missing value, because a deployment whose provider publishes no results has to be able to
/// tell that apart from mail whose sender was checked and failed.
/// </para>
/// <para>
/// <b>Two conclusions live here and they are not the same one.</b> <see cref="Outcome" /> answers whether an identity
/// authenticated, which is a fact about whoever handed the message over. <see cref="AuthorAuthentication" /> answers
/// whether the author a mail client displays authenticated, which is what an impersonation attempt exists to get wrong.
/// A relay, a mailing list, and a delivery provider all authenticate as themselves while carrying somebody else's
/// <c>From</c>, so the two disagreeing is an ordinary state of legitimate mail rather than a contradiction.
/// </para>
/// <para>
/// MailFathom verifies nothing itself. It resolves no DNS, evaluates no SPF policy, verifies no DKIM signature, computes
/// no organizational domain, consults no public suffix list, and reasons from no <c>Received</c> chain. Everything here
/// was read back out of one header written by the one server the account trusts, which is the only party in the chain
/// that observed the connection the message arrived on.
/// </para>
/// <para>
/// Every domain here is personal data. No log line, metric, or exception message may carry one; the occurrence identity,
/// <see cref="Outcome" />, and <see cref="AuthorAuthentication" /> are what those may report.
/// </para>
/// </remarks>
public sealed record SenderAuthentication
{
    private SenderAuthentication(
        SenderAuthenticationOutcome outcome,
        SenderAuthenticationMethod authenticatedBy,
        SenderDomain? authenticatedDomain,
        SenderDomain? dkimDomain,
        SenderDomain? spfDomain,
        SenderDomain? fromDomain,
        DmarcOutcome dmarc,
        IReadOnlyList<SenderDomain> authenticatedIdentities)
    {
        this.Outcome = outcome;
        this.AuthenticatedBy = authenticatedBy;
        this.AuthenticatedDomain = authenticatedDomain;
        this.DkimDomain = dkimDomain;
        this.SpfDomain = spfDomain;
        this.FromDomain = fromDomain;
        this.Dmarc = dmarc;

        (this.AuthorAuthentication, this.AuthenticatedAuthorDomain) =
            EstablishAuthor(fromDomain, dmarc, authenticatedIdentities);
    }

    /// <summary>Gets what was established about the identity that handed the message over.</summary>
    public SenderAuthenticationOutcome Outcome { get; }

    /// <summary>Gets which check established <see cref="AuthenticatedDomain" />, or none where nothing did.</summary>
    public SenderAuthenticationMethod AuthenticatedBy { get; }

    /// <summary>Gets the domain that authenticated, or <see langword="null" /> where none did.</summary>
    /// <remarks>
    /// Where both checks produced an identity and the two differ, this is the DKIM one. That is the authoritative claim
    /// because it is cryptographic — it says a key published by the signing domain signed these bytes — while SPF says
    /// only that a particular address was permitted to connect on behalf of the envelope sender, which a forwarding hop
    /// legitimately breaks and a shared relay legitimately satisfies for anybody using it. Both are kept, so a reader
    /// that cares about the difference still has it.
    /// </remarks>
    public SenderDomain? AuthenticatedDomain { get; }

    /// <summary>Gets the domain of a DKIM signature that verified, or <see langword="null" /> where none did.</summary>
    /// <remarks>
    /// A message may carry several signatures and each of them may verify. This names the first, as evidence of what the
    /// server did, and it is deliberately not what <see cref="AuthorAuthentication" /> was decided from: every verified
    /// signature is considered there, so an unrelated one arriving first cannot hide the one that establishes the author.
    /// </remarks>
    public SenderDomain? DkimDomain { get; }

    /// <summary>Gets the envelope-sender domain of an SPF check that passed, or <see langword="null" /> where none did.</summary>
    public SenderDomain? SpfDomain { get; }

    /// <summary>Gets the domain the message displays as its sender, or <see langword="null" /> when it wrote no usable one.</summary>
    /// <remarks>
    /// Recorded and never believed. It is attacker-controlled message content, so nothing is trusted for appearing here
    /// and no list is ever held against it; what it is for is that a message authenticated as one domain while claiming
    /// another is visible as exactly that.
    /// </remarks>
    public SenderDomain? FromDomain { get; }

    /// <summary>Gets the DMARC result the trusted header reported, or that it reported none.</summary>
    public DmarcOutcome Dmarc { get; }

    /// <summary>Gets what was established about the author the message displays, which is a separate conclusion.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="AuthorAuthenticationOutcome.Authenticated" /> is reached two ways and neither of them believes the
    /// <c>From</c> header on its own. A trusted <see cref="DmarcOutcome.Pass" /> is the receiving server's own statement
    /// that the displayed domain passed under its published policy, so the displayed domain is the answer. Failing
    /// that, an authenticated identity whose domain is exactly the displayed one is the same claim reached without
    /// DMARC — exactly, because a differing
    /// subdomain would need the sender's own policy to say whether relaxed alignment is permitted, and reading that
    /// policy is not something MailFathom does.
    /// </para>
    /// <para>
    /// <see cref="AuthorAuthenticationOutcome.Failed" /> comes from <see cref="DmarcOutcome.Fail" /> alone, and it ends
    /// the question rather than falling through to that second route: the receiving server reached it with the displayed
    /// domain's own published policy in hand, so it outranks an identity comparison made here without one. Every other
    /// DMARC result leaves the second route open, which is how most mail actually arrives.
    /// </para>
    /// </remarks>
    public AuthorAuthenticationOutcome AuthorAuthentication { get; }

    /// <summary>Gets the domain of the displayed author where it authenticated, or <see langword="null" />.</summary>
    /// <remarks>
    /// Present exactly when <see cref="AuthorAuthentication" /> is
    /// <see cref="AuthorAuthenticationOutcome.Authenticated" />, and then always the displayed domain rather than
    /// whichever identity established it. Anything deciding what to make of the <em>author</em> — a trust policy, a
    /// warning a reader is shown — reads this and never <see cref="AuthenticatedDomain" />.
    /// </remarks>
    public SenderDomain? AuthenticatedAuthorDomain { get; }

    /// <summary>Records that nothing was established about a message's sender.</summary>
    /// <param name="fromDomain">The domain the message displays as its sender, where it wrote a usable one.</param>
    /// <param name="dmarc">What the trusted header reported for DMARC, where one was read at all.</param>
    /// <returns>The verdict.</returns>
    /// <remarks>
    /// The DMARC result is carried even here, because a trusted header may state one for a message neither check
    /// authenticated — and a reader shown that a domain's own policy refused the message is being shown something
    /// stronger than silence. It reaches the author conclusion for the same reason: a trusted DMARC result is a
    /// statement about the displayed author whether or not anything else about the message was established.
    /// </remarks>
    public static SenderAuthentication NotEstablished(
        SenderDomain? fromDomain = null,
        DmarcOutcome dmarc = DmarcOutcome.NotReported) =>
        new(
            SenderAuthenticationOutcome.NotEstablished,
            SenderAuthenticationMethod.None,
            authenticatedDomain: null,
            dkimDomain: null,
            spfDomain: null,
            fromDomain,
            dmarc,
            authenticatedIdentities: []);

    /// <summary>Records that the receiving server checked an identity and it did not hold.</summary>
    /// <param name="fromDomain">The domain the message displays as its sender, where it wrote a usable one.</param>
    /// <param name="dmarc">What the trusted header reported for DMARC.</param>
    /// <returns>The verdict.</returns>
    public static SenderAuthentication Failed(SenderDomain? fromDomain, DmarcOutcome dmarc) =>
        new(
            SenderAuthenticationOutcome.Failed,
            SenderAuthenticationMethod.None,
            authenticatedDomain: null,
            dkimDomain: null,
            spfDomain: null,
            fromDomain,
            dmarc,
            authenticatedIdentities: []);

    /// <summary>Records the identities the receiving server verified.</summary>
    /// <param name="dkimDomains">Every domain whose DKIM signature verified, in the order the header reported them.</param>
    /// <param name="spfDomains">Every envelope-sender domain whose SPF check passed, in the same order.</param>
    /// <param name="fromDomain">The domain the message displays as its sender, where it wrote a usable one.</param>
    /// <param name="dmarc">What the trusted header reported for DMARC.</param>
    /// <returns>The verdict, naming the first DKIM domain as authoritative wherever one is present.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either collection is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when neither check produced a domain, which is not an authenticated message.</exception>
    /// <remarks>
    /// Both collections rather than one domain apiece, because one passing signature must never hide another. A message
    /// legitimately carries a delivery provider's signature beside its author's, and taking whichever the server listed
    /// first would leave the author unestablished on ordinary mail while establishing nothing an attacker could not also
    /// arrange. Which one is kept as <see cref="DkimDomain" /> is evidence and stays first-in-header-order, because the
    /// displayed author decides no part of what the verdict records about the transport.
    /// </remarks>
    public static SenderAuthentication Authenticated(
        IReadOnlyList<SenderDomain> dkimDomains,
        IReadOnlyList<SenderDomain> spfDomains,
        SenderDomain? fromDomain,
        DmarcOutcome dmarc)
    {
        ArgumentNullException.ThrowIfNull(dkimDomains);
        ArgumentNullException.ThrowIfNull(spfDomains);

        if (dkimDomains.Count == 0 && spfDomains.Count == 0)
        {
            throw new ArgumentException(
                "An authenticated verdict names the domain that authenticated, so at least one method must have produced one.",
                nameof(dkimDomains));
        }

        var dkimDomain = dkimDomains.Count > 0 ? dkimDomains[0] : default(SenderDomain?);
        var spfDomain = spfDomains.Count > 0 ? spfDomains[0] : default(SenderDomain?);

        return new SenderAuthentication(
            SenderAuthenticationOutcome.Authenticated,
            dkimDomain is null ? SenderAuthenticationMethod.SenderPolicyFramework : SenderAuthenticationMethod.DomainKeysIdentifiedMail,
            dkimDomain ?? spfDomain,
            dkimDomain,
            spfDomain,
            fromDomain,
            dmarc,
            [.. dkimDomains, .. spfDomains]);
    }

    /// <summary>Concludes what the trusted evidence establishes about the displayed author.</summary>
    /// <remarks>
    /// The order of the three questions is the rule. A DMARC failure is answered first because it is the receiving
    /// server's statement against the displayed domain, reached under that domain's own published policy, and nothing
    /// decided here outranks it. A message displaying no usable domain has no author to conclude anything about, which
    /// is why it stops at not established rather than at an identity comparison against nothing — but a DMARC failure
    /// still stands above that, since the server evaluated a displayed domain whether or not this reading could parse
    /// one. What is left is the two routes that establish an author, and the exact comparison is over every identity
    /// that authenticated rather than over the one kept as evidence.
    /// </remarks>
    private static (AuthorAuthenticationOutcome Outcome, SenderDomain? Domain) EstablishAuthor(
        SenderDomain? fromDomain,
        DmarcOutcome dmarc,
        IReadOnlyList<SenderDomain> authenticatedIdentities)
    {
        if (dmarc == DmarcOutcome.Fail)
        {
            return (AuthorAuthenticationOutcome.Failed, null);
        }

        if (fromDomain is not { } displayed)
        {
            return (AuthorAuthenticationOutcome.NotEstablished, null);
        }

        return dmarc == DmarcOutcome.Pass || authenticatedIdentities.Contains(displayed)
            ? (AuthorAuthenticationOutcome.Authenticated, displayed)
            : (AuthorAuthenticationOutcome.NotEstablished, null);
    }
}
