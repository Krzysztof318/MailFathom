// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AppHost;
using MailFathom.Application.Accounts;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
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
    bool answeringAuditTrailEnabled = false,
    IReadOnlyList<MailFolderIdentity>? foldersWithoutEmbeddings = null,
    IReadOnlyList<MailFolderIdentity>? foldersHiddenFromTools = null,
    IReadOnlyList<MailFolderIdentity>? foldersNotMirrored = null)
    : IImapAccountSettingsProvider,
    IMailTransportSecurityPolicyReader,
    IMailSynchronizationWindowReader,
    IRemotelyDeletedEmailDispositionReader,
    IAuthoredDeleteEmailDispositionReader,
    IMailboxMutationAuditSettingsReader,
    IMailAnsweringAuditSettingsReader,
    IMailAccountCatalog,
    IMailFolderParticipationReader,
    IMailFolderMappingReader,
    IJunkMailFolderCatalog
{
    /// <summary>Every folder alias this suite's configuration maps, which is every alias its tests bind one to.</summary>
    /// <remarks>
    /// <para>
    /// A folder no mapping names does not exist as far as MailFathom is concerned, so a harness that named none would
    /// make every read answer with nothing and every message reach the chunker unembedded. This is that configuration:
    /// one entry per alias a test class owns, which is what an operator's <c>…:Folders</c> list would hold for the same
    /// mailbox.
    /// </para>
    /// <para>
    /// A test class that introduces an alias adds it here. Forgetting shows up in that class alone and shows up as
    /// emptiness — a listing that returns no mail the same run stored, or a message cut into no passages — which is why
    /// the aliases are kept together rather than derived from whatever a run happened to bind.
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyList<string> MappedFolderAliases =
    [
        nameof(MailFolderSpecialUse.Inbox),
        "inbox",
        "a-folder-nobody-bound",
        "answering-audit-inbox",
        "ask-mail",
        "ask-mail-elsewhere",
        "audit-trail-inbox",
        "authored-delete",
        "content-inventory",
        "content-read",
        "content-store",
        "convergence-inbox",
        "createdarchive",
        "email-chunks",
        "email-chunks-unembedded",
        "email-embeddings",
        "embedding-backfill",
        "embedding-generation",
        "embedding-generations",
        "embedding-workload",
        "extraction-backfill",
        "hybrid-retrieval",
        "lexical-search",
        "manual-move-source",
        "manual-move-target",
        "mcp-tool-contract",
        "mutation-identity",
        "mutation-reconciliation",
        "mutation-record-inbox",
        "occurrence-identity",
        "persistence-session",
        "persistence-session-race",
        "persistence-session-retry",
        "push-watched",
        "reconciliation-erasure",
        "reconciliation-tombstone",
        "reconciliation-window",
        "relocation-source",
        "relocation-target",
        "rule-evaluation",
        "seen-state-provenance",
        "spam-scan",
        "stale-derived-data",
        "synchronized",
        "timeline-and-search",
        "timeline-read-model",
        "vector-search",
    ];

    /// <summary>An alias this configuration deliberately maps nothing to, which is what a folder MailFathom does not have looks like.</summary>
    /// <remarks>
    /// Kept beside the mapped aliases and deliberately absent from them, so a test that needs the unmapped case names
    /// this rather than inventing an alias somebody could later add to the list without noticing what it was for.
    /// </remarks>
    internal const string UnmappedFolderAlias = "no-mapping-names-this";

    /// <summary>Gets the account identifier every occurrence this suite stores belongs to.</summary>
    /// <remarks>
    /// Declared above every static that reads it, and that is load-bearing rather than tidy: static initializers run in
    /// the order they are written, so a folder set built above this line would name the default account — an empty
    /// identifier that narrows every account-scoped query to nothing and reads as a store that lost the mail the same
    /// run wrote, in every test at once rather than in the one that got it wrong.
    /// </remarks>
    public static MailAccountId AccountId { get; } =
        MailAccountId.Create(OrchestrationContract.ServedMailAccountId);

    private static readonly MailFolderMapping Inbox = MailFolderMapping.ToSpecialUse(
        MailFolderAlias.Create(nameof(MailFolderSpecialUse.Inbox)),
        MailFolderSpecialUse.Inbox);

    private static readonly IReadOnlyList<MailFolderIdentity> MappedFolders =
    [
        .. MappedFolderAliases
            .Select(static alias => new MailFolderIdentity(AccountId, MailFolderAlias.Create(alias)))
            .Distinct(),
    ];

    /// <summary>The window this account keeps an answering entry for, which a retention test writes an older entry than.</summary>
    internal static readonly TimeSpan AnsweringAuditRetention = TimeSpan.FromDays(30);

    /// <inheritdoc />
    /// <remarks>
    /// The one account this suite stores anything under. Every read model resolves its scope through this port before it
    /// reads a row, so a harness that answered nothing here would make a mailbox query return an empty window over mail
    /// the same run had just written — an arrangement failure that reads exactly like the query being wrong.
    /// </remarks>
    public IReadOnlyList<ServedMailAccount> ServedAccounts =>
        [new(AccountId, MailAccountDisplayName.Create(OrchestrationContract.ServedMailAccountDisplayName), MailSynchronizationMode.Polling)];

    /// <inheritdoc />
    /// <remarks>
    /// On, because every orchestrated test arranges a deployment that synchronizes: the flag reports the operator's
    /// switch and nothing in the suite exercises a deployment that turned it off.
    /// </remarks>
    public bool SynchronizationEnabled => true;

    /// <inheritdoc />
    /// <remarks>
    /// Every folder this suite maps, less the ones a test stopped mirroring. That is the list a pass over stored mail
    /// runs against, so it is what keeps two kinds of row out of one: mail of a folder whose synchronization was
    /// switched off, which a test arranges here, and mail of a folder outside <see cref="MappedFolderAliases" />, which
    /// no arrangement can put back because an unmapped alias is not a folder this deployment has.
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> FoldersSynchronized =>
        [.. MappedFolders.Where(folder => !(foldersNotMirrored ?? []).Contains(folder))];

    /// <inheritdoc />
    /// <remarks>
    /// Every folder this suite maps, less the ones a test withheld. The subtraction is stated that way round because a
    /// withholding is what a test arranges, while being mapped at all is what makes MailFathom have the folder: an
    /// alias outside <see cref="MappedFolderAliases" /> is not a folder this deployment has, and every read of it
    /// answers with nothing. The one class that withholds a mapped folder needs it, because whether the narrowing
    /// translates to SQL at all is settled by a real database and nothing else.
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> FoldersVisibleToTools =>
        [.. this.FoldersSynchronized.Where(folder => !(foldersHiddenFromTools ?? []).Contains(folder))];

    /// <inheritdoc />
    /// <remarks>
    /// Read the same way as the folders above. The one class that leaves a mapped folder unembedded names a folder of
    /// its own, because whether a message is cut into passages at all is settled by the rows a real transaction leaves
    /// behind.
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> FoldersGeneratingEmbeddings =>
        [.. this.FoldersSynchronized.Where(folder => !(foldersWithoutEmbeddings ?? []).Contains(folder))];

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
    /// Answered from the same mapped set the two lists above are read from, so the per-folder question and the
    /// per-query one cannot disagree — which is the property the production reader has and the one a read that reaches
    /// an email by its identifier depends on.
    /// </remarks>
    public MailFolderParticipation GetParticipation(MailAccountId accountId, MailFolderAlias folderAlias)
    {
        var folder = new MailFolderIdentity(accountId, folderAlias);

        return MappedFolders.Contains(folder)
            ? MailFolderParticipation.Create(
                this.FoldersSynchronized.Contains(folder),
                this.FoldersGeneratingEmbeddings.Contains(folder),
                this.FoldersVisibleToTools.Contains(folder))
            : MailFolderParticipation.Unmapped;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Nothing, because the one mapping this account carries plays the inbox role and no test maps a junk folder. That
    /// is a deployment an operator can have — the production reader answers with nothing for exactly the same reason,
    /// an account whose configuration names no junk folder — and it is what makes every mailbox read here behave as it
    /// did before junk was withheld from one. A test that needs the narrowing itself has to map a folder to the junk
    /// role first, because a catalog answering with nothing withholds nothing and would report the narrowing as working
    /// whatever the query did.
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> JunkFolders => [];

    /// <inheritdoc />
    /// <remarks>Answered from the same mapping the list above is read from, so the per-folder question and the per-query one cannot disagree.</remarks>
    public bool IsJunkFolder(MailAccountId accountId, MailFolderAlias folderAlias) => false;

    /// <inheritdoc />
    /// <remarks>
    /// Only the inbox is mapped, and only for the served account, because that is the one folder every test's
    /// arrangement shares; the folders a test creates for itself are named by alias everywhere they are used. Answering
    /// for an alias nothing mapped would make a role look answerable when nothing configured one.
    /// </remarks>
    public MailFolderMapping? FindFolderPlayingRole(MailAccountId accountId, MailFolderSpecialUse role) =>
        accountId == AccountId && Inbox.Plays(role) ? Inbox : null;

    /// <inheritdoc />
    public MailFolderMapping? FindFolderNamed(MailAccountId accountId, MailFolderAlias folderAlias) =>
        accountId == AccountId && Inbox.Alias == folderAlias ? Inbox : null;

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
