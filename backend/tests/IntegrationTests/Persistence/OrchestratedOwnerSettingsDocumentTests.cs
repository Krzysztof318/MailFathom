// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves what one owner's record is read as, and what the schema refuses to hold two of.</summary>
/// <remarks>
/// Every claim here needs a real database. The read is one row reached by the key and carries the version a later
/// write would be accepted against, and nothing in a substitute would report a projection that had quietly become a
/// scan or dropped the version; the label refusal is a unique index rather than a decision in any write path, so the
/// only thing that can demonstrate it is PostgreSQL declining the insert; and the ceiling is measured by the server
/// over its own rendering of the column, so a substitute would be asserting the arithmetic of the test instead.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOwnerSettingsDocumentTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The label the migration chain backfills onto the owner an upgraded deployment already served.</summary>
    /// <remarks>
    /// <c>docs/operations/database-schema.md</c> publishes this word to operators, and the only thing that writes it
    /// is this change's own backfill, so a migration writing some other label is a documented fact that quietly
    /// stopped being true.
    /// </remarks>
    private const string ProvisionedOwnerLabel = "owner";

    /// <summary>The provisioned owner is read as an envelope with the empty document their row was created with.</summary>
    [Fact]
    public async Task ReadAsync_TheOwnerTheDeploymentHolds_IsOneRowCarryingItsLabelVersionAndMarker()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var record = await services.InScopeAsync(
            async (scope, token) =>
            {
                var owner = await scope.GetRequiredService<MailFathomDbContext>()
                    .OwnerAccounts
                    .AsNoTracking()
                    .OrderBy(candidate => candidate.CreatedAt)
                    .Select(candidate => candidate.Id)
                    .FirstAsync(token);

                return await scope.GetRequiredService<IOwnerSettingsDocumentReader>()
                    .ReadAsync(MailOwnerId.Create(owner), token);
            },
            cancellationToken);

        // Assert
        Assert.NotNull(record);
        Assert.Equal(ProvisionedOwnerLabel, record.DisplayName);
        Assert.Equal("{}", record.Json);
        Assert.Equal(1, record.Version);
        Assert.False(record.WrittenAtRuntime);
    }

    /// <summary>An owner this deployment holds no record of is an absence rather than an empty record.</summary>
    [Fact]
    public async Task ReadAsync_AnOwnerTheDeploymentHoldsNoRecordOf_IsAbsent()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var unheldOwner = MailOwnerId.Create(Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"));

        // Act
        var record = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOwnerSettingsDocumentReader>().ReadAsync(unheldOwner, token),
            cancellationToken);

        // Assert
        Assert.Null(record);
    }

    /// <summary>Two owners under one label is refused by the schema, so a list of owners can be read.</summary>
    /// <remarks>
    /// The owner this test provisions is erased in a <c>finally</c>, including on a failure, for the reason
    /// <c>OrchestratedForeignOwner</c> states: a deployment whose accounts come from configuration holds exactly one
    /// owner record, and a second one left in <c>settings_accounts</c> refuses the folder bindings every later class in
    /// this collection arranges. It is what keeps the first test in this class reading the provisioned owner too.
    /// </remarks>
    [Fact]
    public async Task Insert_ASecondOwnerUnderALabelAlreadyHeld_IsRefusedByTheDatabase()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var contested = $"owner-{Guid.NewGuid():N}";
        var holder = Guid.NewGuid();

        // The identifier the refused insert was given, held for the same reason the first one is: were the unique
        // index ever missing — the regression this test exists to catch — that insert would commit, and a row nothing
        // holds the identifier for is one nothing can erase.
        var contender = Guid.NewGuid();

        try
        {
            await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    context.OwnerAccounts.Add(NewOwner(holder, contested));
                },
                cancellationToken);

            // Act
            var refused = await Record.ExceptionAsync(() => services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    context.OwnerAccounts.Add(NewOwner(contender, contested));
                },
                cancellationToken));

            // Assert
            Assert.IsType<DbUpdateException>(refused);
        }
        finally
        {
            await OrchestratedForeignOwner.EraseAsync(services, holder);
            await OrchestratedForeignOwner.EraseAsync(services, contender);
        }
    }

    /// <summary>The marker is read as the row holds it, so a written record is told from one nobody has written.</summary>
    /// <remarks>
    /// The column exists to separate an unfilled row from one an owner emptied — the same empty document, different
    /// facts — and a class that only ever observed it clear would stay green with the marker dropped from the
    /// projection or read out of the wrong ordinal. So it is set on a foreign owner and read back through the port,
    /// beside the assertion that the provisioned owner's is clear.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_AnOwnerWhoseDocumentWasWrittenAtRuntime_CarriesTheMarkerSet()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var written = Guid.NewGuid();

        try
        {
            await OrchestratedForeignOwner.ProvisionAsync(services, written, cancellationToken);

            await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);
                    var record = await context.OwnerAccounts.SingleAsync(owner => owner.Id == written, token);

                    record.DocumentWrittenAtRuntime = true;
                },
                cancellationToken);

            // Act
            var record = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOwnerSettingsDocumentReader>()
                    .ReadAsync(MailOwnerId.Create(written), token),
                cancellationToken);

            // Assert
            Assert.NotNull(record);
            Assert.True(record.WrittenAtRuntime);
        }
        finally
        {
            await OrchestratedForeignOwner.EraseAsync(services, written);
        }
    }

    /// <summary>A record past what this build binds is refused by the statement rather than expanded in the process.</summary>
    /// <remarks>
    /// Only a real server settles this: the bound is <c>octet_length</c> over PostgreSQL's own rendering of the
    /// column, so the gate in the statement and the check the reader makes on the length it was sent are the same
    /// number measured twice, and a disagreement between them would hand a caller a null column rather than the
    /// refusal an operator is told to expect. The owner is provisioned and erased here like every other one this
    /// class seeds.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ARecordPastWhatThisBuildBinds_IsRefusedRatherThanRead()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var overfilled = Guid.NewGuid();

        try
        {
            await OrchestratedForeignOwner.ProvisionAsync(services, overfilled, cancellationToken);

            await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);
                    var record = await context.OwnerAccounts.SingleAsync(owner => owner.Id == overfilled, token);

                    record.Document = DocumentPastTheCeiling();
                },
                cancellationToken);

            // Act
            var refused = await Record.ExceptionAsync(() => services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOwnerSettingsDocumentReader>()
                    .ReadAsync(MailOwnerId.Create(overfilled), token),
                cancellationToken));

            // Assert
            var unreadable = Assert.IsType<OwnerSettingsUnreadableException>(refused);
            Assert.Equal(MailFathomErrorCode.OwnerSettingsUnreadable, unreadable.ErrorCode);
            Assert.Contains(overfilled.ToString(), unreadable.Message, StringComparison.Ordinal);
        }
        finally
        {
            await OrchestratedForeignOwner.EraseAsync(services, overfilled);
        }
    }

    /// <summary>Composes one owner document larger than the ceiling, as one setting rather than as many.</summary>
    /// <remarks>
    /// One long value rather than a page of keys, because what this test is about is the ceiling rather than the
    /// difference between the compact form and the stored rendering — a single pair renders at the same length either
    /// way but for one space, so the margin below is what keeps the arrangement past the bound whichever is measured.
    /// </remarks>
    private static string DocumentPastTheCeiling() =>
        $$"""{"Filler":"{{new string('f', OwnerSettingsDocument.MaximumOctets + 1024)}}"}""";

    private static OwnerAccountEntity NewOwner(Guid ownerId, string displayName) => new()
    {
        Id = ownerId,
        DisplayName = displayName,
        Document = "{}",
        Version = 1,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
