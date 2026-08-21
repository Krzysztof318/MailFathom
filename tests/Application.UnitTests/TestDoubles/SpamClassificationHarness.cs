// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Composes the real classifier and the real action recorder, over stores a test writes mail into.</summary>
/// <remarks>
/// One occurrence classified under a lease and a pass walking many of them are two use cases over one composition:
/// the same classifier, the same recorder, the same destination resolution, and the same header saying the mail is
/// junk. Holding that composition here is what keeps the two suites proving what each of them is about — a handler's
/// idempotency and a pass's walk — rather than each restating how a classification is assembled.
/// </remarks>
internal sealed class SpamClassificationHarness
{
    private static readonly MailTransportSecurityPolicy TlsOnConnect = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    /// <summary>Initializes the harness with the instant every verdict and every movement is stamped with.</summary>
    /// <param name="evaluatedAt">What the clock reads for the whole classification.</param>
    internal SpamClassificationHarness(DateTimeOffset evaluatedAt) => this.Clock = new FakeTimeProvider(evaluatedAt);

    /// <summary>Gets the mail the walk reads, which holds whatever a test stored in it.</summary>
    internal InMemoryClassifiableEmailReader Emails { get; } = new();

    /// <summary>Gets the verdicts written down, which is what a test reads a classification's outcome from.</summary>
    internal InMemoryEmailSpamClassificationStore Classifications { get; } = new();

    /// <summary>Gets the mutation records the actions opened, which is what proves a verdict was acted on.</summary>
    internal InMemoryMailboxMutationRecordStore Mutations { get; } = new();

    /// <summary>Gets the folder bindings a destination is resolved against.</summary>
    internal InMemoryMailFolderResolutionStore Bindings { get; } = new();

    /// <summary>Gets the content store the classifier reads a message through, which a suite arranges for itself.</summary>
    internal IEmailContentStore ContentStore { get; } = Substitute.For<IEmailContentStore>();

    /// <summary>Gets the clock every stamp is read from, which stands still unless a test advances it.</summary>
    internal FakeTimeProvider Clock { get; }

    /// <summary>Builds content the classification only hands to its collaborators, so the bytes themselves say nothing.</summary>
    /// <returns>The stored content.</returns>
    internal static StoredEmailContent SomeContent()
    {
        var rawMime = "Subject: synthetic\r\n\r\nA body nothing here reads.\r\n"u8.ToArray();

        return new StoredEmailContent(rawMime, rawMime.Length, SHA256.HashData(rawMime));
    }

    /// <summary>Answers with an occurrence in the account's inbox for whichever message the recorder asks about.</summary>
    /// <param name="accountId">The account the occurrence belongs to.</param>
    /// <param name="folderAlias">The folder the occurrence sits in.</param>
    /// <returns>The reader.</returns>
    internal static ISpamActionOccurrenceReader OccurrenceReader(MailAccountId accountId, MailFolderAlias folderAlias)
    {
        var reader = Substitute.For<ISpamActionOccurrenceReader>();
        reader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(call => new SpamActionOccurrence(
                call.Arg<StoredEmailId>(),
                EmailOccurrenceId.Create(
                    accountId,
                    new MailFolderResolutionId(folderAlias, MailFolderResolutionGeneration.First),
                    ImapUidValidity.Create(9),
                    ImapUid.Create(4401)),
                folderAlias,
                IsRemotelySeen: false));

        return reader;
    }

    /// <summary>Builds the real use case a verdict is scored through, over a header that says the mail is junk.</summary>
    /// <param name="settingsReader">What configuration says about which mail is classified.</param>
    /// <param name="commitPolicy">The policy every write is committed under.</param>
    /// <returns>The classifier.</returns>
    internal EmailSpamClassifier CreateClassifier(
        ISpamClassificationSettingsReader settingsReader,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        var headerReader = Substitute.For<IEmailSpamHeaderReader>();
        headerReader
            .ReadAsync(Arg.Any<StoredEmailContent>(), Arg.Any<CancellationToken>())
            .Returns(SpamHeaderFacts.Create(
                [],
                [new ProviderSpamHeaderValue("X-Spam-Status", "Yes, score=15.2 required=5.0")]));

        return new EmailSpamClassifier(
            this.Emails,
            this.ContentStore,
            headerReader,
            StubJunkMailFolderCatalog.None,
            new DeterministicSpamClassifier(),
            settingsReader,
            this.Classifications,
            Substitute.For<IEmailChunkStore>(),
            new RecordingDerivedWorkGateTelemetry(),
            commitPolicy,
            this.Clock);
    }

    /// <summary>Builds the real recorder a verdict is acted on through.</summary>
    /// <param name="actions">What configuration says is done about junk mail.</param>
    /// <param name="occurrences">Answers where the classified message sits on the server.</param>
    /// <param name="sessionFactory">Opens the sessions a movement is written under.</param>
    /// <param name="commitPolicy">The policy every write is committed under.</param>
    /// <returns>The recorder.</returns>
    internal SpamActionRecorder CreateActionRecorder(
        SpamActionSettings actions,
        ISpamActionOccurrenceReader occurrences,
        IPersistenceSessionFactory sessionFactory,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        var settingsReader = Substitute.For<ISpamActionSettingsReader>();
        settingsReader.Actions.Returns(actions);

        var dispositions = Substitute.For<IAuthoredDeleteEmailDispositionReader>();
        dispositions
            .GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>())
            .Returns(AuthoredDeleteEmailDisposition.RetainLocalCopy);

        return new SpamActionRecorder(
            settingsReader,
            occurrences,
            this.Mutations,
            this.CreateDestinationResolver(sessionFactory),
            dispositions,
            commitPolicy);
    }

    /// <summary>Resolves destinations over a server advertising nothing, because no suite here files anything.</summary>
    /// <param name="sessionFactory">Opens the sessions a folder binding is written under.</param>
    /// <returns>The resolver.</returns>
    internal MailboxDestinationResolver CreateDestinationResolver(IPersistenceSessionFactory sessionFactory)
    {
        var remoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>();
        remoteFolderCatalog
            .ListFoldersAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RemoteFolder>>([]));

        var transportSecurityPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
        transportSecurityPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(TlsOnConnect);

        return new MailboxDestinationResolver(
            StubMailFolderMappings.Nothing.Resolver,
            this.Bindings,
            new MailFolderResolver(
                remoteFolderCatalog,
                Substitute.For<IRemoteFolderCreator>(),
                this.Bindings,
                Substitute.For<IMailFolderMappingChangeAuditor>(),
                sessionFactory,
                this.Clock),
            transportSecurityPolicies);
    }

    /// <summary>Opens the sessions a classification writes under, every one of which commits.</summary>
    /// <returns>The factory.</returns>
    /// <remarks>Nothing here is about a conflict the policy has to retry, so a session that always commits is the whole of it.</remarks>
    internal IPersistenceSessionFactory CommittingSessions()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return sessionFactory;
    }

    /// <summary>Builds the policy every write in a classification is committed under.</summary>
    /// <param name="sessionFactory">Opens the sessions the policy commits.</param>
    /// <returns>The policy.</returns>
    internal OptimisticConcurrencyRetryPolicy CommitPolicyOver(IPersistenceSessionFactory sessionFactory) =>
        new(sessionFactory, new PersistenceConcurrencyOptions(), this.Clock);

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
