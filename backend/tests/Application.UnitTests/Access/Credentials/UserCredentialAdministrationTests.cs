// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Access.Credentials;

/// <summary>Covers what each administrative act requires, what it writes down, and what it refuses to be handed.</summary>
/// <remarks>
/// One class for four methods, because the use case is one: what differs between them is the lookup that is composed
/// and the material that is kept, and both are asserted per method against the same store double.
/// </remarks>
public sealed class UserCredentialAdministrationTests
{
    private const string AdministratorIdentity = "admin-key";

    private const string AcceptablePassword = "correcthorsebatterystaple";

    private const string WrittenPublicKey = "-----BEGIN PUBLIC KEY-----readable-----END PUBLIC KEY-----";

    private static readonly MailUserId User = MailUserId.Create(new Guid("0197c0de-0000-7000-8000-00000000ffff"));

    private static readonly Guid CredentialId = new("0197c0de-0000-7000-8000-000000000001");

    private static readonly DateTimeOffset ActedAt = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProvisionPasswordAsync_ACallerGrantedTheCredentialWrite_StoresTheHashUnderAMintedIdentifier()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var provisioning = await harness.Administration.ProvisionPasswordAsync(
            User,
            UserCredentialUsername.Create("user"),
            AcceptablePassword.AsMemory(),
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.Written, provisioning.Outcome);
        Assert.NotEqual(Guid.Empty, provisioning.CredentialId);
        Assert.Equal("user", provisioning.Lookup.Value);
        Assert.Null(provisioning.MintedKey);

