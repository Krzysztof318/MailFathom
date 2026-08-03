// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the schema artifact a release ships establishes the schema and can be applied again safely.</summary>
/// <remarks>
/// <para>
/// The artifact is the idempotent SQL script `aspire publish` writes for the app model's
/// <c>PublishAsMigrationScript(idempotent: true)</c>, and <c>scripts/build-schema-artifact.sh</c> names and checksums.
/// Generating it is <c>dotnet ef migrations script --idempotent</c>, which is this EF Core call with these options, so
/// the SQL under test here is the SQL the release publishes rather than a second script written to resemble it.
/// </para>
/// <para>
/// Each test owns a database of its own on the orchestrated server, because the suite's own database was migrated by
/// the orchestration before any test ran and applying the artifact to it would prove nothing about a clean apply. A
/// second database is not a second container topology: it is one <c>CREATE DATABASE</c> on the server the app model
/// already started.
/// </para>
/// <para>
/// What the two tests establish together is the operator's whole path in
/// <see href="../../../docs/operations/database-schema.md">the schema documentation</see>: an installation that has
/// never held the schema takes the complete chain and then satisfies the startup gate, and one that already carries
/// part of it takes only what it is missing without touching a row. The second is what makes the artifact safe to
/// apply when nobody is certain which migrations a given database holds.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSchemaArtifactTests(MailFathomOrchestrationFixture orchestration)
{
    [Fact]
    public async Task SchemaArtifact_AppliedToACleanDatabase_EstablishesTheSchemaTheStartupGateAccepts()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await this.CreateEmptyDatabaseAsync("mailfathom_artifact_clean", cancellationToken);
        using var host = ComposeHost(connectionString);
        await host.StartAsync(cancellationToken);

        using var scope = host.Services.CreateScope();
        var artifact = GenerateSchemaArtifact(scope.ServiceProvider);
        var definedMigrations = scope.ServiceProvider
            .GetRequiredService<MailFathomDbContext>()
            .Database.GetMigrations();

        // Act
        await ApplyAsync(connectionString, artifact, cancellationToken);

        // Assert
        var inspector = scope.ServiceProvider.GetRequiredService<IDatabaseSchemaInspector>();

        Assert.Equal(definedMigrations, await ReadAppliedMigrationsAsync(connectionString, cancellationToken));
        Assert.Empty(await inspector.ReadPendingMigrationIdentifiersAsync(cancellationToken));
        Assert.Equal(
            PostgresTextSearchConfiguration.Default.Value,
            await inspector.ReadSearchVectorTextSearchConfigurationAsync(cancellationToken));
        Assert.True(await ReadVectorExtensionInstalledAsync(connectionString, cancellationToken));

        await host.StopAsync(cancellationToken);
    }

    /// <summary>Proves a second apply over persisted mail takes nothing and destroys nothing.</summary>
    /// <remarks>
    /// This is the upgrade path expressed with the migrations that exist. An installation being upgraded holds the
    /// previous release's chain and representative mail, and the artifact has to apply only what is missing — which,
    /// while the chain is one migration long, is nothing at all. The rows are written through the production
    /// <see cref="MailFathomDbContext" /> rather than by hand, so what survives the second apply has gone through the
    /// mapping a release writes with, including the unsigned IMAP identity, the address arrays, and the raw MIME
    /// <c>bytea</c>.
    /// </remarks>
    [Fact]
    public async Task SchemaArtifact_AppliedAgainOverPersistedMail_RecordsNoFurtherMigrationAndKeepsTheRows()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await this.CreateEmptyDatabaseAsync("mailfathom_artifact_upgrade", cancellationToken);
        using var host = ComposeHost(connectionString);
        await host.StartAsync(cancellationToken);

        using var scope = host.Services.CreateScope();
        var artifact = GenerateSchemaArtifact(scope.ServiceProvider);
        await ApplyAsync(connectionString, artifact, cancellationToken);

        var context = scope.ServiceProvider.GetRequiredService<MailFathomDbContext>();
        var storedEmailId = await PersistRepresentativeMailAsync(context, cancellationToken);
        var migrationsAfterTheFirstApply = await ReadAppliedMigrationsAsync(connectionString, cancellationToken);

        // Act
        await ApplyAsync(connectionString, artifact, cancellationToken);

        // Assert
        var survivingEmail = await context.StoredEmails
            .AsNoTracking()
            .Include(email => email.MailFolder)
            .SingleAsync(email => email.Id == storedEmailId, cancellationToken);
        var survivingContent = await context.EmailMessageContents
            .AsNoTracking()
            .SingleAsync(content => content.StoredEmailId == storedEmailId, cancellationToken);

        Assert.Equal(
            migrationsAfterTheFirstApply,
            await ReadAppliedMigrationsAsync(connectionString, cancellationToken));
        Assert.Equal(uint.MaxValue, survivingEmail.Uid);
        Assert.Equal(["recipient@mailfathom.test"], survivingEmail.ToAddresses);
        Assert.Equal(RepresentativeRawMime, survivingContent.RawMime);
        Assert.Empty(await scope.ServiceProvider
            .GetRequiredService<IDatabaseSchemaInspector>()
            .ReadPendingMigrationIdentifiersAsync(cancellationToken));

        await host.StopAsync(cancellationToken);
    }

    /// <summary>The raw MIME the upgrade test writes, short enough to compare and long enough to be a real payload.</summary>
    private static byte[] RepresentativeRawMime =>
        "From: sender@mailfathom.test\r\nSubject: schema artifact\r\n\r\nBody.\r\n"u8.ToArray();

    /// <summary>Generates the idempotent script the release publishes, from the migrations this build defines.</summary>
    private static string GenerateSchemaArtifact(IServiceProvider scope) => scope
        .GetRequiredService<MailFathomDbContext>()
        .GetService<IMigrator>()
        .GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

    /// <summary>Composes the production registrations against a database this class created.</summary>
    /// <remarks>
    /// The same composition <see cref="OrchestratedDatabaseSchemaTests" /> uses, pointed at another database: a real
    /// host, because infrastructure composes the connection string during hosted-service startup.
    /// </remarks>
    private static IHost ComposeHost(string connectionString)
    {
        var builder = new HostApplicationBuilder();
        builder.Services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        builder.Services.AddInfrastructure(
            _ => new PostgresConnectionSettings(connectionString, null, null),
            PostgresTextSearchConfiguration.Default);

        return builder.Build();
    }

    /// <summary>Applies the artifact the way a client hands a script file to the server.</summary>
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The command text is the migration script EF Core generated from this build's own migration assembly, which is the artifact under test; parameterizing it would mean not applying it.")]
    private static async Task ApplyAsync(
        string connectionString,
        string artifact,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(artifact, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Reads the migration history the way the artifact wrote it, in the order the script applies.</summary>
    private static async Task<IReadOnlyList<string>> ReadAppliedMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";""",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var appliedMigrations = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            appliedMigrations.Add(reader.GetString(0));
        }

        return appliedMigrations;
    }

    /// <summary>Reads whether the artifact installed the extension the vector columns will need.</summary>
    /// <remarks>
    /// The privileged half of the apply, and the one an ordinary role cannot perform. Asserting it here is what makes
    /// the documented privilege requirement a property of the artifact rather than a claim about it.
    /// </remarks>
    private static async Task<bool> ReadVectorExtensionInstalledAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector');",
            connection);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Writes one account, folder, email, and raw MIME payload through the production mapping.</summary>
    private static async Task<Guid> PersistRepresentativeMailAsync(
        MailFathomDbContext context,
        CancellationToken cancellationToken)
    {
        var account = new MailboxAccountEntity { Id = "artifact-upgrade" };
        var folder = new MailFolderEntity
        {
            MailboxAccountId = account.Id,
            MailboxAccount = account,
            Alias = "inbox",
            RemotePath = "INBOX",
        };
        var storedEmail = new StoredEmailEntity
        {
            Id = Guid.CreateVersion7(),
            MailboxAccountId = account.Id,
            MailFolder = folder,
            UidValidity = 1,
            Uid = uint.MaxValue,
            Subject = "schema artifact",
            SizeOctets = RepresentativeRawMime.Length,
            ContentAvailability = StoredEmailContentAvailability.Available,
            SenderAddress = "sender@mailfathom.test",
            SenderNormalizedAddress = "sender@mailfathom.test",
            ToAddresses = ["recipient@mailfathom.test"],
        };

        context.MailboxAccounts.Add(account);
        context.MailFolders.Add(folder);
        context.StoredEmails.Add(storedEmail);
        context.EmailMessageContents.Add(new EmailMessageContentEntity
        {
            StoredEmailId = storedEmail.Id,
            StoredEmail = storedEmail,
            RawMime = RepresentativeRawMime,
            MimeByteLength = RepresentativeRawMime.Length,
            Sha256Hash = SHA256.HashData(RepresentativeRawMime),
        });

        await context.SaveChangesAsync(cancellationToken);

        return storedEmail.Id;
    }

    /// <summary>Creates an empty database on the orchestrated server and returns the connection string for it.</summary>
    /// <remarks>
    /// Dropped first, so a killed run cannot leave a half-applied database that turns the next run's clean apply into an
    /// upgrade of it. The orchestration connects as the server's superuser, which is why this can create a database and
    /// why the artifact's <c>CREATE EXTENSION</c> succeeds against it; a deployment grants that privilege deliberately
    /// and separately from the service's own role.
    /// </remarks>
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The database name is a compile-time constant of this class, and PostgreSQL accepts no parameter in the position a CREATE DATABASE names it.")]
    private async Task<string> CreateEmptyDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(orchestration.DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var drop = new NpgsqlCommand($"""DROP DATABASE IF EXISTS "{databaseName}" WITH (FORCE);""", connection))
        {
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var create = new NpgsqlCommand($"""CREATE DATABASE "{databaseName}";""", connection))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(orchestration.DatabaseConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;
    }
}
