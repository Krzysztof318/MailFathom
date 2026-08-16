// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Decides whether one account recognizes the author a message authenticated as.</summary>
/// <remarks>
/// <para>
/// The rule is deliberately narrow. An author is recognized when their domain belongs to an account this deployment
/// synchronizes, or when an entry on the receiving account's trusted-sender list names them. Everything else is unknown,
/// including most legitimate mail, because the claim being published is <em>this deployment does not know who wrote
/// this</em> rather than <em>this is suspicious</em>.
/// </para>
/// <para>
/// <b>Every configured account's domain counts, not only the receiving one.</b> An instance synchronizing a work
/// mailbox and a personal one is synchronizing one person's correspondence, and mail sent from the first to the second
/// is the least suspicious mail in the mailbox; recognizing it only against the receiving account's own domain would
/// leave the owner's own mail unrecognized. Whether the set is consulted at all is the deployment's choice, and a
/// policy built without it simply receives no domains.
/// </para>
/// <para>
/// <b>The trusted-sender list has two halves and this is the one matcher over both.</b> Configuration holds what an
/// operator declared when they set the deployment up and a store holds what somebody added while it was running, an
/// entry in either recognizes, and neither can undo the other — configuration is not editable at runtime and the store
/// is not editable by a configuration reload. The configured half is consulted first, so where both name one sender the
/// verdict reports the deployment's declared trust rather than something added later.
/// </para>
/// </remarks>
public sealed class SenderTrustPolicy
{
    private readonly HashSet<SenderDomain> ownAccountDomains;
    private readonly IReadOnlyList<TrustedSenderEntry> configuredEntries;
    private readonly IReadOnlyList<TrustedSenderEntry> storedEntries;

    private SenderTrustPolicy(
        HashSet<SenderDomain> ownAccountDomains,
        IReadOnlyList<TrustedSenderEntry> configuredEntries,
        IReadOnlyList<TrustedSenderEntry> storedEntries)
    {
        this.ownAccountDomains = ownAccountDomains;
        this.configuredEntries = configuredEntries;
        this.storedEntries = storedEntries;

        this.Revision = SenderTrustPolicyRevision.Of(
        [
            .. ownAccountDomains.Select(static domain => $"account:{domain.NormalizedValue}"),
            .. configuredEntries.Select(static entry => $"configured:{entry.ToPolicyStatement()}"),
            .. storedEntries.Select(static entry => $"stored:{entry.ToPolicyStatement()}"),
        ]);
    }

    /// <summary>Gets the policy that recognizes nobody, which is what an account this deployment no longer serves has.</summary>
    /// <remarks>
    /// It carries <see cref="SenderTrustPolicyRevision.None" />, so a verdict reached under it is indistinguishable
    /// from one no policy produced — which is the honest reading, since a deployment that has forgotten an account has
    /// no opinion about its mail rather than a negative one.
    /// </remarks>
    public static SenderTrustPolicy RecognizingNobody { get; } = new([], [], []);

    /// <summary>Gets the revision that names this policy, which every verdict it reaches is stored with.</summary>
    public SenderTrustPolicyRevision Revision { get; }

    /// <summary>Builds the policy one account judges its mail by.</summary>
    /// <param name="ownAccountDomains">The domains of the accounts this deployment synchronizes, or none where they do not count.</param>
    /// <param name="configuredTrustedSenders">What the account's configuration declares it recognizes.</param>
    /// <param name="storedTrustedSenders">What was added to the account's stored list while the deployment was running.</param>
    /// <returns>The policy, which recognizes nobody when all three are empty.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static SenderTrustPolicy Create(
        IEnumerable<SenderDomain> ownAccountDomains,
        IEnumerable<TrustedSenderEntry> configuredTrustedSenders,
        IEnumerable<TrustedSenderEntry> storedTrustedSenders)
    {
        ArgumentNullException.ThrowIfNull(ownAccountDomains);
        ArgumentNullException.ThrowIfNull(configuredTrustedSenders);
        ArgumentNullException.ThrowIfNull(storedTrustedSenders);

        return new SenderTrustPolicy(
            [.. ownAccountDomains],
            [.. configuredTrustedSenders],
            [.. storedTrustedSenders]);
    }

    /// <summary>Decides what this deployment makes of one message's author.</summary>
    /// <param name="authentication">What the receiving mail server established about the message.</param>
    /// <param name="displayedSender">The address the message's <c>From</c> header displays, where it wrote a usable one.</param>
    /// <returns>The verdict, always carrying <see cref="Revision" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authentication" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// <b>The subject of the decision is <see cref="SenderAuthentication.AuthenticatedAuthorDomain" /> and nothing
    /// else.</b> The <c>From</c> header on its own is written by whoever sent the message, so holding it against a list
    /// would let anybody be recognized by claiming to be; and the identity the receiving server happened to authenticate
    /// belongs to whoever handed the message over, which for a relay, a mailing list, or a delivery provider is not the
    /// author at all. Recognizing a provider one correspondent uses would otherwise recognize every message that
    /// provider ever relays, whoever it says wrote them.
    /// </para>
    /// <para>
    /// So a message whose author was not established — nothing authenticated, an authentication that failed, or an
    /// identity that authenticated as somebody other than the displayed author — reaches
    /// <see cref="SenderTrustLevel.Unknown" /> without the list being consulted at all. Which of those it was stays on
    /// <see cref="SenderAuthentication" />, where it was recorded.
    /// </para>
    /// </remarks>
    public SenderTrust Evaluate(SenderAuthentication authentication, EmailAddress? displayedSender)
    {
        ArgumentNullException.ThrowIfNull(authentication);

        if (authentication.AuthenticatedAuthorDomain is not { } authorDomain)
        {
            return SenderTrust.Unknown(this.Revision);
        }

        if (this.ownAccountDomains.Contains(authorDomain))
        {
            return SenderTrust.Trusted(SenderTrustSource.OwnAccountDomain, this.Revision);
        }

        if (Recognizes(this.configuredEntries, authorDomain, displayedSender))
        {
            return SenderTrust.Trusted(SenderTrustSource.ConfiguredTrustedSender, this.Revision);
        }

        return Recognizes(this.storedEntries, authorDomain, displayedSender)
            ? SenderTrust.Trusted(SenderTrustSource.StoredTrustedSender, this.Revision)
            : SenderTrust.Unknown(this.Revision);
    }

    /// <summary>Answers whether one half of the list names the author.</summary>
    private static bool Recognizes(
        IReadOnlyList<TrustedSenderEntry> entries,
        SenderDomain authorDomain,
        EmailAddress? displayedSender) =>
        entries.Any(entry => entry.Matches(authorDomain, displayedSender));
}
