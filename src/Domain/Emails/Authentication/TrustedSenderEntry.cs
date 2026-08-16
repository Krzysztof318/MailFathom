// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>One author an account's owner said this deployment recognizes.</summary>
/// <remarks>
/// <para>
/// An entry names either a whole domain or a single address, and the two are different claims. A domain entry says that
/// an author established as writing from that domain is recognized, which is the right shape for a counterparty whose
/// mail all comes from one organization and the wrong one for a correspondent at a provider everybody else uses too. An
/// address entry narrows that to one mailbox, at the cost described on <see cref="Matches" />.
/// </para>
/// <para>
/// Reaching under a domain is opt-in per entry rather than a mode the whole list runs in, because both answers are
/// defensible and a deployment needs both: an organization signing everything as one name wants its subdomains, and a
/// single host trusted inside a domain full of untrusted ones must not drag the rest in with it.
/// </para>
/// <para>
/// An entry is personal data — it names who somebody corresponds with — so nothing here may be logged.
/// </para>
/// </remarks>
public sealed record TrustedSenderEntry
{
    private TrustedSenderEntry(SenderDomain domain, bool includesSubdomains, string? normalizedLocalPart)
    {
        this.Domain = domain;
        this.IncludesSubdomains = includesSubdomains;
        this.NormalizedLocalPart = normalizedLocalPart;
    }

    /// <summary>Gets the domain this entry recognizes, which for an address entry is that address's domain.</summary>
    public SenderDomain Domain { get; }

    /// <summary>Gets whether the entry reaches the names beneath <see cref="Domain" /> as well as the domain itself.</summary>
    public bool IncludesSubdomains { get; }

    /// <summary>Gets the comparison form of the address's local part, or <see langword="null" /> for a domain entry.</summary>
    public string? NormalizedLocalPart { get; }

    /// <summary>Builds an entry that recognizes a whole domain.</summary>
    /// <param name="domain">The domain text an operator or a reader supplied.</param>
    /// <param name="includeSubdomains">Whether the entry also reaches the names beneath that domain.</param>
    /// <param name="entry">The entry, when the text is a domain this system compares on.</param>
    /// <returns><see langword="true" /> when the text is usable; otherwise <see langword="false" />.</returns>
    public static bool TryCreateForDomain(
        string? domain,
        bool includeSubdomains,
        [NotNullWhen(true)] out TrustedSenderEntry? entry)
    {
        entry = null;

        if (!SenderDomain.TryCreate(domain, out var trustedDomain))
        {
            return false;
        }

        entry = new TrustedSenderEntry(trustedDomain, includeSubdomains, normalizedLocalPart: null);

        return true;
    }

    /// <summary>Builds an entry that recognizes one mailbox.</summary>
    /// <param name="address">The address text an operator or a reader supplied.</param>
    /// <param name="entry">The entry, when the text is an address this system compares on.</param>
    /// <returns><see langword="true" /> when the text is usable; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The address is split the way every address in this system is, at the last at-sign, because a quoted local part
    /// may contain one. Its domain half is held in the same comparison form a domain entry's is, so an entry written in
    /// one encoding of an internationalized name recognizes a message that carried the other.
    /// </remarks>
    public static bool TryCreateForAddress(string? address, [NotNullWhen(true)] out TrustedSenderEntry? entry)
    {
        entry = null;

        if (!EmailAddress.TryCreate(displayName: null, address, out var mailbox)
            || !TrySplitMailbox(mailbox, out var localPart, out var domain))
        {
            return false;
        }

        entry = new TrustedSenderEntry(domain, includesSubdomains: false, localPart);

        return true;
    }

    /// <summary>Answers whether this entry recognizes one message's authenticated author.</summary>
    /// <param name="authorDomain">The domain of the author the receiving server's verdict established.</param>
    /// <param name="displayedSender">The address the message's <c>From</c> header displays, where it wrote a usable one.</param>
    /// <returns><see langword="true" /> when the entry names that author.</returns>
    /// <remarks>
    /// <para>
    /// A domain entry is held against the author's domain alone, which is the only identity established at all: what a
    /// receiving server authenticates is a domain, never a mailbox — DKIM signs as <c>d=</c>, SPF answers for the
    /// envelope sender's domain, and DMARC states that one of those held for the displayed domain.
    /// </para>
    /// <para>
    /// <b>An address entry therefore narrows an established domain by a local part nothing established.</b> It matches
    /// when the author's domain is the entry's own and the displayed address is exactly the entry's, so the claim it
    /// makes is: this domain wrote the message, and it presents it as coming from this mailbox of its own. That is worth
    /// something because a domain answerable for its own <c>From</c> is answerable for the whole address in it, and it
    /// is worth less than a domain entry, because a domain that can authenticate can display any local part it likes.
    /// What it never does is recognize a message whose displayed address is missing or unusable, which the caller cannot
    /// rule out from the domain alone.
    /// </para>
    /// </remarks>
    public bool Matches(SenderDomain authorDomain, EmailAddress? displayedSender)
    {
        if (this.NormalizedLocalPart is not { } localPart)
        {
            return authorDomain == this.Domain
                || (this.IncludesSubdomains && authorDomain.IsSubdomainOf(this.Domain));
        }

        return authorDomain == this.Domain
            && displayedSender is { } sender
            && TrySplitMailbox(sender, out var displayedLocalPart, out var displayedDomain)
            && displayedDomain == this.Domain
            && string.Equals(displayedLocalPart, localPart, StringComparison.Ordinal);
    }

    /// <summary>Writes the entry as the one line the policy revision is derived from.</summary>
    /// <returns>The comparison form of what this entry recognizes.</returns>
    /// <remarks>
    /// Every distinguishable entry writes a distinguishable line, since two entries producing one line would make a
    /// change to the list invisible in the revision. The prefixes are what keeps a domain, a domain reaching under
    /// itself, and an address apart.
    /// </remarks>
    public string ToPolicyStatement() => this.NormalizedLocalPart is { } localPart
        ? $"mailbox:{localPart}@{this.Domain.NormalizedValue}"
        : $"domain{(this.IncludesSubdomains ? "+sub" : string.Empty)}:{this.Domain.NormalizedValue}";

    /// <summary>Splits a mailbox into the two halves that are compared separately.</summary>
    /// <remarks>
    /// The local part takes the address's own comparison form, and the domain takes the domain type's, because the two
    /// halves normalize differently: only the domain has an encoding to settle.
    /// </remarks>
    private static bool TrySplitMailbox(EmailAddress mailbox, out string localPart, out SenderDomain domain)
    {
        localPart = string.Empty;
        domain = default;

        var separatorIndex = mailbox.Address.LastIndexOf('@');

        if (separatorIndex <= 0 || !SenderDomain.TryCreate(mailbox.Address[(separatorIndex + 1)..], out domain))
        {
            return false;
        }

        localPart = mailbox.Address[..separatorIndex].ToUpperInvariant();

        return true;
    }
}
