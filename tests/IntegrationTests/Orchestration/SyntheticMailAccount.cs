// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AppHost;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Synchronization;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>The one throwaway mailbox the orchestrated mail server serves, as the adapter's two ports see it.</summary>
/// <remarks>
/// <para>
/// The suite supplies these ports itself rather than composing the host's configuration-bound ones, for the same reason
/// it does not start the host resource: what is under test is the mail adapter against a real server, not how a
/// composition root binds an options section. Both implementations are small enough that stating them here is clearer
/// than reaching into another assembly's internals for them.
/// </para>
/// <para>
/// The policy is the weakest one MailFathom will build, and deliberately: the server speaks plain IMAP on a container port
/// and offers no SASL mechanism, so reaching it requires the unencrypted-connection opt-in and the clear-text
/// authentication opt-in together. That combination is exactly what a test of the clear-text fallback needs to
/// exercise, and it is confined to a container that lives for one run.
/// </para>
/// </remarks>
internal sealed class SyntheticMailAccount(
    OrchestratedMailServerEndpoints endpoints,
    RemotelyDeletedEmailDisposition remotelyDeletedEmailDisposition =
        RemotelyDeletedEmailDisposition.RetainTombstone,
    bool auditTrailEnabled = false,
    bool answeringAuditTrailEnabled = false)
    : IImapAccountSettingsProvider,
    IMailTransportSecurityPolicyReader,
    IMailSynchronizationWindowReader,
    IRemotelyDeletedEmailDispositionReader,
    IAuthoredDeleteEmailDispositionReader,
    IMailboxMutationAuditSettingsReader,
    IMailAnsweringAuditSettingsReader
{
    /// <summary>The window this account keeps an answering entry for, which a retention test writes an older entry than.</summary>
    internal static readonly TimeSpan AnsweringAuditRetention = TimeSpan.FromDays(30);

    /// <summary>Gets the account identifier every occurrence this suite stores belongs to.</summary>
    public static MailAccountId AccountId { get; } = MailAccountId.Create("integration");

    /// <inheritdoc />
    /// <remarks>
    /// Off unless a test asks for it, which is the deployed default and the state every other test needs: an audit
    /// entry per finished mutation would otherwise accumulate across a suite that authors many of them. The retention
    /// is long enough that no orchestrated run erases what it just wrote.
    /// </remarks>
    public MailboxMutationAuditSettings GetAuditSettings(MailAccountId accountId) =>
        new(auditTrailEnabled, TimeSpan.FromDays(90));

    /// <inheritdoc />
    /// <remarks>
    /// Off unless a test asks for it, for the reason the trail above is: it is the deployed default, and an entry per
    /// answered question would otherwise accumulate across a suite that asks many of them. The window is a constant so
    /// a retention test can write an entry older than it rather than reconfigure the account.
    /// </remarks>
    public MailAnsweringAuditSettings GetAnsweringAuditSettings(MailAccountId accountId) =>
        new(answeringAuditTrailEnabled, AnsweringAuditRetention);

    /// <inheritdoc />
    /// <remarks>
    /// Unbounded, because every test seeds the mail it then expects a run to find. A bound would silently exclude a
    /// seeded email whenever the container's clock and the seeding date disagreed about the day, which would look like a
    /// synchronization defect rather than like the arrangement it was.
    /// </remarks>
    public MailSynchronizationWindow GetWindow(MailAccountId accountId) => MailSynchronizationWindow.Unbounded;

    /// <inheritdoc />
    /// <remarks>
    /// The configured default, and the only disposition under which a test can read back what an earlier run stored:
    /// erasing a local copy would let a folder this suite recreates take the mail an ordered test asserts on with it.
    /// What the other disposition does is decided by <c>MailboxReconciler</c> and covered where that decision is, in
    /// the unit suite. It is a constructor parameter for one case only — a test proving that this setting is *not*
    /// what decides the local outcome of a deletion MailFathom itself performed — and that test owns folders nothing
    /// else reads.
    /// </remarks>
    public RemotelyDeletedEmailDisposition GetDisposition(MailAccountId accountId) =>
        remotelyDeletedEmailDisposition;

    /// <inheritdoc />
    /// <remarks>
    /// Every delete this suite authors names its own disposition on the request, so nothing resolves one through this
    /// port. It is implemented because the port is part of the account's contract and a harness that answered none of
    /// it would let a production path resolve nothing where the host resolves something.
    /// </remarks>
    public AuthoredDeleteEmailDisposition GetAuthoredDeleteDisposition(MailAccountId accountId) =>
        AuthoredDeleteEmailDisposition.RetainLocalCopy;

    /// <inheritdoc />
    public MailTransportSecurityPolicy GetPolicy(MailAccountId accountId) => MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.None,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain, MailAuthenticationMechanism.Login],
            allowInsecureConnection: true,
            allowClearTextAuthenticationOverUnencryptedConnection: true),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    /// <inheritdoc />
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the resolved material passes to the connection attempt that requested it, which disposes it when the attempt ends.")]
    public Task<ImapAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken) =>
        Task.FromResult(new ImapAccountSettings(
            AccountId.Value,
            endpoints.ImapHost,
            endpoints.ImapPort,
            OrchestrationContract.MailServerAccountUserName,
            new MailAccountConnectionMaterial(
                ResolvedSecret.FromText(OrchestrationContract.MailServerAccountPassword),
                TrustedCertificateAuthority: null)));
}
