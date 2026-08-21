// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using NSubstitute;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Assembles the filing side of an outbox pass over doubles, so a test can say what the mailbox does.</summary>
/// <remarks>
/// <para>
/// Everything between the pass and the mail server is real: the filer, the destination resolution, and the store that
/// refuses a second copy. What is scripted is the two ends — which folders the account maps, and what the server
/// answers an <c>APPEND</c> with — because those are the facts a test is written about.
/// </para>
/// <para>
/// It files nothing by default. An account that maps no outbox folder and asks for no sent copy is the arrangement of
/// every test that is not about filing, and it leaves those tests saying exactly what they said before this existed.
/// </para>
/// </remarks>
internal sealed class OutgoingMailFilingHarness
{
    private static readonly MailTransportSecurityPolicy RequiredTlsPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    private readonly StubMailFolderMappings mappings = StubMailFolderMappings.Nothing;
    private readonly InMemoryMailFolderResolutionStore folderResolutions = new();
    private readonly IOutgoingMailFilingPolicyReader filingPolicies =
        Substitute.For<IOutgoingMailFilingPolicyReader>();

    internal OutgoingMailFilingHarness(
        InMemoryOutgoingEmailStore outgoingEmails,
        IEmailContentStore contentStore,
        MailOutboxSettings settings,
        TimeProvider clock)
    {
        this.Filings = new InMemoryOutgoingMailFilingStore(outgoingEmails);
        this.filingPolicies.FilesSentCopy(Arg.Any<MailAccountId>()).Returns(false);

        this.WriteSession = Substitute.For<IMailboxWriteSession>();
        this.WriteSession
            .AppendAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<AppendedMailFlags>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(this.AppendAnswer));

        var writeSessions = Substitute.For<IMailboxWriteSessionFactory>();
        writeSessions
            .OpenForWritingAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(this.WriteSession);

        var transportSecurityPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
        transportSecurityPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(RequiredTlsPolicy);

        var persistenceSession = Substitute.For<IPersistenceSession>();
        persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var persistenceSessions = Substitute.For<IPersistenceSessionFactory>();
        persistenceSessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);

        var destinations = new MailboxDestinationResolver(
            this.mappings.Resolver,
            this.folderResolutions,
            new MailFolderResolver(
                Substitute.For<IRemoteFolderCatalog>(),
                Substitute.For<IRemoteFolderCreator>(),
                this.folderResolutions,
                Substitute.For<IMailFolderMappingChangeAuditor>(),
                persistenceSessions,
                clock),
            transportSecurityPolicies);

        var commitPolicy = new OptimisticConcurrencyRetryPolicy(
            persistenceSessions,
            new PersistenceConcurrencyOptions(),
            clock);

        this.Filer = new OutgoingMailFiler(
            new MailboxCopyAppender(
                writeSessions,
                destinations,
                contentStore,
                transportSecurityPolicies,
                clock),
            writeSessions,
            destinations,
            this.Filings,
            transportSecurityPolicies,
            commitPolicy,
            clock);

        this.Pass = new OutgoingMailFilingPass(
            outgoingEmails,
            this.Filer,
            this.mappings,
            this.filingPolicies,
            clock,
            settings);
    }

    /// <summary>Gets the pass an outbox runs, which is what a test under this harness exercises.</summary>
    internal OutgoingMailFilingPass Pass { get; }

    /// <summary>Gets the filer itself, for a test about one append rather than about a pass.</summary>
    internal OutgoingMailFiler Filer { get; }

    /// <summary>Gets what the filer has written down about the copies it filed.</summary>
    internal InMemoryOutgoingMailFilingStore Filings { get; }

    /// <summary>Gets the write session every append goes through, so a test can assert what was asked of the server.</summary>
    internal IMailboxWriteSession WriteSession { get; }

    /// <summary>Gets or sets what the server answers an append with, which defaults to a server naming no placement.</summary>
    internal AppendedMailCopy AppendAnswer { get; set; } =
        new(RemoteEmailPlacement.NotReported(), InternetMessageId: null);

    /// <summary>Maps a folder to a role and binds it, which is what makes a copy filable into it.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="role">The role the folder plays.</param>
    /// <param name="alias">MailFathom's own name for it.</param>
    /// <param name="remotePath">The path the server holds it at, which defaults to the alias.</param>
    /// <returns>The binding, so a test can name the path it arranged.</returns>
    internal MailFolderResolution Map(
        MailAccountId accountId,
        MailFolderSpecialUse role,
        string alias,
        string? remotePath = null)
    {
        var folderAlias = MailFolderAlias.Create(alias);

        this.mappings.With(
            accountId,
            MailFolderMapping.ToRemotePath(
                folderAlias,
                RemoteFolderPath.Create(remotePath ?? alias),
                MailFolderParticipation.Full,
                mayCreateMissingFolder: false,
                role));

        return this.folderResolutions.Bind(accountId, folderAlias, remotePath);
    }

    /// <summary>Says that this account files a copy of everything it sends.</summary>
    /// <param name="accountId">The account asking for the copy.</param>
    internal void FileSentCopies(MailAccountId accountId) =>
        this.filingPolicies.FilesSentCopy(accountId).Returns(true);
}
