// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.Chunking;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Evaluations.Answering;
using MailFathom.Evaluations.Corpus;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Embeddings;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Synchronization;
using MailFathom.TestSupport;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;

namespace MailFathom.Evaluations.Retrieval;

/// <summary>
/// A database of its own on the PostgreSQL server a run names, holding the retrieval mailbox under the schema a
/// deployment migrates to and ranked by the readers a deployment ranks with.
/// </summary>
/// <remarks>
/// <para>
/// The database is created for one run and dropped by <see cref="DisposeAsync" />, so a server this is pointed at keeps
/// nothing it did not already hold, and two runs against one server never read each other's mail.
/// </para>
/// <para>
/// Each message is read from its raw MIME by the reader synchronization reads it with, and its row, its search
/// document, and its passages are written by the mapping and writers a stored message is written through. What is
/// written by hand is only what a deployment's own path would fetch from elsewhere: the occurrence's place in the
/// folder, the profile row, and the vectors, which come from the model under test rather than from a deployment's
/// provider. The message keeps the identifier the corpus gives it, which is what every case's evidence names.
/// </para>
/// </remarks>
internal sealed class RetrievalDatabase : IAsyncDisposable
{
    /// <summary>The variable naming the server, as an Npgsql connection string whose role may create a database.</summary>
    public const string ServerVariable = "MAILFATHOM_EVALUATION_DATABASE";

    private static readonly ImapUidValidity FolderUidValidity = ImapUidValidity.Create(1);

    private readonly string serverConnectionString;
    private readonly string databaseName;
    private readonly NpgsqlDataSource dataSource;
    private readonly TimeProvider timeProvider;

