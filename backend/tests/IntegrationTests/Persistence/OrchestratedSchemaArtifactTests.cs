// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Access;
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
/// What the tests establish together is the operator's whole path in
/// <see href="../../../docs/operations/database-schema.md">the schema documentation</see>: an installation that has
/// never held the schema takes the complete chain and then satisfies the startup gate, one that already carries part of
/// it takes only what it is missing without touching a row, and one that carries mail stored before the user axis
/// existed applies the whole chain over it and comes out with the stored copy discarded. The second is what makes the
/// artifact safe to apply when nobody is certain which migrations a given database holds; the third cannot be written
/// against the whole chain at once, because what it is about is the state between two of its migrations. The fourth
/// applies across a later pair the same way, carrying what each credential granted onto its user's roles.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSchemaArtifactTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The migration that introduces the user axis, which the carry-forward test applies across.</summary>
    private const string UserMigrationName = "AddUserAccounts";

    /// <summary>The migration that carries each credential's grant onto its user's roles, which the mail-grant test applies across.</summary>
    private const string MailGrantMigrationName = "HoldMailGrantsOnUsers";

    /// <summary>How the mail-grant test reads a credential list that names nothing, which no joined list of names can spell.</summary>
    private const string NamesNothing = "(none)";

    private const string CarriedAccount = "artifact-carry-forward";

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

        // No row of its own: a database that never held one comes out of the artifact holding no user either, and the
        // first user is the one an administrator records. It is asserted rather than left unsaid because the migration
        // that carries an existing deployment onto the user axis does write one, and a clean apply taking that branch
        // would provision a user nobody recorded.
        Assert.Empty(await scope.ServiceProvider
            .GetRequiredService<MailFathomDbContext>()
            .UserAccounts
            .AsNoTracking()
            .ToListAsync(cancellationToken));
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

    /// <summary>Proves a mailbox stored before the user axis existed is discarded with the accounts that moved, and the chain still applies over it.</summary>
    /// <remarks>
    /// The one claim in this class that a whole-chain apply cannot make: the row has to exist while the user column is
    /// added, filled, made required, and then dropped again by the migration that keys the mail graph by the account's
    /// generated identifier. A database that never held a mailbox row through that stretch exercises none of those
    /// steps, and every one of them is what an installation of an earlier release meets on the day it takes this one.
    /// So the chain is applied in two parts with the row written between them, and what this catches is a generated
    /// shape that fails over real data rather than over an empty schema.
    /// <para>
    /// What comes out is no mailbox at all, and that is the intended outcome rather than a loss this catches: the
    /// migration moving mail accounts into records of their own discards the stored copy instead of rewriting it,
    /// because a mailbox is resynchronized from the server it was read from. So the assertion is that the row is gone
    /// and the history is complete, which is what an operator's upgrade actually produces.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SchemaArtifact_AppliedOverAMailboxStoredBeforeTheUserMigration_DiscardsItAndRecordsTheWholeChain()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await this.CreateEmptyDatabaseAsync(
            "mailfathom_artifact_carry_forward",
            cancellationToken);
        using var host = ComposeHost(connectionString);
        await host.StartAsync(cancellationToken);

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MailFathomDbContext>();
        var releaseBeforeTheUserAxis = context.GetService<IMigrator>().GenerateScript(
            toMigration: MigrationPreceding(context, UserMigrationName),
            options: MigrationsSqlGenerationOptions.Idempotent);

        await ApplyAsync(connectionString, releaseBeforeTheUserAxis, cancellationToken);
        await InsertUnattributedMailboxAsync(connectionString, cancellationToken);

        // Act
        await ApplyAsync(connectionString, GenerateSchemaArtifact(scope.ServiceProvider), cancellationToken);

        // Assert
        Assert.Empty(await context.MailboxAccounts.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Equal(
            context.Database.GetMigrations(),
            await ReadAppliedMigrationsAsync(connectionString, cancellationToken));

        await host.StopAsync(cancellationToken);
    }

    /// <summary>Proves the grant each credential named is carried onto its user's roles, and what was carried stops narrowing.</summary>
    /// <remarks>
    /// Like the case above, it is a claim about rows that exist while one migration runs, so the chain is applied in
    /// two parts with the users and their credentials written between them. The five users are the five outcomes the
    /// migration distinguishes: a credential naming the whole mail half, credentials whose union is narrower, no
    /// credential at all, credentials that all name nothing, and a user whose roles were already assigned and whom the
    /// migration leaves alone. Every credential's list is read back, because a list the migration widened is the one
    /// outcome that would grant somebody more than they held before it.
    /// </remarks>
    [Fact]
    public async Task SchemaArtifact_AppliedOverCredentialsStoredBeforeGrantsMovedToUsers_CarriesEachGrantOntoItsUser()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await this.CreateEmptyDatabaseAsync(
            "mailfathom_artifact_mail_grants",
            cancellationToken);
        using var host = ComposeHost(connectionString);
        await host.StartAsync(cancellationToken);

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MailFathomDbContext>();
        var releaseBeforeGrantsMoved = context.GetService<IMigrator>().GenerateScript(
            toMigration: MigrationPreceding(context, MailGrantMigrationName),
            options: MigrationsSqlGenerationOptions.Idempotent);
        await ApplyAsync(connectionString, releaseBeforeGrantsMoved, cancellationToken);

        string[] wholeMailHalf = [.. MailFathomPermission.PublishedFor(ProtectedSurface.Mail).Select(permission => permission.Name)];
        var mailUser = await context.Roles.AsNoTracking().SingleAsync(role => role.Name == "Mail user", cancellationToken);
        var wholeHalf = await SeedUserAsync(
            connectionString,
            [wholeMailHalf, [MailFathomPermission.MailRead.Name]],
            assignedRole: null,
            cancellationToken);
        var narrower = await SeedUserAsync(
            connectionString,
            [[MailFathomPermission.MailAsk.Name], [MailFathomPermission.MailRead.Name]],
            assignedRole: null,
            cancellationToken);
        var withoutCredential = await SeedUserAsync(connectionString, [], assignedRole: null, cancellationToken);
        var alreadyAssigned = await SeedUserAsync(
            connectionString,
            [[MailFathomPermission.MailSend.Name]],
            mailUser.Id,
            cancellationToken);
        var namingNothing = await SeedUserAsync(connectionString, [[], []], assignedRole: null, cancellationToken);

        // Act
        await ApplyAsync(connectionString, GenerateSchemaArtifact(scope.ServiceProvider), cancellationToken);

        // Assert
        var assignments = await context.RoleAssignments.AsNoTracking().ToListAsync(cancellationToken);
        var credentials = await context.UserCredentials.AsNoTracking().ToListAsync(cancellationToken);
        var narrowerRole = Assert.Single(assignments, assignment => assignment.PrincipalUserId == narrower);

        Assert.All(assignments, assignment => Assert.Equal(assignment.PrincipalUserId, assignment.ScopeUserId));
        Assert.Equal(mailUser.Id, Assert.Single(assignments, assignment => assignment.PrincipalUserId == wholeHalf).RoleId);
        Assert.Equal(mailUser.Id, Assert.Single(assignments, assignment => assignment.PrincipalUserId == withoutCredential).RoleId);
        Assert.Single(assignments, assignment => assignment.PrincipalUserId == alreadyAssigned);
        Assert.StartsWith(
            "Carried-over mail grant ",
            (await context.Roles.AsNoTracking().SingleAsync(role => role.Id == narrowerRole.RoleId, cancellationToken)).Name,
            StringComparison.Ordinal);
        Assert.Equal(
            [MailFathomPermission.MailAsk.Name, MailFathomPermission.MailRead.Name],
            await context.RolePermissions.AsNoTracking()
                .Where(permission => permission.RoleId == narrowerRole.RoleId)
                .Select(permission => permission.Permission)
                .OrderBy(permission => permission)
                .ToListAsync(cancellationToken));
        Assert.DoesNotContain(assignments, assignment => assignment.PrincipalUserId == namingNothing);

        Assert.Equal([NamesNothing, MailFathomPermission.MailRead.Name], ListsHeldBy(wholeHalf));
        Assert.Equal([MailFathomPermission.MailAsk.Name, MailFathomPermission.MailRead.Name], ListsHeldBy(narrower));
        Assert.Equal([MailFathomPermission.MailSend.Name], ListsHeldBy(alreadyAssigned));
        Assert.Equal([string.Empty, string.Empty], ListsHeldBy(namingNothing));

        string[] ListsHeldBy(Guid user) =>
        [
            .. credentials
                .Where(credential => credential.UserId == user)
                .Select(credential => credential.Permissions is { } names ? string.Join(',', names) : NamesNothing)
                .Order(StringComparer.Ordinal),
        ];

        await host.StopAsync(cancellationToken);
    }

    /// <summary>Names the migration a database is left at so that the named migration is the next one it takes.</summary>
    private static string MigrationPreceding(MailFathomDbContext context, string migrationName)
    {
        string[] definedMigrations = [.. context.Database.GetMigrations()];
        var named = Array.FindIndex(
            definedMigrations,
            migration => migration.EndsWith(migrationName, StringComparison.Ordinal));

        return named > 0
            ? definedMigrations[named - 1]
            : throw new InvalidOperationException(
                $"The migration chain holds no {migrationName} with a migration before it, so there is no state to carry rows forward from.");
    }

    /// <summary>Writes a user the way a previous release holds one: an API key per list of names, and the role assigned on the user's own scope where one is given.</summary>
    /// <remarks>Written by hand rather than through the context, because the context maps the schema this build ends at and a later migration adding a column would make it write one this database does not have yet.</remarks>
    private static async Task<Guid> SeedUserAsync(
        string connectionString,
        string[][] credentialPermissions,
        Guid? assignedRole,
        CancellationToken cancellationToken)
    {
        var userId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            """
            INSERT INTO settings_accounts ("Id", "DisplayName", "Document", "Version", "CreatedAt", "UpdatedAt")
            VALUES ($1, $1::text, '{}', 1, now(), now());
            """,
            [userId],
            cancellationToken);

        foreach (var permissions in credentialPermissions)
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO user_credentials ("Id", "UserId", "Method", "Lookup", "Permissions", "Enabled", "Version", "CreatedAt", "MaterialChangedAt")
                VALUES ($1, $2, 'api-key', $1::text, $3, true, 1, now(), now());
                """,
                [Guid.CreateVersion7(), userId, permissions],
                cancellationToken);
        }

        if (assignedRole is { } role)
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO role_assignments ("Id", "RoleId", "PrincipalUserId", "ScopeUserId", "AssignedAt")
                VALUES ($1, $2, $3, $3, now());
                """,
                [Guid.CreateVersion7(), role, userId],
                cancellationToken);
        }

        return userId;
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Every statement is a literal of this class, and every value it carries travels as a parameter.")]
    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string statement,
        IReadOnlyList<object> values,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(statement, connection);
        command.Parameters.AddRange(values.Select(value => new NpgsqlParameter { Value = value }).ToArray());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Writes the mailbox row a previous release would hold, while the user column does not yet exist.</summary>
    private static async Task InsertUnattributedMailboxAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """INSERT INTO mailbox_accounts ("Id") VALUES ($1);""",
            connection);
        command.Parameters.AddWithValue(CarriedAccount);

        await command.ExecuteNonQueryAsync(cancellationToken);
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
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        builder.Services.AddInfrastructure(
            _ => new PostgresConnectionSettings(connectionString, null, null),
            PostgresTextSearchConfiguration.Default,
            MailAnsweringBudget.Default);

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
        var account = new MailboxAccountEntity { Id = "artifact-upgrade", };
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
