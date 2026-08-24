// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;

namespace MailFathom.TestSupport;

/// <summary>Builds the bounds a caller's own send is judged by, over a posture a test states.</summary>
/// <remarks>
/// Both submission use cases now pass them, so a test arranging a send that is meant to succeed would otherwise spell
/// the same permissive posture out in each suite. What a test states here is what an operator would have configured —
/// who may be written to, what one caller may ask for in a period, and what to do about a recipient nothing vouches for
/// — rather than a substitute of the governor itself, so a suite proving something about a send is held to the same
/// decision the deployment makes.
/// </remarks>
internal static class AuthoredSendGovernors
{
    /// <summary>Builds the bounds of a deployment that has configured nothing about what a caller may be talked into.</summary>
    /// <param name="authorization">The caller the send runs for, or <see langword="null" /> for one granted the send capability.</param>
    /// <returns>The governor a send passes.</returns>
    internal static AuthoredSendGovernor Permitting(AccessAuthorization? authorization = null) =>
        Governing(authorization: authorization);

    /// <summary>Builds the bounds a test states, each part defaulting to the posture that refuses nothing.</summary>
    /// <param name="recipientPolicy">Who this deployment may write to, or <see langword="null" /> for anybody.</param>
    /// <param name="settings">What to do about a recipient nothing here vouches for, or <see langword="null" /> to admit one.</param>
    /// <param name="ledger">What this caller has already been admitted for, or <see langword="null" /> for no ceiling at all.</param>
    /// <param name="contacts">The book an address is vouched against, or <see langword="null" /> for an empty one.</param>
    /// <param name="accounts">The accounts the caller's owner owns, or <see langword="null" /> for none.</param>
    /// <param name="senderIdentities">The addresses those accounts send as, or <see langword="null" /> for none.</param>
    /// <param name="auditor">Where the record of the send goes, or <see langword="null" /> to drop it.</param>
    /// <param name="authorization">The caller the send runs for, or <see langword="null" /> for one granted the send capability.</param>
    /// <param name="timeProvider">The clock a period is placed by, or <see langword="null" /> for the system clock.</param>
    /// <returns>The governor the submission use cases ask.</returns>
    internal static AuthoredSendGovernor Governing(
        OutgoingRecipientPolicy? recipientPolicy = null,
        AuthoredSendSettings? settings = null,
        AuthoredSendUsageLedger? ledger = null,
        IContactDirectory? contacts = null,
        ICallerMailAccountCatalog? accounts = null,
        IOutgoingSenderIdentityReader? senderIdentities = null,
        IAuthoredSendAuditor? auditor = null,
        AccessAuthorization? authorization = null,
        TimeProvider? timeProvider = null) =>
        new(
            recipientPolicy ?? OutgoingRecipientPolicy.Unrestricted,
            settings ?? AuthoredSendSettings.Permissive,
            new RecipientVouching(
                contacts ?? new VouchingNobody(),
                accounts ?? new OwningNobody(),
                senderIdentities ?? new SendingAsNobody()),
            ledger ?? new AuthoredSendUsageLedger(
                AuthoredSendCeilings.Unbounded,
                timeProvider ?? TimeProvider.System),
            auditor ?? new DiscardingAuditor(),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend),
            timeProvider ?? TimeProvider.System);

    /// <summary>A contact book holding nobody, which is what an installation that has recorded no correspondent has.</summary>
    private sealed class VouchingNobody : IContactDirectory
    {
        public Task<Contact?> FindAsync(ContactId contactId, CancellationToken cancellationToken) =>
            Task.FromResult<Contact?>(null);

        public Task<IReadOnlyDictionary<ContactId, Contact>> FindAllAsync(
            IReadOnlyCollection<ContactId> contactIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<ContactId, Contact>>(new Dictionary<ContactId, Contact>());

        public Task<Contact?> FindByAddressAsync(EmailAddress address, CancellationToken cancellationToken) =>
            Task.FromResult<Contact?>(null);

        public Task<IReadOnlyDictionary<ContactDisplayName, ContactMatch>> MatchDisplayNamesAsync(
            IReadOnlyCollection<ContactDisplayName> displayNames,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<ContactDisplayName, ContactMatch>>(
                new Dictionary<ContactDisplayName, ContactMatch>());

        public Task<IReadOnlyDictionary<EmailAddress, ContactId>> FindHoldersOfAsync(
            IReadOnlyCollection<EmailAddress> addresses,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<EmailAddress, ContactId>>(new Dictionary<EmailAddress, ContactId>());

        public Task<ContactPage> ReadPageAsync(ContactQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new ContactPage([], null));
    }

    /// <summary>An owner owning no account, which vouches for no address of their own.</summary>
    private sealed class OwningNobody : ICallerMailAccountCatalog
    {
        public IReadOnlyList<ServedMailAccount> OwnedAccounts { get; } = [];

        public bool SynchronizationEnabled => false;
    }

    /// <summary>A deployment whose accounts declare no sending address.</summary>
    private sealed class SendingAsNobody : IOutgoingSenderIdentityReader
    {
        public OutgoingSenderIdentity? FindSenderIdentity(MailAccountId accountId) => null;
    }

    /// <summary>An audit sink a test says nothing about, which keeps the record from being one more arrangement.</summary>
    private sealed class DiscardingAuditor : IAuthoredSendAuditor
    {
        public Task RecordAuthoredSendAsync(AuthoredSend send, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
