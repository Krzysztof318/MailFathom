// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using NSubstitute;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Assembles the draft side of a deployment over doubles, so a test can say what the mailbox does.</summary>
/// <remarks>
/// <para>
/// Everything between the book and the mail server is real: the filer, the destination resolution, the record that
/// tracks each copy, and the store that holds the message. What is scripted is the two ends — which folder plays the
/// drafts role, and what the server answers an <c>APPEND</c> with — because those are the facts a test is written
/// about.
/// </para>
/// <para>
/// It maps no drafts folder by default, which is the arrangement of a test about a mailbox that cannot be reached.
/// <see cref="MapDraftsFolder" /> is what makes an append possible.
/// </para>
/// <para>
/// A destination resolver answers each role once and remembers it, because one belongs to one account run. So a test
/// about a folder that moved has to say which run met which arrangement, and <see cref="BeginNewScope" /> is that:
/// everything after it is a later run, with a resolver that asks again.
/// </para>
/// </remarks>
internal sealed class MailDraftHarness
{
    private static readonly MailTransportSecurityPolicy RequiredTlsPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    private readonly InMemoryMailFolderResolutionStore folderResolutions = new();
    private readonly IMailboxWriteSessionFactory writeSessions = Substitute.For<IMailboxWriteSessionFactory>();
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicies =
        Substitute.For<IMailTransportSecurityPolicyReader>();

    private readonly IPersistenceSessionFactory persistenceSessions = Substitute.For<IPersistenceSessionFactory>();
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly AccessAuthorization authorization;
    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly MailOutboxSettings settings;
    private readonly TimeProvider clock;

    private StubMailFolderMappings mappings = StubMailFolderMappings.Nothing;

