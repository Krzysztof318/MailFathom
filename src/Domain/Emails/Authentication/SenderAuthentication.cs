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
/// MailFathom verifies nothing itself. It resolves no DNS, evaluates no SPF policy, verifies no DKIM signature, and
/// reasons from no <c>Received</c> chain. Everything here was read back out of one header written by the one server the
/// account trusts, which is the only party in the chain that observed the connection the message arrived on.
/// </para>
/// <para>
/// Every domain here is personal data. No log line, metric, or exception message may carry one; the occurrence identity
/// and <see cref="Outcome" /> are what those may report.
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
        SenderDomainAlignment alignment)
    {
        this.Outcome = outcome;
        this.AuthenticatedBy = authenticatedBy;
        this.AuthenticatedDomain = authenticatedDomain;
        this.DkimDomain = dkimDomain;
        this.SpfDomain = spfDomain;
        this.FromDomain = fromDomain;
        this.Dmarc = dmarc;
        this.Alignment = alignment;
    }

    /// <summary>Gets what was established, which is the value everything above this reads first.</summary>
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
    public SenderDomain? DkimDomain { get; }

    /// <summary>Gets the envelope-sender domain of an SPF check that passed, or <see langword="null" /> where none did.</summary>
    public SenderDomain? SpfDomain { get; }

    /// <summary>Gets the domain the message displays as its sender, or <see langword="null" /> when it wrote no usable one.</summary>
    /// <remarks>
    /// Recorded and never believed. It is here so that a message authenticated as one domain while claiming to be from
    /// another is visible as exactly that, which <see cref="Alignment" /> states directly.
    /// </remarks>
    public SenderDomain? FromDomain { get; }

    /// <summary>Gets the DMARC result the trusted header reported, or that it reported none.</summary>
    public DmarcOutcome Dmarc { get; }

    /// <summary>Gets whether the authenticated domain is the displayed one.</summary>
    public SenderDomainAlignment Alignment { get; }

    /// <summary>Gets the domain of the displayed author where this verdict establishes it, or <see langword="null" />.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="AuthenticatedDomain" /> is an identity the receiving server checked; it says nothing about who the
    /// message displays as its author. A relay, a mailing list, and a delivery provider all authenticate as themselves
    /// while carrying somebody else's <c>From</c>, so anything deciding what to make of the <em>author</em> reads this
    /// and never that one.
    /// </para>
    /// <para>
    /// Two things establish an author here, and neither of them believes the header on its own. A trusted
    /// <see cref="DmarcOutcome.Pass" /> is the receiving server's own statement that the displayed domain passed under
    /// its published policy, so the displayed domain is the answer. Failing that, an authenticated identity whose domain
    /// is exactly the displayed one is the same claim reached without DMARC. Everything else — including
    /// <see cref="DmarcOutcome.Fail" />, which is a statement <em>against</em> the author — establishes nothing and
    /// answers <see langword="null" />.
    /// </para>
    /// <para>
    /// Only the one DKIM identity this verdict names is considered, so a message carrying a second signature over the
    /// displayed domain that the trusted header reported alongside an unrelated first one reads as establishing no
    /// author. That is the conservative direction: it withholds an author rather than inventing one.
    /// </para>
    /// </remarks>
    public SenderDomain? AuthenticatedAuthorDomain
    {
        get
        {
            if (this.FromDomain is not { } displayed)
            {
                return null;
            }

            if (this.Dmarc == DmarcOutcome.Pass)
            {
                return displayed;
            }

            var authenticatedAsDisplayed = this.Outcome == SenderAuthenticationOutcome.Authenticated
                && (this.DkimDomain == displayed || this.SpfDomain == displayed);

            return authenticatedAsDisplayed ? displayed : null;
        }
    }

    /// <summary>Records that nothing was established about a message's sender.</summary>
    /// <param name="fromDomain">The domain the message displays as its sender, where it wrote a usable one.</param>
    /// <param name="dmarc">What the trusted header reported for DMARC, where one was read at all.</param>
    /// <returns>The verdict.</returns>
    /// <remarks>
    /// The DMARC result is carried even here, because a trusted header may state one for a message neither check
    /// authenticated — and a reader shown that a domain's own policy refused the message is being shown something
    /// stronger than silence.
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
            SenderDomainAlignment.NotAssessed);

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
            SenderDomainAlignment.NotAssessed);

    /// <summary>Records the identity the receiving server verified.</summary>
    /// <param name="dkimDomain">The domain of a DKIM signature that verified, where one did.</param>
    /// <param name="spfDomain">The envelope-sender domain of an SPF check that passed, where one did.</param>
    /// <param name="fromDomain">The domain the message displays as its sender, where it wrote a usable one.</param>
    /// <param name="dmarc">What the trusted header reported for DMARC.</param>
    /// <returns>The verdict, naming the DKIM domain as authoritative wherever one is present.</returns>
    /// <exception cref="ArgumentException">Thrown when neither check produced a domain, which is not an authenticated message.</exception>
    public static SenderAuthentication Authenticated(
        SenderDomain? dkimDomain,
        SenderDomain? spfDomain,
        SenderDomain? fromDomain,
        DmarcOutcome dmarc)
    {
        if (dkimDomain is null && spfDomain is null)
        {
            throw new ArgumentException(
                "An authenticated verdict names the domain that authenticated, so at least one method must have produced one.",
                nameof(dkimDomain));
        }

        var authenticatedDomain = dkimDomain ?? spfDomain;

        return new SenderAuthentication(
            SenderAuthenticationOutcome.Authenticated,
            dkimDomain is null ? SenderAuthenticationMethod.SenderPolicyFramework : SenderAuthenticationMethod.DomainKeysIdentifiedMail,
            authenticatedDomain,
            dkimDomain,
            spfDomain,
            fromDomain,
            dmarc,
            AlignmentOf(authenticatedDomain, fromDomain));
    }

    /// <summary>Compares the authenticated domain with the displayed one, exactly.</summary>
    /// <remarks>
    /// Exact rather than organizational: <c>mail.example.test</c> and <c>example.test</c> are two names, and treating
    /// them as one here would quietly assert an alignment the receiving server never claimed. Where a sender's published
    /// policy does permit the relaxed form, the server's own DMARC result says so and is recorded beside this.
    /// </remarks>
    private static SenderDomainAlignment AlignmentOf(SenderDomain? authenticatedDomain, SenderDomain? fromDomain) =>
        (authenticatedDomain, fromDomain) switch
        {
            ({ } authenticated, { } displayed) when authenticated == displayed => SenderDomainAlignment.Aligned,
            (not null, not null) => SenderDomainAlignment.Misaligned,
            _ => SenderDomainAlignment.NotAssessed,
        };
}
