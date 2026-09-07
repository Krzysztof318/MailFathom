// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
/// <c>WHERE</c> carries the user's existence and the per-user ceiling, an <c>ON CONFLICT</c> naming a unique index by
/// its columns, a row lock taken in a statement of its own inside a transaction, and an <c>UPDATE</c> conditioned on the
/// record the caller verified against. A substitute settles none of them: a conflict target that does not match the
/// index, an identifier PostgreSQL folds differently, and a lock that does not serialize what it was written to
/// serialize all answer correctly in memory and wrongly on a deployment.
/// </para>
/// <para>
/// The store is reached through the container rather than constructed, so what runs is the registration a deployment
/// runs. It takes no caller: <c>UserCredentialAdministration</c> is where the grant is checked and is covered in the
/// unit suite, and what is under test here is the row the statement leaves.
/// </para>
/// <para>
/// Each test provisions under lookups of its own, because the lookup index is deployment-wide and this class shares a
/// database with every other class in the collection. The index is over the method beside the lookup, which is why one
/// test provisions the same value under two methods and expects both to land.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedUserCredentialTests(MailFathomOrchestrationFixture orchestration)
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
        var lookup = UserCredentialLookup.ForUsername(UserCredentialUsername.Create("orchestrated-provisioned"));

        // Act
        var outcome = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                credentialId,
                SyntheticMailAccount.User,
                UserCredentialMethod.Password,
                lookup,
                StoredHash,
                WholeMailSurface,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.Written, outcome);

        var listed = await services.InScopeAsync(
            (scope, token) => Store(scope).ReadForUserAsync(SyntheticMailAccount.User, token),
            cancellationToken);

        var provisioned = Assert.Single(listed, credential => credential.Id == credentialId);
        Assert.Equal(lookup, provisioned.Lookup);
        Assert.Equal(UserCredentialMethod.Password, provisioned.Method);
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
        var lookup = UserCredentialLookup.ForUsername(UserCredentialUsername.Create("orchestrated-taken"));
        await ProvisionAsync(services, UserCredentialMethod.Password, lookup, cancellationToken);

        // Act
        var second = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                Guid.CreateVersion7(),
                SyntheticMailAccount.User,
                UserCredentialMethod.Password,
                lookup,
                StoredHash,
                WholeMailSurface,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.LookupTaken, second);
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
        var shared = UserCredentialLookup.ForDigest("orchestrated-shared-value");
        await ProvisionAsync(services, UserCredentialMethod.ApiKey, shared, cancellationToken);

        // Act
        var second = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                Guid.CreateVersion7(),
                SyntheticMailAccount.User,
                UserCredentialMethod.PublicKey,
                shared,
                StoredHash,
                WholeMailSurface,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.Written, second);
        Assert.Equal(2, await CountUnderAsync(services, shared, cancellationToken));

        var resolved = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(UserCredentialMethod.PublicKey, shared, token),
            cancellationToken);

        Assert.Equal(UserCredentialMethod.PublicKey, resolved!.Method);
    }

    /// <summary>A user this deployment holds no record for is answered rather than raised, which is what the <c>EXISTS</c> subquery is for.</summary>
    [Fact]
    public async Task CreateAsync_AnUserTheDeploymentHoldsNoRecordFor_IsRefusedWithoutWriting()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var stranger = MailUserId.Create(Guid.CreateVersion7());
        var lookup = UserCredentialLookup.ForUsername(UserCredentialUsername.Create("orchestrated-unheld-user"));

        // Act
        var outcome = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                Guid.CreateVersion7(),
                stranger,
                UserCredentialMethod.Password,
                lookup,
                StoredHash,
                WholeMailSurface,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.UnknownUser, outcome);
        Assert.Equal(0, await CountUnderAsync(services, lookup, cancellationToken));
    }

    /// <summary>
    /// The ceiling is enforced by a count inside the insert, and a count under READ COMMITTED sees neither of two
    /// concurrent inserts. What serializes them is the row lock the statement before it takes on the user, so the
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
        var contender = MailUserId.Create(contenderId);

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, contenderId, cancellationToken));

            Assert.Equal(
                UserCredential.MaximumListedPerUser - 1,
                await FillToOnePlaceLeftAsync(services, contender, cancellationToken));

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                $"{nameof(IUserCredentialStore)}.{nameof(IUserCredentialStore.CreateAsync)}",
                ConcurrentProvisioners,
                (ordinal, token) => services.InScopeAsync(
                    (scope, inner) => Store(scope).CreateAsync(
                        Guid.CreateVersion7(),
                        contender,
                        UserCredentialMethod.Password,
                        UserCredentialLookup.ForUsername(
                            UserCredentialUsername.Create($"orchestrated-ceiling-race-{contenderId:N}-{ordinal}")),
                        StoredHash,
                        WholeMailSurface,
                        inner),
                    token),
                cancellationToken);

            // Assert
            var held = await CountForUserAsync(services, contender, cancellationToken);

            attempts.AssertSingleEffect(held - (UserCredential.MaximumListedPerUser - 1));

            Assert.All(
                attempts.Results.Where(outcome => outcome != UserCredentialWriteOutcome.Written),
                static outcome => Assert.Equal(UserCredentialWriteOutcome.UserAtCredentialCeiling, outcome));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, contenderId);
        }
    }

    /// <summary>
    /// A password rotation states the username the credential already carries, and the update sets the lookup
    /// unconditionally — so the stated value is in the predicate as well. A mistyped one has to match no row and leave
    /// the sign-in alone, because writing it would stop the user's username authenticating and start the typo, report
    /// the rotation as performed, and record only that material was replaced.
    /// </summary>
    [Fact]
    public async Task ReplaceMaterialAsync_AUsernameTheCredentialDoesNotCarry_WritesNothingAndRenamesNoSignIn()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var lookup = UserCredentialLookup.ForUsername(UserCredentialUsername.Create("orchestrated-rotation-typo"));
        var mistyped = UserCredentialLookup.ForUsername(UserCredentialUsername.Create("orchestrated-rotation-typo2"));
        var credentialId = await ProvisionAsync(services, UserCredentialMethod.Password, lookup, cancellationToken);

        // Act
        var rotation = await services.InScopeAsync(
            (scope, token) => Store(scope).ReplaceMaterialAsync(
                SyntheticMailAccount.User,
                credentialId,
                UserCredentialMethod.Password,
                mistyped,
                "$mf1$rotated$",
                token),
            cancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.UnknownCredential, rotation);

        var stillResolvedByTheUsernameTheUserTypes = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(UserCredentialMethod.Password, lookup, token),
            cancellationToken);

        var resolvedByTheTypo = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(UserCredentialMethod.Password, mistyped, token),
            cancellationToken);

        Assert.NotNull(stillResolvedByTheUsernameTheUserTypes);
        Assert.Equal(StoredHash, stillResolvedByTheUsernameTheUserTypes.Material);
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
        var retired = UserCredentialLookup.ForDigest("orchestrated-rotation-retired-fingerprint");
        var replacement = UserCredentialLookup.ForDigest("orchestrated-rotation-replacement-fingerprint");
        var credentialId = await ProvisionAsync(services, UserCredentialMethod.PublicKey, retired, cancellationToken);
        const string ReplacementKey = "-----BEGIN PUBLIC KEY-----replacement-----END PUBLIC KEY-----";

        // Act
        var rotation = await services.InScopeAsync(
            (scope, token) => Store(scope).ReplaceMaterialAsync(
                SyntheticMailAccount.User,
                credentialId,
                UserCredentialMethod.PublicKey,
                replacement,
                ReplacementKey,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.Written, rotation);

        var resolvedByTheNewFingerprint = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(UserCredentialMethod.PublicKey, replacement, token),
            cancellationToken);

        var resolvedByTheRetiredOne = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(UserCredentialMethod.PublicKey, retired, token),
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
        var lookup = UserCredentialLookup.ForUsername(UserCredentialUsername.Create("orchestrated-rehash-race"));
        var credentialId = await ProvisionAsync(services, UserCredentialMethod.Password, lookup, cancellationToken);
        const string Rotated = "$mf1$rotated$";

        await services.InScopeAsync(
            (scope, token) => Store(scope).ReplaceMaterialAsync(
                SyntheticMailAccount.User,
                credentialId,
                UserCredentialMethod.Password,
                lookup,
                Rotated,
                token),
            cancellationToken);

        // Act
        var rehash = await services.InScopeAsync(
            (scope, token) => Store(scope).RewriteMaterialAsync(
                SyntheticMailAccount.User,
                credentialId,
                StoredHash,
                "$mf1$stronger$",
                token),
            cancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.UnknownCredential, rehash);

        var stored = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(UserCredentialMethod.Password, lookup, token),
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
        var lookup = UserCredentialLookup.ForUsername(UserCredentialUsername.Create("orchestrated-rehash-settled"));
        var credentialId = await ProvisionAsync(services, UserCredentialMethod.Password, lookup, cancellationToken);
        const string Stronger = "$mf1$stronger$settled$";

        // Act
        var rehash = await services.InScopeAsync(
            (scope, token) => Store(scope).RewriteMaterialAsync(
                SyntheticMailAccount.User,
                credentialId,
                StoredHash,
                Stronger,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.Written, rehash);

        var stored = await services.InScopeAsync(
            (scope, token) => Store(scope).FindAsync(UserCredentialMethod.Password, lookup, token),
            cancellationToken);

        Assert.Equal(Stronger, stored!.Material);
    }

    private static IUserCredentialStore Store(IServiceProvider scope) =>
        scope.GetRequiredService<IUserCredentialStore>();

    private static async Task<Guid> ProvisionAsync(
        OrchestratedMailFathomServices services,
        UserCredentialMethod method,
        UserCredentialLookup lookup,
        CancellationToken cancellationToken)
    {
        var credentialId = Guid.CreateVersion7();

        var outcome = await services.InScopeAsync(
            (scope, token) => Store(scope).CreateAsync(
                credentialId,
                SyntheticMailAccount.User,
                method,
                lookup,
                StoredHash,
                WholeMailSurface,
                token),
            cancellationToken);

        Assert.Equal(UserCredentialWriteOutcome.Written, outcome);

        return credentialId;
    }

    /// <summary>Writes credentials until the user holds one short of the ceiling, so the race above is for the last place.</summary>
    /// <returns>How many rows the statement wrote, which the caller asserts before it races for the place they leave.</returns>
    /// <remarks>
    /// Written as rows in one statement rather than through the store, because what is being arranged is a count and not
    /// the statement under test: ninety-nine round trips through the production write would cost the suite far more than
    /// the claim is worth, and the rows only have to satisfy the count the ceiling reads.
    /// </remarks>
    private static Task<int> FillToOnePlaceLeftAsync(
        OrchestratedMailFathomServices services,
        MailUserId user,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) =>
            {
                var provisionedAt = DateTimeOffset.UnixEpoch;
                var lookupPrefix = $"orchestrated-ceiling-{user.Value:N}-";
                var method = UserCredentialMethod.Password.Name;
                var permissions = WholeMailSurface.Select(permission => permission.Name).ToArray();

                return scope.GetRequiredService<MailFathomDbContext>().Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO user_credentials
                         ("Id", "UserId", "Method", "Lookup", "Material", "Permissions", "Enabled", "Version", "CreatedAt", "MaterialChangedAt")
                     SELECT gen_random_uuid(), {user.Value}, {method}, {lookupPrefix} || ordinal::text, {StoredHash}, {permissions}, TRUE, 1, {provisionedAt}, {provisionedAt}
                     FROM generate_series(1, {UserCredential.MaximumListedPerUser - 1}) AS ordinal
                     """,
                    token);
            },
            cancellationToken);

    private static Task<int> CountForUserAsync(
        OrchestratedMailFathomServices services,
        MailUserId user,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .UserCredentials
                .AsNoTracking()
                .CountAsync(credential => credential.UserId == user.Value, token),
            cancellationToken);

    private static Task<int> CountUnderAsync(
        OrchestratedMailFathomServices services,
        UserCredentialLookup lookup,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .UserCredentials
                .AsNoTracking()
                .CountAsync(credential => credential.Lookup == lookup.Value, token),
            cancellationToken);
}