    internal MailDraftHarness(
        TimeProvider clock,
        IOutgoingEmailStore outgoingEmails,
        MailOutboxSettings settings,
        params IEnumerable<MailFathomPermission> permissions)
    {
        this.clock = clock;
        this.outgoingEmails = outgoingEmails;
        this.settings = settings;

        var granted = permissions.ToArray();
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(AuthorizedPrincipal.Caller(
            "a-caller",
            granted.Length == 0
                ? [MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend]
                : granted));

        this.authorization = new AccessAuthorization(principals);

        this.WriteSession = Substitute.For<IMailboxWriteSession>();
        this.WriteSession
            .AppendAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<AppendedMailFlags>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                this.AppendCount++;

                return this.Append(this.AppendCount);
            });

        this.WriteSession
            .WithdrawAppendedAsync(Arg.Any<ImapUidValidity>(), Arg.Any<ImapUid>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var uidValidity = callInfo.ArgAt<ImapUidValidity>(0);
                var uid = callInfo.ArgAt<ImapUid>(1);

                // Recorded after the scripted answer rather than before it, so what the list holds is the occurrences
                // the server actually took back out — which is the claim a test about a replacement makes.
                await this.Withdraw(uidValidity, uid);

                this.Withdrawn.Add((uidValidity, uid));
            });

        this.writeSessions
            .OpenForWritingAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => this.WriteSession);

        this.transportSecurityPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(RequiredTlsPolicy);

        var persistenceSession = Substitute.For<IPersistenceSession>();
        persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        this.persistenceSessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);

        this.commitPolicy = new OptimisticConcurrencyRetryPolicy(
            this.persistenceSessions,
            new PersistenceConcurrencyOptions(),
            clock);

        this.BeginNewScope();
    }

    /// <summary>Gets the one way in, which is what a test about saving, revising, or giving up a draft exercises.</summary>
    internal MailDraftBook Book { get; private set; } = null!;

    /// <summary>Gets the filer itself, for a test about one append rather than about a whole save.</summary>
    internal MailDraftFiler Filer { get; private set; } = null!;

    /// <summary>Gets the pass that resumes whatever a stopped process left, which is where a crash is replayed.</summary>
    internal MailDraftPass Pass { get; private set; } = null!;

    /// <summary>Gets what has been written down about every draft and every copy of one.</summary>
    internal InMemoryMailDraftStore Drafts { get; } = new();

    /// <summary>Gets the messages the drafts are held as, which is what an append is issued from.</summary>
    internal InMemoryMailDraftContentStore Contents { get; } = new();

    /// <summary>Gets the write session every command goes through, so a test can assert what was asked of the server.</summary>
    internal IMailboxWriteSession WriteSession { get; }

    /// <summary>Gets how many appends the server has been asked for.</summary>
    internal int AppendCount { get; private set; }

    /// <summary>Gets every occurrence the server was asked to take back out, in the order it was asked.</summary>
    internal List<(ImapUidValidity UidValidity, ImapUid Uid)> Withdrawn { get; } = [];

    /// <summary>Gets or sets what the server answers each append with, by how many appends have been issued.</summary>
    /// <remarks>
    /// A function of the count rather than one fixed answer, because a replacement is two appends and a test about one
    /// has to be able to tell the copies apart by the occurrence each was placed at.
    /// </remarks>
    internal Func<int, Task<AppendedMailCopy>> Append { get; set; } = count => Task.FromResult(
        new AppendedMailCopy(
            RemoteEmailPlacement.Reported(ImapUidValidity.Create(1), ImapUid.Create((uint)count)),
            InternetMessageId: null));

    /// <summary>Gets or sets what the server does when asked to take a copy back out, which defaults to doing it.</summary>
    internal Func<ImapUidValidity, ImapUid, Task> Withdraw { get; set; } = (_, _) => Task.CompletedTask;

    /// <summary>Rebuilds everything a work unit owns, which is what makes the next call a later run.</summary>
    /// <remarks>
    /// Only the destination resolver's memory actually turns over, and that is the point: a folder that moved is met by
    /// the run after the move rather than by the one that was already holding an answer.
    /// </remarks>
    internal void BeginNewScope()
    {
        var destinations = new MailboxDestinationResolver(
            this.mappings.Resolver,
            this.folderResolutions,
            new MailFolderResolver(
                Substitute.For<IRemoteFolderCatalog>(),
                Substitute.For<IRemoteFolderCreator>(),
                this.folderResolutions,
                Substitute.For<IMailFolderMappingChangeAuditor>(),
                this.persistenceSessions,
                this.clock),
            this.transportSecurityPolicies);

        this.Filer = new MailDraftFiler(
            new MailboxCopyAppender(
                this.writeSessions,
                destinations,
                this.Contents,
                this.transportSecurityPolicies,
                this.clock),
            this.writeSessions,
            destinations,
            this.Drafts,
            this.transportSecurityPolicies,
            this.commitPolicy,
            this.clock);

        this.Book = new MailDraftBook(
            this.Drafts,
            this.Contents,
            this.commitPolicy,
            this.Filer,
            this.authorization,
            this.clock);

        this.Pass = new MailDraftPass(
            this.Drafts,
            this.outgoingEmails,
            this.Filer,
            this.commitPolicy,
            this.clock,
            this.settings);
    }

    /// <summary>Maps a folder to the drafts role and binds it, which is what makes a draft appendable at all.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="alias">MailFathom's own name for it.</param>
    /// <param name="remotePath">The path the server holds it at, which defaults to the alias.</param>
    /// <returns>The binding, so a test can name the path it arranged.</returns>
    internal MailFolderResolution MapDraftsFolder(
        MailAccountId accountId,
        string alias = "drafts",
        string? remotePath = null)
    {
        var folderAlias = MailFolderAlias.Create(alias);

        this.mappings = StubMailFolderMappings.Nothing.With(
            accountId,
            MailFolderMapping.ToRemotePath(
                folderAlias,
                RemoteFolderPath.Create(remotePath ?? alias),
                MailFolderParticipation.Full,
                mayCreateMissingFolder: false,
                MailFolderSpecialUse.Drafts));

        this.BeginNewScope();

        return this.folderResolutions.Bind(accountId, folderAlias, remotePath);
    }
}