    private RetrievalDatabase(
        string serverConnectionString,
        string databaseName,
        NpgsqlDataSource dataSource,
        TimeProvider timeProvider)
    {
        this.serverConnectionString = serverConnectionString;
        this.databaseName = databaseName;
        this.dataSource = dataSource;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets the mail every ranking reads: the corpus account's inbox, resolved as a deployment resolves a readable scope.</summary>
    public static MailboxEmailSelection Selection { get; } = MailboxEmailSelection.Create(
        new MailboxScopeResolver(
                AssignedMailAccountCatalogs.For(
                    AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
                    SyntheticServedAccount.Of(CorpusKnowledgeSearch.Account)),
                StubMailFolderParticipation.Mapping(Folder),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing)
            .ReadableScope([], [MailFolderReference.ToAlias(CorpusKnowledgeSearch.Inbox)], JunkMailInclusion.Excluded),
        senderAddress: null,
        recipientAddress: null,
        subjectFragment: null,
        receivedOnOrAfter: null,
        receivedBefore: null,
        isRemotelySeen: null,
        isRemotelyFlagged: null,
        keyword: null,
        hasAttachments: null);

    private static MailFolderIdentity Folder => new(CorpusKnowledgeSearch.Account, CorpusKnowledgeSearch.Inbox);

    /// <summary>Creates a database on the server, brings it to the deployment's schema, and stores the mailbox in it.</summary>
    /// <param name="serverConnectionString">The server, reached through a role that may create and drop a database.</param>
    /// <param name="mailbox">The mail to store.</param>
    /// <param name="timeProvider">The clock the rows are stamped with.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The database, which the caller disposes to drop it.</returns>
    public static async Task<RetrievalDatabase> CreateAsync(
        string serverConnectionString,
        IReadOnlyList<CorpusMessage> mailbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var databaseName = $"mailfathom_retrieval_{Guid.CreateVersion7(timeProvider.GetUtcNow()):N}";
        var connectionString = ConnectionTo(serverConnectionString, databaseName);

        await ExecuteOnServerAsync(serverConnectionString, $"CREATE DATABASE \"{databaseName}\"", cancellationToken);

        // Migrated over a context of its own before the pool the rankings read through exists: the migration is what
        // creates the vector type, and a pool that met the database first would read an embedding as an unknown type.
        await using (var migrating = new MailFathomDbContext(
            MailFathomDbContextDesignTimeFactory.BuildOptions(connectionString, designTimeConnectionString: null),
            PostgresTextSearchConfiguration.Default))
        {
            await migrating.Database.MigrateAsync(cancellationToken);
        }

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();

        var database = new RetrievalDatabase(serverConnectionString, databaseName, dataSourceBuilder.Build(), timeProvider);

        try
        {
            await database.StoreAsync(mailbox, cancellationToken);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }

        return database;
    }

    /// <summary>Names a database on the server a connection string reaches, keeping everything else it says.</summary>
    /// <param name="serverConnectionString">The server's connection string.</param>
    /// <param name="databaseName">The database to name instead of whichever it named.</param>
    /// <returns>The connection string to that database.</returns>
    public static string ConnectionTo(string serverConnectionString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = databaseName }.ConnectionString;

    /// <summary>Reads every passage the mailbox was cut into, which is what a profile's vectors are written for.</summary>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>Each passage's identifier beside its text.</returns>
    public async Task<IReadOnlyList<(Guid ChunkId, string Text)>> ReadPassagesAsync(CancellationToken cancellationToken)
    {
        await using var context = this.NewContext();

        var passages = await context.EmailChunks
            .AsNoTracking()
            .OrderBy(static chunk => chunk.Id)
            .Select(static chunk => new { chunk.Id, chunk.Text })
            .ToArrayAsync(cancellationToken);

        return [.. passages.Select(static passage => (passage.Id, passage.Text))];
    }

    /// <summary>Makes one model's geometry the one serving, with a vector for every passage, as a deployment's activated profile is.</summary>
    /// <param name="identity">The profile's identity, whose width every vector has.</param>
    /// <param name="vectors">The vector of each passage, by the passage's identifier.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The profile the vectors are stored under.</returns>
    public async Task<RegisteredEmbeddingProfile> ServeAsync(
        EmbeddingProfileIdentity identity,
        IReadOnlyDictionary<Guid, EmbeddingVector> vectors,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(vectors);

        await using var context = this.NewContext();
        var now = this.timeProvider.GetUtcNow();

        // The partial unique index admits one serving generation, so the model measured before this one stands down
        // first, in its own statement, as it would when a deployment switches to a new profile.
        await context.EmbeddingProfiles
            .Where(static profile => profile.LifecycleState == EmbeddingProfileLifecycleState.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(static profile => profile.LifecycleState, EmbeddingProfileLifecycleState.Superseded)
                    .SetProperty(static profile => profile.SupersededAt, now),
                cancellationToken);

        var profile = new EmbeddingProfileEntity
        {
            Id = Guid.CreateVersion7(now),
            Provider = identity.Provider,
            ModelIdentifier = identity.ModelIdentifier,
            ModelVersion = identity.ModelVersion,
            Dimension = identity.Dimension,
            DistanceMetric = identity.DistanceMetric,
            InputCharacterLimit = identity.InputPreparation.InputCharacterLimit,
            PassageInstruction = identity.InputPreparation.PassageInstruction,
            NormalizesVector = identity.InputPreparation.NormalizesVector,
            IdentityFingerprint = EmbeddingProfileFingerprint.Compute(identity).Value,
            LifecycleState = EmbeddingProfileLifecycleState.Active,
            RegisteredAt = now,
            ActivatedAt = now,
        };

        context.EmbeddingProfiles.Add(profile);
        context.EmailEmbeddings.AddRange(vectors.Select(passage => new EmailEmbeddingEntity
        {
            EmailChunkId = passage.Key,
            EmbeddingProfileId = profile.Id,
            Dimension = identity.Dimension,
            Embedding = new Vector(passage.Value.Components),
            GeneratedAt = now,
        }));

        await context.SaveChangesAsync(cancellationToken);

        return new RegisteredEmbeddingProfile(EmbeddingProfileId.Create(profile.Id), identity);
    }

    /// <summary>Ranks the mailbox by the full-text index, as a deployment's lexical search ranks it.</summary>
    /// <param name="queryText">The query, matched as the search it stands for matches it.</param>
    /// <param name="limit">How many candidates the ranking hands back.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The candidates, best first.</returns>
    public async Task<IReadOnlyList<RankedEmailCandidate>> RankLexicallyAsync(
        EmailSearchQueryText queryText,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var context = this.NewContext();

        return await new StoredEmailSearchIndexReader(context, PostgresTextSearchConfiguration.Default)
            .ReadRankedCandidatesAsync(Selection, queryText, limit, cancellationToken);
    }

    /// <summary>Ranks the mailbox by pgvector's distance to a query's vector, as a deployment's semantic search ranks it.</summary>
    /// <param name="profile">The profile whose vectors are read.</param>
    /// <param name="queryVector">The query's vector, in that profile's space.</param>
    /// <param name="limit">How many candidates each ranking hands back.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The written and the depicted rankings, nearest first.</returns>
    public async Task<SemanticEmailRankings> RankSemanticallyAsync(
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var context = this.NewContext();

        return await new EmailVectorSearchIndexReader(context)
            .ReadNearestCandidatesAsync(Selection, profile, queryVector, limit, cancellationToken);
    }

    /// <summary>Drops the database and closes the pool that reached it.</summary>
    /// <returns>A task that completes when the database is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await this.dataSource.DisposeAsync();

        // Forced, because the pool the migration opened belongs to the context's own provider and is not this class's
        // to close, and a database is refused a drop while anything is still connected to it.
        await ExecuteOnServerAsync(
            this.serverConnectionString,
            $"DROP DATABASE IF EXISTS \"{this.databaseName}\" WITH (FORCE)",
            CancellationToken.None);
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "PostgreSQL takes no parameter for a database's name, and the only name written here is the one CreateAsync mints from a GUID.")]
    private static async Task ExecuteOnServerAsync(
        string serverConnectionString,
        string statement,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(serverConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ExtractedEmailMetadata> ExtractedAsync(
        MimeKitEmailMimeReader reader,
        CorpusMessage message,
        CancellationToken cancellationToken)
    {
        var extraction = await reader.ReadMetadataAsync(CorpusKnowledgeSearch.Account, message.RawMime, cancellationToken);

        var metadata = extraction.Metadata
            ?? throw new InvalidOperationException(
                $"The corpus message \"{message.Subject}\" was read as {extraction.Outcome} rather than extracted.");

        // A deployment reads the arrival time from the Received header the delivering server writes, and the corpus
        // was never delivered, so it carries none. The date it was written is its arrival in every other scenario, and
        // the rankings break a tie by it.
        return metadata with { ReceivedAt = message.ReceivedAt };
    }

    private MailFathomDbContext NewContext() => new(
        new DbContextOptionsBuilder<MailFathomDbContext>()
            .UseNpgsql(this.dataSource, static npgsql => npgsql.UseVector())
            .Options,
        PostgresTextSearchConfiguration.Default);

    private async Task StoreAsync(IReadOnlyList<CorpusMessage> mailbox, CancellationToken cancellationToken)
    {
        await using var context = this.NewContext();
        var now = this.timeProvider.GetUtcNow();
        var resolution = MailFolderResolution.FirstBindingOf(
            CorpusKnowledgeSearch.Inbox,
            RemoteFolderPath.Create(CorpusKnowledgeSearch.Inbox.Value));

        var folder = await MailFolderEntityResolver.AddAsync(context, CorpusKnowledgeSearch.Account, resolution, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var reader = new MimeKitEmailMimeReader(
            new EmailMimeExtractionOptions(),
            new NoTrustedAuthentication(),
            localSenderVerifier: null);
        var chunkWriter = new EmailChunkWriter(
            new DeterministicEmailTextChunker(),
            EmailChunkingRules.Current,
            EmbeddingInputBound.Default,
            new EmailEmbeddingTelemetry(),
            StubMailFolderParticipation.Mapping(Folder),
            this.timeProvider);

        foreach (var (message, position) in mailbox.Select(static (message, position) => (message, position)))
        {
            var metadata = await ExtractedAsync(reader, message, cancellationToken);
            var uid = ImapUid.Create((uint)position + 1);
            var stored = new StoredEmailEntity
            {
                Id = message.Id.Value,
                MailboxAccountId = CorpusKnowledgeSearch.Account.Value,
                MailFolder = folder,
                UidValidity = FolderUidValidity.Value,
                Uid = uid.Value,
                StoredAt = now,
            };

            context.StoredEmails.Add(stored);

            StoredEmailMetadataMapping.ApplyRemoteSummary(
                stored,
                new RemoteEmailMetadata(
                    EmailOccurrenceId.Create(CorpusKnowledgeSearch.Account, resolution.Id, FolderUidValidity, uid),
                    metadata.ThreadReferences.MessageId,
                    metadata.Subject,
                    metadata.SentAt,
                    message.RawMime.Length,
                    IsRemotelySeen: false),
                StoredEmailContentAvailability.Available);
            StoredEmailMetadataMapping.ApplyExtractedMetadata(stored, metadata);

            await EmailSearchDocumentWriter.SaveAsync(context, stored, metadata, now, cancellationToken);
            await chunkWriter.SaveAsync(context, stored, metadata.Text, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
