// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
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
/// Both claims need a real database. The read is one row reached by the key and carries the version a later write
/// would be accepted against, and nothing in a substitute would report a projection that had quietly become a scan or
/// dropped the version; the refusal is a unique index rather than a decision in any write path, so the only thing that
/// can demonstrate it is PostgreSQL declining the insert.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOwnerSettingsDocumentTests(MailFathomOrchestrationFixture orchestration)
{
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
        Assert.False(string.IsNullOrWhiteSpace(record.DisplayName));
        Assert.Equal("{}", record.Json);
        Assert.True(record.Version > 0);
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

                    context.OwnerAccounts.Add(NewOwner(Guid.NewGuid(), contested));
                },
                cancellationToken));

            // Assert
            Assert.IsType<DbUpdateException>(refused);
        }
        finally
        {
            await OrchestratedForeignOwner.EraseAsync(services, holder);
        }
    }

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