        await harness.Credentials.Received(1).CreateAsync(
            provisioning.CredentialId,
            User,
            UserCredentialMethod.Password,
            Arg.Is<UserCredentialLookup>(lookup => lookup.Value == "user"),
            AdministrationHarness.StoredHash,
            Arg.Any<IReadOnlyList<MailFathomPermission>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The plaintext is turned into a stored representation inside the call and never reaches the store.</summary>
    [Fact]
    public async Task ProvisionPasswordAsync_APassword_ReachesTheStoreOnlyAsWhatTheHasherProduced()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await harness.Administration.ProvisionPasswordAsync(
            User,
            UserCredentialUsername.Create("user"),
            AcceptablePassword.AsMemory(),
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Credentials.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(),
            Arg.Any<MailUserId>(),
            Arg.Any<UserCredentialMethod>(),
            Arg.Any<UserCredentialLookup>(),
            Arg.Is<string>(stored => stored != null && stored.Contains(AcceptablePassword, StringComparison.Ordinal)),
            Arg.Any<IReadOnlyList<MailFathomPermission>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The policy is checked here as well as at the boundary. Reaching it is an entrypoint that did not check rather
    /// than an operator's mistake, so it raises instead of composing an answer somebody would read.
    /// </summary>
    [Fact]
    public async Task ProvisionPasswordAsync_APasswordThePolicyRefuses_ThrowsWithoutNamingWhatWasWritten()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Administration.ProvisionPasswordAsync(
                User,
                UserCredentialUsername.Create("user"),
                "short".AsMemory(),
                permissions: null,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain("short", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Credentials.ReceivedCalls());
    }

    /// <summary>The key exists in the answer and nowhere else: the row keeps its digest, which is the lookup.</summary>
    [Fact]
    public async Task ProvisionApiKeyAsync_ACallerGrantedTheCredentialWrite_AnswersWithTheKeyAndStoresNoMaterial()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var provisioning = await harness.Administration.ProvisionApiKeyAsync(
            User,
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatedApiKeyMinter.Key, provisioning.MintedKey);
        Assert.Equal(StatedApiKeyMinter.Digest, provisioning.Lookup.Value);

        await harness.Credentials.Received(1).CreateAsync(
            provisioning.CredentialId,
            User,
            UserCredentialMethod.ApiKey,
            Arg.Is<UserCredentialLookup>(lookup => lookup.Value == StatedApiKeyMinter.Digest),
            null,
            Arg.Any<IReadOnlyList<MailFathomPermission>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A key nothing stored is a key nothing may report, so an act that wrote no row answers with none.</summary>
    [Fact]
    public async Task ProvisionApiKeyAsync_AnActThatWroteNothing_WithholdsTheKeyItWouldHaveHandedOver()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.AnswerCreateWith(UserCredentialWriteOutcome.UnknownUser);

        // Act
        var provisioning = await harness.Administration.ProvisionApiKeyAsync(
            User,
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.UnknownUser, provisioning.Outcome);
        Assert.Null(provisioning.MintedKey);
    }

    [Fact]
    public async Task ProvisionPublicKeyAsync_AReadableKey_StoresItUnderTheFingerprintTheReaderComputed()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var provisioning = await harness.Administration.ProvisionPublicKeyAsync(
            User,
            WrittenPublicKey,
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatedPublicKeyReader.Fingerprint, provisioning.Lookup.Value);
        Assert.Null(provisioning.MintedKey);

        await harness.Credentials.Received(1).CreateAsync(
            provisioning.CredentialId,
            User,
            UserCredentialMethod.PublicKey,
            Arg.Is<UserCredentialLookup>(lookup => lookup.Value == StatedPublicKeyReader.Fingerprint),
            WrittenPublicKey,
            Arg.Any<IReadOnlyList<MailFathomPermission>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An unreadable key is an entrypoint that did not check, and it says what an acceptable one looks like.</summary>
    [Fact]
    public async Task ProvisionPublicKeyAsync_AKeyTheReaderRefuses_ThrowsNamingTheFormItAccepts()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Administration.ProvisionPublicKeyAsync(
                User,
                "not a key",
                permissions: null,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(StatedPublicKeyReader.AcceptedForm, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Credentials.ReceivedCalls());
    }

    [Fact]
    public async Task ProvisionOAuthSubjectAsync_AnIssuerAndASubject_StoresTheMappingWithNoMaterialOfItsOwn()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var provisioning = await harness.Administration.ProvisionOAuthSubjectAsync(
            User,
            "https://login.example/",
            "subject-1",
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(provisioning.Lookup.TryReadOAuthSubject(out var issuer, out var subject));
        Assert.Equal("https://login.example/", issuer);
        Assert.Equal("subject-1", subject);

        await harness.Credentials.Received(1).CreateAsync(
            provisioning.CredentialId,
            User,
            UserCredentialMethod.OAuthSubject,
            Arg.Any<UserCredentialLookup>(),
            null,
            Arg.Any<IReadOnlyList<MailFathomPermission>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The two halves compose one indexed value, so a pair that cannot compose one is refused rather than stored.</summary>
    [Theory]
    [InlineData("https://login example/", "subject-1")]
    [InlineData("https://login.example/", "")]
    [InlineData(null, "subject-1")]
    public async Task ProvisionOAuthSubjectAsync_APairThatComposesNoLookup_ThrowsWithoutTouchingTheStore(
        string? issuer,
        string? subject)
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Administration.ProvisionOAuthSubjectAsync(
            User,
            issuer,
            subject,
            permissions: null,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(harness.Credentials.ReceivedCalls());
    }

    /// <summary>An unwritten grant is the whole mail surface, which is what an operator who narrowed nothing asked for.</summary>
    [Fact]
    public async Task ProvisionApiKeyAsync_AGrantNobodyNarrowed_StoresEveryPermissionTheMailSurfacePublishes()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await harness.Administration.ProvisionApiKeyAsync(
            User,
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomPermission.PublishedFor(ProtectedSurface.Mail), harness.WrittenGrant);
    }

    /// <summary>A narrowed grant is stored in the published order rather than in whichever order it was written in.</summary>
    [Fact]
    public async Task ProvisionApiKeyAsync_ANarrowedGrant_StoresItInThePublishedOrderWithoutDuplicates()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await harness.Administration.ProvisionApiKeyAsync(
            User,
            [MailFathomPermission.MailSend, MailFathomPermission.MailRead, MailFathomPermission.MailSend],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MailFathomPermission.MailRead, MailFathomPermission.MailSend], harness.WrittenGrant);
    }

    /// <summary>An empty grant is a credential that authenticates and may do nothing, which is not the same as an unwritten one.</summary>
    [Fact]
    public async Task ProvisionApiKeyAsync_AGrantNamingNothing_StoresNothingRatherThanTheWholeSurface()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await harness.Administration.ProvisionApiKeyAsync(User, [], TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(harness.WrittenGrant);
        Assert.Empty(harness.WrittenGrant);
    }

    /// <summary>
    /// A credential reaches one user's mail, so an administrative permission on one would be a way into the deployment
    /// rather than into a mailbox. The refusal is published so a boundary can answer with it instead of raising.
    /// </summary>
    [Fact]
    public void FindGrantRefusal_APermissionOfTheAdministrativeSurface_IsRefusedNamingWhatMayBeWrittenInstead()
    {
        // Arrange
        IReadOnlyList<MailFathomPermission> requested = [MailFathomPermission.MailRead, MailFathomPermission.AdminErase];

        // Act
        var refusal = UserCredentialAdministration.FindGrantRefusal(requested);

        // Assert
        Assert.NotNull(refusal);
        Assert.Contains(MailFathomPermission.AdminErase.Name, refusal, StringComparison.Ordinal);
        Assert.Contains(MailFathomPermission.MailRead.Name, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void FindGrantRefusal_AValueNamingNoPublishedPermission_IsRefused()
    {
        // Arrange
        IReadOnlyList<MailFathomPermission> requested = [default];

        // Act
        var refusal = UserCredentialAdministration.FindGrantRefusal(requested);

        // Assert
        Assert.NotNull(refusal);
    }

    [Fact]
    public void FindGrantRefusal_AnUnwrittenGrantOrOneOfTheMailSurface_IsAccepted()
    {
        // Arrange
        var mailSurface = MailFathomPermission.PublishedFor(ProtectedSurface.Mail);

        // Act
        var unwritten = UserCredentialAdministration.FindGrantRefusal(permissions: null);
        var narrowed = UserCredentialAdministration.FindGrantRefusal(mailSurface);

        // Assert
        Assert.Null(unwritten);
        Assert.Null(narrowed);
    }

    /// <summary>The same rule holds inside the use case, where reaching it means an entrypoint did not check.</summary>
    [Fact]
    public async Task ProvisionApiKeyAsync_AGrantNamingSomethingAdministrative_ThrowsWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Administration.ProvisionApiKeyAsync(
            User,
            [MailFathomPermission.AdminOperate],
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(harness.Credentials.ReceivedCalls());
    }

    [Fact]
    public async Task ProvisionPasswordAsync_AWrittenCredential_IsRecordedAgainstTheAdministratorThatAskedForIt()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var provisioning = await harness.Administration.ProvisionPasswordAsync(
            User,
            UserCredentialUsername.Create("user"),
            AcceptablePassword.AsMemory(),
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Auditor.Received(1).RecordCredentialChangeAsync(
            Arg.Is<UserCredentialChange>(change =>
                change != null
                && change.Act == UserCredentialAct.Provisioned
                && change.CredentialId == provisioning.CredentialId
                && change.User == User
                && change.Method == UserCredentialMethod.Password
                && change.ActingAdministrator == AdministratorIdentity
                && change.OccurredAt == ActedAt),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A record of a mistyped identifier is a record of an attempt, and would read alike beside a real change.</summary>
    [Theory]
    [InlineData(UserCredentialWriteOutcome.UnknownUser)]
    [InlineData(UserCredentialWriteOutcome.LookupTaken)]
    [InlineData(UserCredentialWriteOutcome.UserAtCredentialCeiling)]
    public async Task ProvisionPasswordAsync_AnActThatChangedNothing_IsNotWrittenDown(
        UserCredentialWriteOutcome outcome)
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.AnswerCreateWith(outcome);

        // Act
        var provisioning = await harness.Administration.ProvisionPasswordAsync(
            User,
            UserCredentialUsername.Create("user"),
            AcceptablePassword.AsMemory(),
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(outcome, provisioning.Outcome);

        await harness.Auditor.DidNotReceive().RecordCredentialChangeAsync(
            Arg.Any<UserCredentialChange>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Rotating names the value the credential is resolved by, so a mistyped one is refused rather than renaming a sign-in.</summary>
    [Fact]
    public async Task RotatePasswordAsync_ACallerGrantedTheCredentialWrite_ReplacesTheStoredHashAndRecordsIt()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var rotation = await harness.Administration.RotatePasswordAsync(
            User,
            CredentialId,
            UserCredentialUsername.Create("user"),
            AcceptablePassword.AsMemory(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.Written, rotation.Outcome);
        Assert.Null(rotation.MintedKey);

        await harness.Credentials.Received(1).ReplaceMaterialAsync(
            User,
            CredentialId,
            UserCredentialMethod.Password,
            Arg.Is<UserCredentialLookup>(lookup => lookup.Value == "user"),
            AdministrationHarness.StoredHash,
            Arg.Any<CancellationToken>());

        await harness.Auditor.Received(1).RecordCredentialChangeAsync(
            Arg.Is<UserCredentialChange>(change =>
                change != null && change.Act == UserCredentialAct.MaterialRotated),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RotateApiKeyAsync_AWrittenRotation_AnswersWithTheKeyTheClientMustPresentFromNowOn()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var rotation = await harness.Administration.RotateApiKeyAsync(
            User,
            CredentialId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatedApiKeyMinter.Key, rotation.MintedKey);
        Assert.Equal(StatedApiKeyMinter.Digest, rotation.Lookup.Value);

        await harness.Credentials.Received(1).ReplaceMaterialAsync(
            User,
            CredentialId,
            UserCredentialMethod.ApiKey,
            Arg.Is<UserCredentialLookup>(lookup => lookup.Value == StatedApiKeyMinter.Digest),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RotateApiKeyAsync_ARotationThatWroteNothing_WithholdsTheKeyItWouldHaveHandedOver()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.AnswerReplaceWith(UserCredentialWriteOutcome.UnknownCredential);

        // Act
        var rotation = await harness.Administration.RotateApiKeyAsync(
            User,
            CredentialId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserCredentialWriteOutcome.UnknownCredential, rotation.Outcome);
        Assert.Null(rotation.MintedKey);
    }

    [Fact]
    public async Task ReplacePublicKeyAsync_AReadableKey_ReplacesBothTheMaterialAndTheFingerprintItIsResolvedBy()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var rotation = await harness.Administration.ReplacePublicKeyAsync(
            User,
            CredentialId,
            WrittenPublicKey,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatedPublicKeyReader.Fingerprint, rotation.Lookup.Value);

        await harness.Credentials.Received(1).ReplaceMaterialAsync(
            User,
            CredentialId,
            UserCredentialMethod.PublicKey,
            Arg.Is<UserCredentialLookup>(lookup => lookup.Value == StatedPublicKeyReader.Fingerprint),
            WrittenPublicKey,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, UserCredentialAct.Enabled)]
    [InlineData(false, UserCredentialAct.Disabled)]
    public async Task SetEnabledAsync_EitherDecision_IsWrittenDownAsTheActItWas(bool enabled, UserCredentialAct act)
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await harness.Administration.SetEnabledAsync(
            User,
            CredentialId,
            enabled,
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Auditor.Received(1).RecordCredentialChangeAsync(
            Arg.Is<UserCredentialChange>(change => change != null && change.Act == act),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_ACallerGrantedTheCredentialWrite_RemovesTheCredentialAndRecordsIt()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await harness.Administration.DeleteAsync(User, CredentialId, TestContext.Current.CancellationToken);

        // Assert
        await harness.Credentials.Received(1).DeleteAsync(User, CredentialId, Arg.Any<CancellationToken>());

        await harness.Auditor.Received(1).RecordCredentialChangeAsync(
            Arg.Is<UserCredentialChange>(change => change != null && change.Act == UserCredentialAct.Deleted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadCredentialsAsync_ACallerGrantedTheAdministrativeRead_IsAnsweredWithWhatTheUserHolds()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminRead);
        var held = new UserCredential(
            CredentialId,
            User,
            UserCredentialMethod.Password,
            UserCredentialLookup.ForUsername(UserCredentialUsername.Create("user")),
            [MailFathomPermission.MailRead],
            Enabled: true,
            Version: 1,
            ActedAt,
            ActedAt);

        harness.Credentials.ReadForUserAsync(User, Arg.Any<CancellationToken>()).Returns([held]);

        // Act
        var credentials = await harness.Administration.ReadCredentialsAsync(
            User,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([held], credentials);
    }

    /// <summary>
    /// Reading says which credentials exist and whose they are; writing decides who can read a person's mail. A grant
    /// carrying one is never a grant carrying the other, which is what these two cases are about.
    /// </summary>
    [Fact]
    public async Task ReadCredentialsAsync_ACallerGrantedOnlyTheCredentialWrite_IsRefused()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.ReadCredentialsAsync(User, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    /// <summary>One permission governs every method, so a caller holding the read alone provisions none of the four.</summary>
    [Fact]
    public async Task ProvisioningAnyMethod_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminRead);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var refusals = await Task.WhenAll(
            RefusalOf(() => harness.Administration.ProvisionPasswordAsync(
                User,
                UserCredentialUsername.Create("user"),
                AcceptablePassword.AsMemory(),
                permissions: null,
                cancellationToken)),
            RefusalOf(() => harness.Administration.ProvisionApiKeyAsync(User, permissions: null, cancellationToken)),
            RefusalOf(() => harness.Administration.ProvisionPublicKeyAsync(
                User,
                WrittenPublicKey,
                permissions: null,
                cancellationToken)),
            RefusalOf(() => harness.Administration.ProvisionOAuthSubjectAsync(
                User,
                "https://login.example/",
                "subject-1",
                permissions: null,
                cancellationToken)));

        // Assert
        Assert.Equal(
            [.. refusals.Select(_ => MailFathomPermission.AdminCredentialsWrite)],
            [.. refusals.Select(refusal => refusal.RequiredPermission)]);

        Assert.Empty(harness.Credentials.ReceivedCalls());
    }

    [Fact]
    public async Task RotatePasswordAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedBeforeThePasswordIsHashed()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminRead);

        // Act
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => harness.Administration.RotatePasswordAsync(
            User,
            CredentialId,
            UserCredentialUsername.Create("user"),
            AcceptablePassword.AsMemory(),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(0, harness.PasswordHasher.HashCount);
    }

    [Theory]
    [InlineData("mailfathom.admin.read")]
    [InlineData("mailfathom.admin.operate")]
    public async Task DeleteAsync_ACallerGrantedSomethingElseAdministrative_IsRefused(string granted)
    {
        // Arrange
        Assert.True(MailFathomPermission.TryParse(granted, out var permission));

        var harness = new AdministrationHarness(permission);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.DeleteAsync(User, CredentialId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCredentialsWrite, refusal.RequiredPermission);
    }

    private static async Task<PrincipalNotAuthorizedException> RefusalOf(Func<Task> act) =>
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(act);

    /// <summary>Counts what the use case asked of a hasher, and answers with a fixed stored representation.</summary>
    /// <remarks>
    /// Hand-written rather than substituted, because the members take the password as a <see cref="ReadOnlySpan{T}" />
    /// and a dynamic proxy cannot carry a by-ref-like argument through its invocation. Counting the calls is what lets a
    /// test assert that an unauthorized act never reached the derivation at all.
    /// </remarks>
    private sealed class RecordingPasswordHasher : IPasswordHasher
    {
        internal int HashCount { get; private set; }

        public string HashDecoy() => AdministrationHarness.StoredHash;

        public string Hash(ReadOnlySpan<char> password)
        {
            this.HashCount++;

            return AdministrationHarness.StoredHash;
        }

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password) =>
            PasswordVerification.Failed;
    }

    /// <summary>Mints one stated key, so a test can assert which value reached the answer and which reached the row.</summary>
    /// <remarks>Hand-written for the same reason the hasher is: <see cref="IUserApiKeyMinter.TryDigest" /> takes a span.</remarks>
    private sealed class StatedApiKeyMinter : IUserApiKeyMinter
    {
        internal const string Key = "mfk_stated-key";

        internal const string Digest = "stated-digest";

        public MintedUserApiKey Mint() => new(Key, UserCredentialLookup.ForDigest(Digest));

        public bool TryDigest(ReadOnlySpan<char> presentedKey, out UserCredentialLookup lookup)
        {
            lookup = presentedKey.SequenceEqual(Key) ? UserCredentialLookup.ForDigest(Digest) : default;

            return lookup.IsSpecified;
        }
    }

    /// <summary>Reads one stated key and refuses everything else, so both branches are reachable from a test.</summary>
    private sealed class StatedPublicKeyReader : IClientPublicKeyReader
    {
        internal const string Fingerprint = "stated-fingerprint";

        internal const string AcceptedForm = "A client's public key is a PEM document.";

        public bool TryRead(string? written, out ClientPublicKey? publicKey)
        {
            publicKey = written == WrittenPublicKey
                ? new ClientPublicKey(written, UserCredentialLookup.ForDigest(Fingerprint))
                : null;

            return publicKey is not null;
        }

        public string DescribeAcceptedForm() => AcceptedForm;
    }

    /// <summary>Builds the use case over doubles, with the clock and the identity every record is stamped from.</summary>
    private sealed class AdministrationHarness
    {
        internal const string StoredHash = "$mf1$stored$";

        internal AdministrationHarness(MailFathomPermission granted)
        {
            var principals = Substitute.For<IAuthorizedPrincipalSource>();
            // A caller acting for nobody's mail, which is the only shape the administrative surface produces: the user
            // every act here names comes from its own argument rather than from whoever was admitted.
            principals.Current.Returns(AuthorizedPrincipal.Caller(AdministratorIdentity, [granted]));

            this.Credentials = Substitute.For<IUserCredentialStore>();
            this.AnswerCreateWith(UserCredentialWriteOutcome.Written);
            this.AnswerReplaceWith(UserCredentialWriteOutcome.Written);
            this.Credentials.SetEnabledAsync(
                    Arg.Any<MailUserId>(),
                    Arg.Any<Guid>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(UserCredentialWriteOutcome.Written);
            this.Credentials.DeleteAsync(Arg.Any<MailUserId>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(UserCredentialWriteOutcome.Written);
            this.Credentials.ReadForUserAsync(Arg.Any<MailUserId>(), Arg.Any<CancellationToken>())
                .Returns([]);

            this.PasswordHasher = new RecordingPasswordHasher();

            this.Auditor = Substitute.For<IUserCredentialAuditor>();

            this.Administration = new UserCredentialAdministration(
                new AccessAuthorization(principals),
                this.Credentials,
                this.PasswordHasher,
                new StatedApiKeyMinter(),
                new StatedPublicKeyReader(),
                this.Auditor,
                new FakeTimeProvider(ActedAt));
        }

        internal UserCredentialAdministration Administration { get; }

        internal IUserCredentialStore Credentials { get; }

        internal RecordingPasswordHasher PasswordHasher { get; }

        internal IUserCredentialAuditor Auditor { get; }

        /// <summary>Gets the grant the store was handed, which is what a test asserting a resolved grant reads.</summary>
        internal IReadOnlyList<MailFathomPermission>? WrittenGrant { get; private set; }

        internal void AnswerCreateWith(UserCredentialWriteOutcome outcome) => this.Credentials.CreateAsync(
                Arg.Any<Guid>(),
                Arg.Any<MailUserId>(),
                Arg.Any<UserCredentialMethod>(),
                Arg.Any<UserCredentialLookup>(),
                Arg.Any<string>(),
                Arg.Do<IReadOnlyList<MailFathomPermission>>(grant => this.WrittenGrant = grant),
                Arg.Any<CancellationToken>())
            .Returns(outcome);

        internal void AnswerReplaceWith(UserCredentialWriteOutcome outcome) => this.Credentials.ReplaceMaterialAsync(
                Arg.Any<MailUserId>(),
                Arg.Any<Guid>(),
                Arg.Any<UserCredentialMethod>(),
                Arg.Any<UserCredentialLookup>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(outcome);
    }
}
