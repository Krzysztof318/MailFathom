// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves what the credential store's own statements do against a real PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Everything this class covers is written as SQL the provider does not compose for us: an <c>INSERT … SELECT</c> whose
/// <c>WHERE</c> carries the owner's existence and the per-owner ceiling, an <c>ON CONFLICT</c> naming a unique index by
/// its column, a row lock taken in a statement of its own inside a transaction, and an <c>UPDATE</c> conditioned on the
/// record the caller verified against. A substitute settles none of them: a conflict target that does not match the
/// index, an identifier PostgreSQL folds differently, and a lock that does not serialize what it was written to
/// serialize all answer correctly in memory and wrongly on a deployment.
/// </para>
/// <para>
/// The store is reached through the container rather than constructed, so what runs is the registration a deployment
/// runs. It takes no caller: <c>OwnerPasswordCredentialAdministration</c> is where the grant is checked and is covered
/// in the unit suite, and what is under test here is the row the statement leaves.
/// </para>
/// <para>
/// Each test provisions under usernames of its own, because the username index is deployment-wide and this class shares
/// a database with every other class in the collection.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOwnerPasswordCredentialTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many callers race for the last place under the ceiling.</summary>
    /// <remarks>More than two, because a pair can be serialized by the scheduler alone and would establish nothing about the lock.</remarks>
    private const int ConcurrentProvisioners = 6;

    private const string StoredHash = "$mf1$stored$orchestrated$";

    [Fact]
    public async Task CreateAsync_AUsernameNobodyHolds_ProvisionsACredentialTheListingReports()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var credentialId = Guid.CreateVersion7();
        var username = OwnerCredentialUsername.Create("orchestrated-provisioned");

        // Act
        var outcome = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                credentialId,
                SyntheticMailAccount.Owner,
                username,
                StoredHash,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.Written, outcome);

        var listed = await services.InScopeAsync(
            (scope, token) => Store(scope).ReadForOwnerAsync(SyntheticMailAccount.Owner, token),
            cancellationToken);

        var provisioned = Assert.Single(listed, credential => credential.Id == credentialId);
        Assert.Equal(username, provisioned.Username);
        Assert.True(provisioned.Enabled);
    }

    /// <summary>The username is unique across the deployment, and it is the unique index rather than a read that says so.</summary>
    [Fact]
    public async Task CreateAsync_AUsernameAlreadyHeld_IsRefusedByTheIndexRatherThanWritten()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var username = OwnerCredentialUsername.Create("orchestrated-taken");
        await ProvisionAsync(services, username, cancellationToken);

        // Act
        var second = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                Guid.CreateVersion7(),
                SyntheticMailAccount.Owner,
                username,
                StoredHash,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.UsernameTaken, second);
        Assert.Equal(1, await CountUnderAsync(services, username, cancellationToken));
    }

    /// <summary>An owner this deployment holds no record for is answered rather than raised, which is what the <c>EXISTS</c> subquery is for.</summary>
    [Fact]
    public async Task CreateAsync_AnOwnerTheDeploymentHoldsNoRecordFor_IsRefusedWithoutWriting()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var stranger = MailOwnerId.Create(Guid.CreateVersion7());
        var username = OwnerCredentialUsername.Create("orchestrated-unheld-owner");

        // Act
        var outcome = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                Guid.CreateVersion7(),
                stranger,
                username,
                StoredHash,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.UnknownOwner, outcome);
        Assert.Equal(0, await CountUnderAsync(services, username, cancellationToken));
    }

    /// <summary>
    /// The ceiling is enforced by a count inside the insert, and a count under READ COMMITTED sees neither of two
    /// concurrent inserts. What serializes them is the row lock the statement before it takes on the owner, so the
    /// second caller counts what the first committed — and one place under the ceiling admits one credential however
    /// many callers reach for it at once.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ManyCallersReachingForTheLastPlaceUnderTheCeiling_AdmitOne()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var contenderId = Guid.CreateVersion7();
        var contender = MailOwnerId.Create(contenderId);

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignOwner.ProvisionAsync(services, contenderId, cancellationToken));

            Assert.Equal(
                OwnerPasswordCredential.MaximumListedPerOwner - 1,
                await FillToOnePlaceLeftAsync(services, contender, cancellationToken));

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                $"{nameof(IOwnerPasswordCredentialStore)}.{nameof(IOwnerPasswordCredentialStore.CreateAsync)}",
                ConcurrentProvisioners,
                (ordinal, token) => services.InScopeAsync(
                    (scope, inner) => Store(scope).CreateAsync(
                        Guid.CreateVersion7(),
                        contender,
                        OwnerCredentialUsername.Create($"orchestrated-ceiling-race-{contenderId:N}-{ordinal}"),
                        StoredHash,
                        inner),
                    token),
                cancellationToken);

            // Assert
            var held = await CountForOwnerAsync(services, contender, cancellationToken);

            attempts.AssertSingleEffect(held - (OwnerPasswordCredential.MaximumListedPerOwner - 1));

            Assert.All(
                attempts.Results.Where(outcome => outcome != OwnerCredentialWriteOutcome.Written),
                static outcome => Assert.Equal(OwnerCredentialWriteOutcome.OwnerAtCredentialCeiling, outcome));
        }
        finally
        {
            await OrchestratedForeignOwner.EraseAsync(services, contenderId);
        }
    }

    /// <summary>
    /// A rehash spends two deliberately slow derivations, and an administrator rotating a leaked credential can commit
    /// inside that window — which is the case rotation exists for. The rehash therefore names the record it verified
    /// against, so what the rotation wrote is not overwritten by a request that read the record it replaced.
    /// </summary>
    [Fact]
    public async Task RewritePasswordHashAsync_ARecordAlreadyReplacedByARotation_WritesNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var username = OwnerCredentialUsername.Create("orchestrated-rehash-race");
        var credentialId = await ProvisionAsync(services, username, cancellationToken);
        const string Rotated = "$mf1$rotated$";

        await services.InScopeAsync(
            (scope, token) => Store(scope).ReplacePasswordAsync(
                SyntheticMailAccount.Owner,
                credentialId,
                Rotated,
                token),
            cancellationToken);

        // Act
        var rehash = await services.InScopeAsync(
            (scope, token) => Store(scope).RewritePasswordHashAsync(
                SyntheticMailAccount.Owner,
                credentialId,
                StoredHash,
                "$mf1$stronger$",
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.UnknownCredential, rehash);

        var stored = await services.InScopeAsync(
            (scope, token) => Store(scope).FindByUsernameAsync(username, token),
            cancellationToken);

        Assert.Equal(Rotated, stored!.PasswordHash);
    }

    /// <summary>The rehash the verification actually resolved does land, which is what says the refusal above is about the race rather than about the predicate refusing everything.</summary>
    [Fact]
    public async Task RewritePasswordHashAsync_TheRecordTheRequestVerifiedAgainst_IsRewritten()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var username = OwnerCredentialUsername.Create("orchestrated-rehash-settled");
        var credentialId = await ProvisionAsync(services, username, cancellationToken);
        const string Stronger = "$mf1$stronger$settled$";

        // Act
        var rehash = await services.InScopeAsync(
            (scope, token) => Store(scope).RewritePasswordHashAsync(
                SyntheticMailAccount.Owner,
                credentialId,
                StoredHash,
                Stronger,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.Written, rehash);

        var stored = await services.InScopeAsync(
            (scope, token) => Store(scope).FindByUsernameAsync(username, token),
            cancellationToken);

        Assert.Equal(Stronger, stored!.PasswordHash);
    }

    private static IOwnerPasswordCredentialStore Store(IServiceProvider scope) =>
        scope.GetRequiredService<IOwnerPasswordCredentialStore>();

    private static async Task<Guid> ProvisionAsync(
        OrchestratedMailFathomServices services,
        OwnerCredentialUsername username,
        CancellationToken cancellationToken)
    {
        var credentialId = Guid.CreateVersion7();

        var outcome = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                credentialId,
                SyntheticMailAccount.Owner,
                username,
                StoredHash,
                token),
            cancellationToken);

        Assert.Equal(OwnerCredentialWriteOutcome.Written, outcome);

        return credentialId;
    }

    /// <summary>Writes credentials until the owner holds one short of the ceiling, so the race above is for the last place.</summary>
    /// <returns>How many rows the statement wrote, which the caller asserts before it races for the place they leave.</returns>
    /// <remarks>
    /// Written as rows in one statement rather than through the store, because what is being arranged is a count and not
    /// the statement under test: ninety-nine round trips through the production write would cost the suite far more than
    /// the claim is worth, and the rows only have to satisfy the count the ceiling reads.
    /// </remarks>
    private static Task<int> FillToOnePlaceLeftAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) =>
            {
                var provisionedAt = DateTimeOffset.UnixEpoch;
                var usernamePrefix = $"orchestrated-ceiling-{owner.Value:N}-";

                return scope.GetRequiredService<MailFathomDbContext>().Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO owner_password_credentials
                         ("Id", "OwnerId", "Username", "PasswordHash", "Enabled", "Version", "CreatedAt", "PasswordChangedAt")
                     SELECT gen_random_uuid(), {owner.Value}, {usernamePrefix} || ordinal::text, {StoredHash}, TRUE, 1, {provisionedAt}, {provisionedAt}
                     FROM generate_series(1, {OwnerPasswordCredential.MaximumListedPerOwner - 1}) AS ordinal
                     """,
                    token);
            },
            cancellationToken);

    private static Task<int> CountForOwnerAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .OwnerPasswordCredentials
                .AsNoTracking()
                .CountAsync(credential => credential.OwnerId == owner.Value, token),
            cancellationToken);

    private static Task<int> CountUnderAsync(
        OrchestratedMailFathomServices services,
        OwnerCredentialUsername username,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .OwnerPasswordCredentials
                .AsNoTracking()
                .CountAsync(credential => credential.Username == username.Value, token),
            cancellationToken);
}
