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
/// its columns, a row lock taken in a statement of its own inside a transaction, and an <c>UPDATE</c> conditioned on the
/// record the caller verified against. A substitute settles none of them: a conflict target that does not match the
/// index, an identifier PostgreSQL folds differently, and a lock that does not serialize what it was written to
/// serialize all answer correctly in memory and wrongly on a deployment.
/// </para>
/// <para>
/// The store is reached through the container rather than constructed, so what runs is the registration a deployment
/// runs. It takes no caller: <c>OwnerCredentialAdministration</c> is where the grant is checked and is covered in the
/// unit suite, and what is under test here is the row the statement leaves.
/// </para>
/// <para>
/// Each test provisions under lookups of its own, because the lookup index is deployment-wide and this class shares a
/// database with every other class in the collection. The index is over the method beside the lookup, which is why one
/// test provisions the same value under two methods and expects both to land.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOwnerCredentialTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many callers race for the last place under the ceiling.</summary>
    /// <remarks>More than two, because a pair can be serialized by the scheduler alone and would establish nothing about the lock.</remarks>
    private const int ConcurrentProvisioners = 6;

    private const string StoredHash = "$mf1$stored$orchestrated$";

    private static readonly IReadOnlyList<MailFathomPermission> WholeMailSurface =
        MailFathomPermission.PublishedFor(ProtectedSurface.Mail);

    [Fact]
    public async Task CreateAsync_ALookupNobodyHolds_ProvisionsACredentialTheListingReports()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var credentialId = Guid.CreateVersion7();
        var lookup = OwnerCredentialLookup.ForUsername(OwnerCredentialUsername.Create("orchestrated-provisioned"));

        // Act
        var outcome = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                credentialId,
                SyntheticMailAccount.Owner,
                OwnerCredentialMethod.Password,
                lookup,
                StoredHash,
                WholeMailSurface,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.Written, outcome);

        var listed = await services.InScopeAsync(
            (scope, token) => Store(scope).ReadForOwnerAsync(SyntheticMailAccount.Owner, token),
            cancellationToken);

        var provisioned = Assert.Single(listed, credential => credential.Id == credentialId);
        Assert.Equal(lookup, provisioned.Lookup);
        Assert.Equal(OwnerCredentialMethod.Password, provisioned.Method);
        Assert.Equal(WholeMailSurface, provisioned.Permissions);
        Assert.True(provisioned.Enabled);
    }

    /// <summary>A lookup is unique within its method across the deployment, and it is the unique index rather than a read that says so.</summary>
    [Fact]
    public async Task CreateAsync_ALookupAlreadyHeldUnderTheSameMethod_IsRefusedByTheIndexRatherThanWritten()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var lookup = OwnerCredentialLookup.ForUsername(OwnerCredentialUsername.Create("orchestrated-taken"));
        await ProvisionAsync(services, OwnerCredentialMethod.Password, lookup, cancellationToken);

        // Act
        var second = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                Guid.CreateVersion7(),
                SyntheticMailAccount.Owner,
                OwnerCredentialMethod.Password,
                lookup,
                StoredHash,
                WholeMailSurface,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.LookupTaken, second);
        Assert.Equal(1, await CountUnderAsync(services, lookup, cancellationToken));
    }

    /// <summary>
    /// The index is over the method beside the lookup, so one value resolves one credential per method rather than one
    /// across the deployment. Nothing in a substitute would tell a conflict target naming both columns from one naming
    /// the lookup alone, and the second shape would refuse a public key whose digest happened to equal a username.
    /// </summary>
    [Fact]
    public async Task CreateAsync_OneValueUnderTwoMethods_ResolvesTwoCredentials()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var shared = OwnerCredentialLookup.ForDigest("orchestrated-shared-value");
        await ProvisionAsync(services, OwnerCredentialMethod.ApiKey, shared, cancellationToken);

        // Act
        var second = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                Guid.CreateVersion7(),
                SyntheticMailAccount.Owner,
                OwnerCredentialMethod.PublicKey,
                shared,
                StoredHash,
                WholeMailSurface,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.Written, second);
        Assert.Equal(2, await CountUnderAsync(services, shared, cancellationToken));

        var resolved = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(OwnerCredentialMethod.PublicKey, shared, token),
            cancellationToken);

        Assert.Equal(OwnerCredentialMethod.PublicKey, resolved!.Method);
    }

    /// <summary>An owner this deployment holds no record for is answered rather than raised, which is what the <c>EXISTS</c> subquery is for.</summary>
    [Fact]
    public async Task CreateAsync_AnOwnerTheDeploymentHoldsNoRecordFor_IsRefusedWithoutWriting()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var stranger = MailOwnerId.Create(Guid.CreateVersion7());
        var lookup = OwnerCredentialLookup.ForUsername(OwnerCredentialUsername.Create("orchestrated-unheld-owner"));

        // Act
        var outcome = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                Guid.CreateVersion7(),
                stranger,
                OwnerCredentialMethod.Password,
                lookup,
                StoredHash,
                WholeMailSurface,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.UnknownOwner, outcome);
        Assert.Equal(0, await CountUnderAsync(services, lookup, cancellationToken));
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
                OwnerCredential.MaximumListedPerOwner - 1,
                await FillToOnePlaceLeftAsync(services, contender, cancellationToken));

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                $"{nameof(IOwnerCredentialStore)}.{nameof(IOwnerCredentialStore.CreateAsync)}",
                ConcurrentProvisioners,
                (ordinal, token) => services.InScopeAsync(
                    (scope, inner) => Store(scope).CreateAsync(
                        Guid.CreateVersion7(),
                        contender,
                        OwnerCredentialMethod.Password,
                        OwnerCredentialLookup.ForUsername(
                            OwnerCredentialUsername.Create($"orchestrated-ceiling-race-{contenderId:N}-{ordinal}")),
                        StoredHash,
                        WholeMailSurface,
                        inner),
                    token),
                cancellationToken);

            // Assert
            var held = await CountForOwnerAsync(services, contender, cancellationToken);

            attempts.AssertSingleEffect(held - (OwnerCredential.MaximumListedPerOwner - 1));

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
    /// A password rotation states the username the credential already carries, and the update sets the lookup
    /// unconditionally — so the stated value is in the predicate as well. A mistyped one has to match no row and leave
    /// the sign-in alone, because writing it would stop the owner's username authenticating and start the typo, report
    /// the rotation as performed, and record only that material was replaced.
    /// </summary>
    [Fact]
    public async Task ReplaceMaterialAsync_AUsernameTheCredentialDoesNotCarry_WritesNothingAndRenamesNoSignIn()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var lookup = OwnerCredentialLookup.ForUsername(OwnerCredentialUsername.Create("orchestrated-rotation-typo"));
        var mistyped = OwnerCredentialLookup.ForUsername(OwnerCredentialUsername.Create("orchestrated-rotation-typo2"));
        var credentialId = await ProvisionAsync(services, OwnerCredentialMethod.Password, lookup, cancellationToken);

        // Act
        var rotation = await services.InScopeAsync(
            (scope, token) => Store(scope).ReplaceMaterialAsync(
                SyntheticMailAccount.Owner,
                credentialId,
                OwnerCredentialMethod.Password,
                mistyped,
                "$mf1$rotated$",
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.UnknownCredential, rotation);

        var stillResolvedByTheUsernameTheOwnerTypes = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(OwnerCredentialMethod.Password, lookup, token),
            cancellationToken);

        var resolvedByTheTypo = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(OwnerCredentialMethod.Password, mistyped, token),
            cancellationToken);

        Assert.NotNull(stillResolvedByTheUsernameTheOwnerTypes);
        Assert.Equal(StoredHash, stillResolvedByTheUsernameTheOwnerTypes.Material);
        Assert.Null(resolvedByTheTypo);
    }

    /// <summary>
    /// The other half of the predicate above: a client sending a new public key is resolved by that key's fingerprint
    /// from then on, so the stated lookup is the new value rather than one the row already carries. Matching on it
    /// would demand the row hold what the rotation exists to write, and the compromised key an operator is replacing
    /// would go on authenticating while the command reported that no such credential exists.
    /// </summary>
    [Fact]
    public async Task ReplaceMaterialAsync_APublicKeyRotatedToANewFingerprint_MovesTheLookupWithTheMaterial()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var retired = OwnerCredentialLookup.ForDigest("orchestrated-rotation-retired-fingerprint");
        var replacement = OwnerCredentialLookup.ForDigest("orchestrated-rotation-replacement-fingerprint");
        var credentialId = await ProvisionAsync(services, OwnerCredentialMethod.PublicKey, retired, cancellationToken);
        const string ReplacementKey = "-----BEGIN PUBLIC KEY-----replacement-----END PUBLIC KEY-----";

        // Act
        var rotation = await services.InScopeAsync(
            (scope, token) => Store(scope).ReplaceMaterialAsync(
                SyntheticMailAccount.Owner,
                credentialId,
                OwnerCredentialMethod.PublicKey,
                replacement,
                ReplacementKey,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.Written, rotation);

        var resolvedByTheNewFingerprint = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(OwnerCredentialMethod.PublicKey, replacement, token),
            cancellationToken);

        var resolvedByTheRetiredOne = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(OwnerCredentialMethod.PublicKey, retired, token),
            cancellationToken);

        Assert.NotNull(resolvedByTheNewFingerprint);
        Assert.Equal(credentialId, resolvedByTheNewFingerprint.Id);
        Assert.Equal(ReplacementKey, resolvedByTheNewFingerprint.Material);
        Assert.Null(resolvedByTheRetiredOne);
    }

    /// <summary>
    /// A rehash spends two deliberately slow derivations, and an administrator rotating a leaked credential can commit
    /// inside that window — which is the case rotation exists for. The rehash therefore names the record it verified
    /// against, so what the rotation wrote is not overwritten by a request that read the record it replaced.
    /// </summary>
    [Fact]
    public async Task RewriteMaterialAsync_ARecordAlreadyReplacedByARotation_WritesNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var lookup = OwnerCredentialLookup.ForUsername(OwnerCredentialUsername.Create("orchestrated-rehash-race"));
        var credentialId = await ProvisionAsync(services, OwnerCredentialMethod.Password, lookup, cancellationToken);
        const string Rotated = "$mf1$rotated$";

        await services.InScopeAsync(
            (scope, token) => Store(scope).ReplaceMaterialAsync(
                SyntheticMailAccount.Owner,
                credentialId,
                OwnerCredentialMethod.Password,
                lookup,
                Rotated,
                token),
            cancellationToken);

        // Act
        var rehash = await services.InScopeAsync(
            (scope, token) => Store(scope).RewriteMaterialAsync(
                SyntheticMailAccount.Owner,
                credentialId,
                StoredHash,
                "$mf1$stronger$",
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.UnknownCredential, rehash);

        var stored = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(OwnerCredentialMethod.Password, lookup, token),
            cancellationToken);

        Assert.Equal(Rotated, stored!.Material);
    }

    /// <summary>The rehash the verification actually resolved does land, which is what says the refusal above is about the race rather than about the predicate refusing everything.</summary>
    [Fact]
    public async Task RewriteMaterialAsync_TheRecordTheRequestVerifiedAgainst_IsRewritten()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var lookup = OwnerCredentialLookup.ForUsername(OwnerCredentialUsername.Create("orchestrated-rehash-settled"));
        var credentialId = await ProvisionAsync(services, OwnerCredentialMethod.Password, lookup, cancellationToken);
        const string Stronger = "$mf1$stronger$settled$";

        // Act
        var rehash = await services.InScopeAsync(
            (scope, token) => Store(scope).RewriteMaterialAsync(
                SyntheticMailAccount.Owner,
                credentialId,
                StoredHash,
                Stronger,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.Written, rehash);

        var stored = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(OwnerCredentialMethod.Password, lookup, token),
            cancellationToken);

        Assert.Equal(Stronger, stored!.Material);
    }

    private static IOwnerCredentialStore Store(IServiceProvider scope) =>
        scope.GetRequiredService<IOwnerCredentialStore>();

    private static async Task<Guid> ProvisionAsync(
        OrchestratedMailFathomServices services,
        OwnerCredentialMethod method,
        OwnerCredentialLookup lookup,
        CancellationToken cancellationToken)
    {
        var credentialId = Guid.CreateVersion7();

        var outcome = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                credentialId,
                SyntheticMailAccount.Owner,
                method,
                lookup,
                StoredHash,
                WholeMailSurface,
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
                var lookupPrefix = $"orchestrated-ceiling-{owner.Value:N}-";
                var method = OwnerCredentialMethod.Password.Name;
                var permissions = WholeMailSurface.Select(permission => permission.Name).ToArray();

                return scope.GetRequiredService<MailFathomDbContext>().Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO owner_credentials
                         ("Id", "OwnerId", "Method", "Lookup", "Material", "Permissions", "Enabled", "Version", "CreatedAt", "MaterialChangedAt")
                     SELECT gen_random_uuid(), {owner.Value}, {method}, {lookupPrefix} || ordinal::text, {StoredHash}, {permissions}, TRUE, 1, {provisionedAt}, {provisionedAt}
                     FROM generate_series(1, {OwnerCredential.MaximumListedPerOwner - 1}) AS ordinal
                     """,
                    token);
            },
            cancellationToken);

    private static Task<int> CountForOwnerAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .OwnerCredentials
                .AsNoTracking()
                .CountAsync(credential => credential.OwnerId == owner.Value, token),
            cancellationToken);

    private static Task<int> CountUnderAsync(
        OrchestratedMailFathomServices services,
        OwnerCredentialLookup lookup,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .OwnerCredentials
                .AsNoTracking()
                .CountAsync(credential => credential.Lookup == lookup.Value, token),
            cancellationToken);
}
