// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads which senders an account recognizes from the bound section.</summary>
/// <remarks>
/// The policies are built once for the snapshot this reader was constructed over rather than per lookup, because every
/// arriving message asks for one and building a policy derives a revision over the whole effective list. The build is
/// deferred rather than done in the constructor, so a deployment that never reads a message never walks the accounts.
/// </remarks>
internal sealed class ConfiguredSenderTrustPolicyReader : ISenderTrustPolicyReader
{
    private readonly MailSynchronizationOptions settings;

    private readonly Lazy<IReadOnlyDictionary<string, SenderTrustPolicy>> policiesByAccount;

    /// <summary>Initializes the reader over one snapshot of the mail section.</summary>
    /// <param name="settings">The snapshot the policies are read from.</param>
    internal ConfiguredSenderTrustPolicyReader(MailSynchronizationOptions settings)
    {
        this.settings = settings;
        this.policiesByAccount = new(this.ReadPolicies, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    /// <remarks>
    /// An account this snapshot no longer names recognizes nobody, for the reason the trusted authority answers with
    /// none: a reload can remove an account while a message of it is still being read.
    /// </remarks>
    public SenderTrustPolicy GetTrustPolicy(MailAccountId accountId) =>
        this.policiesByAccount.Value.TryGetValue(accountId.Value, out var policy)
            ? policy
            : SenderTrustPolicy.RecognizingNobody;

    /// <summary>Builds every account's verification policy once, keyed by the account identifier the lookups arrive with.</summary>
    /// <remarks>
    /// The stored half of each list is empty here and is what <see href="https://github.com/Krzysztof318/MailFathom/issues/760">#760</see>
    /// fills in: the matcher already takes both halves, so the store arrives as a second list rather than as a second
    /// rule. An entry whose text is unusable is skipped rather than raised over, and two accounts configured under one
    /// identifier keep the first, both for the reason the folder mappings do — startup validation refuses each of
    /// those, and a reload being rejected must not make a lookup throw.
    /// </remarks>
    private Dictionary<string, SenderTrustPolicy> ReadPolicies()
    {
        IReadOnlyList<SenderDomain> ownAccountDomains =
            this.settings.TrustOwnAccountDomains ? this.ReadOwnAccountDomains() : [];

        return (this.settings.Accounts ?? [])
            .Select(static account => (
                Id: MailSynchronizationOptions.TryReadAccountId(account.AccountId),
                account.ConfiguredTrustedSenders))
            .Where(static account => account.Id is not null)
            .GroupBy(static account => account.Id!, StringComparer.Ordinal)
            .ToDictionary(
                static account => account.Key,
                account => SenderTrustPolicy.Create(
                    ownAccountDomains,
                    account.First().ConfiguredTrustedSenders,
                    storedTrustedSenders: []),
                StringComparer.Ordinal);
    }

    /// <summary>Reads the domains the configured accounts themselves send and receive under.</summary>
    /// <remarks>
    /// Derived from each account's user name, which is the only mailbox identity an IMAP account states: a server is
    /// reached at a host that is rarely the mail domain, and the account identifier is a key an operator invented. An
    /// account whose user name is a bare login rather than an address therefore contributes nothing, which is the
    /// honest answer — inventing a domain out of the host would recognize senders nobody named.
    /// </remarks>
    private IReadOnlyList<SenderDomain> ReadOwnAccountDomains() =>
    [
        .. (this.settings.Accounts ?? [])
            .Select(static account => TryReadOwnDomain(account.UserName))
            .OfType<SenderDomain>()
            .Distinct(),
    ];

    /// <summary>Reads the mail domain one account's user name states, or nothing when it states none.</summary>
    /// <remarks>
    /// The at-sign is what separates the two shapes an IMAP user name takes. Without one the value is a login and
    /// naming a domain from it would be a guess; with one it is the mailbox address the account belongs to, and the
    /// domain is what follows the last at-sign for the reason every address in this system is split there.
    /// </remarks>
    private static SenderDomain? TryReadOwnDomain(string? userName) =>
        userName is not null
        && userName.Contains('@', StringComparison.Ordinal)
        && SenderDomain.TryCreateFromMailbox(userName, out var domain)
            ? domain
            : null;
}
