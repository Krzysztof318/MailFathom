// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
public sealed class OwnerCredentialAdministrationTests
{
    private const string AdministratorIdentity = "admin-key";

    private const string AcceptablePassword = "correcthorsebatterystaple";

    private const string WrittenPublicKey = "-----BEGIN PUBLIC KEY-----readable-----END PUBLIC KEY-----";

    private static readonly MailOwnerId Owner = MailOwnerId.Create(new Guid("0197c0de-0000-7000-8000-00000000ffff"));

    private static readonly Guid CredentialId = new("0197c0de-0000-7000-8000-000000000001");

    private static readonly DateTimeOffset ActedAt = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProvisionPasswordAsync_ACallerGrantedTheCredentialWrite_StoresTheHashUnderAMintedIdentifier()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var provisioning = await harness.Administration.ProvisionPasswordAsync(
            Owner,
            OwnerCredentialUsername.Create("owner"),
            AcceptablePassword.AsMemory(),
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.Written, provisioning.Outcome);
        Assert.NotEqual(Guid.Empty, provisioning.CredentialId);
        Assert.Equal("owner", provisioning.Lookup.Value);
        Assert.Null(provisioning.MintedKey);

        await harness.Credentials.Received(1).CreateAsync(
            provisioning.CredentialId,
            Owner,
            OwnerCredentialMethod.Password,
            Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == "owner"),
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
            Owner,
            OwnerCredentialUsername.Create("owner"),
            AcceptablePassword.AsMemory(),
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Credentials.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(),
            Arg.Any<MailOwnerId>(),
            Arg.Any<OwnerCredentialMethod>(),
            Arg.Any<OwnerCredentialLookup>(),
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
                Owner,
                OwnerCredentialUsername.Create("owner"),
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
            Owner,
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatedApiKeyMinter.Key, provisioning.MintedKey);
        Assert.Equal(StatedApiKeyMinter.Digest, provisioning.Lookup.Value);

        await harness.Credentials.Received(1).CreateAsync(
            provisioning.CredentialId,
            Owner,
            OwnerCredentialMethod.ApiKey,
            Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == StatedApiKeyMinter.Digest),
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
        harness.AnswerCreateWith(OwnerCredentialWriteOutcome.UnknownOwner);

        // Act
        var provisioning = await harness.Administration.ProvisionApiKeyAsync(
            Owner,
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.UnknownOwner, provisioning.Outcome);
        Assert.Null(provisioning.MintedKey);
    }

    [Fact]
    public async Task ProvisionPublicKeyAsync_AReadableKey_StoresItUnderTheFingerprintTheReaderComputed()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var provisioning = await harness.Administration.ProvisionPublicKeyAsync(
            Owner,
            WrittenPublicKey,
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatedPublicKeyReader.Fingerprint, provisioning.Lookup.Value);
        Assert.Null(provisioning.MintedKey);

        await harness.Credentials.Received(1).CreateAsync(
            provisioning.CredentialId,
            Owner,
            OwnerCredentialMethod.PublicKey,
            Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == StatedPublicKeyReader.Fingerprint),
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
                Owner,
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
            Owner,
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
            Owner,
            OwnerCredentialMethod.OAuthSubject,
            Arg.Any<OwnerCredentialLookup>(),
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
            Owner,
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
            Owner,
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
            Owner,
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
        await harness.Administration.ProvisionApiKeyAsync(Owner, [], TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(harness.WrittenGrant);
        Assert.Empty(harness.WrittenGrant);
    }

    /// <summary>
    /// A credential reaches one owner's mail, so an administrative permission on one would be a way into the deployment
    /// rather than into a mailbox. The refusal is published so a boundary can answer with it instead of raising.
    /// </summary>
    [Fact]
    public void FindGrantRefusal_APermissionOfTheAdministrativeSurface_IsRefusedNamingWhatMayBeWrittenInstead()
    {
        // Arrange
        IReadOnlyList<MailFathomPermission> requested = [MailFathomPermission.MailRead, MailFathomPermission.AdminErase];

        // Act
        var refusal = OwnerCredentialAdministration.FindGrantRefusal(requested);

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
        var refusal = OwnerCredentialAdministration.FindGrantRefusal(requested);

        // Assert
        Assert.NotNull(refusal);
    }

    [Fact]
    public void FindGrantRefusal_AnUnwrittenGrantOrOneOfTheMailSurface_IsAccepted()
    {
        // Arrange
        var mailSurface = MailFathomPermission.PublishedFor(ProtectedSurface.Mail);

        // Act
        var unwritten = OwnerCredentialAdministration.FindGrantRefusal(permissions: null);
        var narrowed = OwnerCredentialAdministration.FindGrantRefusal(mailSurface);

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
            Owner,
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
            Owner,
            OwnerCredentialUsername.Create("owner"),
            AcceptablePassword.AsMemory(),
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Auditor.Received(1).RecordCredentialChangeAsync(
            Arg.Is<OwnerCredentialChange>(change =>
                change != null
                && change.Act == OwnerCredentialAct.Provisioned
                && change.CredentialId == provisioning.CredentialId
                && change.Owner == Owner
                && change.Method == OwnerCredentialMethod.Password
                && change.ActingAdministrator == AdministratorIdentity
                && change.OccurredAt == ActedAt),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A record of a mistyped identifier is a record of an attempt, and would read alike beside a real change.</summary>
    [Theory]
    [InlineData(OwnerCredentialWriteOutcome.UnknownOwner)]
    [InlineData(OwnerCredentialWriteOutcome.LookupTaken)]
    [InlineData(OwnerCredentialWriteOutcome.OwnerAtCredentialCeiling)]
    public async Task ProvisionPasswordAsync_AnActThatChangedNothing_IsNotWrittenDown(
        OwnerCredentialWriteOutcome outcome)
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.AnswerCreateWith(outcome);

        // Act
        var provisioning = await harness.Administration.ProvisionPasswordAsync(
            Owner,
            OwnerCredentialUsername.Create("owner"),
            AcceptablePassword.AsMemory(),
            permissions: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(outcome, provisioning.Outcome);

        await harness.Auditor.DidNotReceive().RecordCredentialChangeAsync(
            Arg.Any<OwnerCredentialChange>(),
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
            Owner,
            CredentialId,
            OwnerCredentialUsername.Create("owner"),
            AcceptablePassword.AsMemory(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.Written, rotation.Outcome);
        Assert.Null(rotation.MintedKey);

        await harness.Credentials.Received(1).ReplaceMaterialAsync(
            Owner,
            CredentialId,
            OwnerCredentialMethod.Password,
            Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == "owner"),
            AdministrationHarness.StoredHash,
            Arg.Any<CancellationToken>());

        await harness.Auditor.Received(1).RecordCredentialChangeAsync(
            Arg.Is<OwnerCredentialChange>(change =>
                change != null && change.Act == OwnerCredentialAct.MaterialRotated),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RotateApiKeyAsync_AWrittenRotation_AnswersWithTheKeyTheClientMustPresentFromNowOn()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var rotation = await harness.Administration.RotateApiKeyAsync(
            Owner,
            CredentialId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatedApiKeyMinter.Key, rotation.MintedKey);
        Assert.Equal(StatedApiKeyMinter.Digest, rotation.Lookup.Value);

        await harness.Credentials.Received(1).ReplaceMaterialAsync(
            Owner,
            CredentialId,
            OwnerCredentialMethod.ApiKey,
            Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == StatedApiKeyMinter.Digest),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RotateApiKeyAsync_ARotationThatWroteNothing_WithholdsTheKeyItWouldHaveHandedOver()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.AnswerReplaceWith(OwnerCredentialWriteOutcome.UnknownCredential);

        // Act
        var rotation = await harness.Administration.RotateApiKeyAsync(
            Owner,
            CredentialId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.UnknownCredential, rotation.Outcome);
        Assert.Null(rotation.MintedKey);
    }

    [Fact]
    public async Task ReplacePublicKeyAsync_AReadableKey_ReplacesBothTheMaterialAndTheFingerprintItIsResolvedBy()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var rotation = await harness.Administration.ReplacePublicKeyAsync(
            Owner,
            CredentialId,
            WrittenPublicKey,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatedPublicKeyReader.Fingerprint, rotation.Lookup.Value);

        await harness.Credentials.Received(1).ReplaceMaterialAsync(
            Owner,
            CredentialId,
            OwnerCredentialMethod.PublicKey,
            Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == StatedPublicKeyReader.Fingerprint),
            WrittenPublicKey,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, OwnerCredentialAct.Enabled)]
    [InlineData(false, OwnerCredentialAct.Disabled)]
    public async Task SetEnabledAsync_EitherDecision_IsWrittenDownAsTheActItWas(bool enabled, OwnerCredentialAct act)
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await harness.Administration.SetEnabledAsync(
            Owner,
            CredentialId,
            enabled,
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Auditor.Received(1).RecordCredentialChangeAsync(
            Arg.Is<OwnerCredentialChange>(change => change != null && change.Act == act),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_ACallerGrantedTheCredentialWrite_RemovesTheCredentialAndRecordsIt()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await harness.Administration.DeleteAsync(Owner, CredentialId, TestContext.Current.CancellationToken);

        // Assert
        await harness.Credentials.Received(1).DeleteAsync(Owner, CredentialId, Arg.Any<CancellationToken>());

        await harness.Auditor.Received(1).RecordCredentialChangeAsync(
            Arg.Is<OwnerCredentialChange>(change => change != null && change.Act == OwnerCredentialAct.Deleted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadCredentialsAsync_ACallerGrantedTheAdministrativeRead_IsAnsweredWithWhatTheOwnerHolds()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminRead);
        var held = new OwnerCredential(
            CredentialId,
            Owner,
            OwnerCredentialMethod.Password,
            OwnerCredentialLookup.ForUsername(OwnerCredentialUsername.Create("owner")),
            [MailFathomPermission.MailRead],
            Enabled: true,
            Version: 1,
            ActedAt,
            ActedAt);

        harness.Credentials.ReadForOwnerAsync(Owner, Arg.Any<CancellationToken>()).Returns([held]);

        // Act
        var credentials = await harness.Administration.ReadCredentialsAsync(
            Owner,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([held], credentials);
    }

    [Fact]
    public async Task ReadOwnersAsync_ACallerGrantedTheAdministrativeRead_IsAnsweredWithWhatTheDirectoryHolds()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminRead);

        // Act
        var owners = await harness.Administration.ReadOwnersAsync(
            OwnerCredential.MaximumListedPerOwner,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Owner], owners);
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
            harness.Administration.ReadCredentialsAsync(Owner, TestContext.Current.CancellationToken));

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
                Owner,
                OwnerCredentialUsername.Create("owner"),
                AcceptablePassword.AsMemory(),
                permissions: null,
                cancellationToken)),
            RefusalOf(() => harness.Administration.ProvisionApiKeyAsync(Owner, permissions: null, cancellationToken)),
            RefusalOf(() => harness.Administration.ProvisionPublicKeyAsync(
                Owner,
                WrittenPublicKey,
                permissions: null,
                cancellationToken)),
            RefusalOf(() => harness.Administration.ProvisionOAuthSubjectAsync(
                Owner,
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
            Owner,
            CredentialId,
            OwnerCredentialUsername.Create("owner"),
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
            harness.Administration.DeleteAsync(Owner, CredentialId, TestContext.Current.CancellationToken));

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
    /// <remarks>Hand-written for the same reason the hasher is: <see cref="IOwnerApiKeyMinter.TryDigest" /> takes a span.</remarks>
    private sealed class StatedApiKeyMinter : IOwnerApiKeyMinter
    {
        internal const string Key = "mfk_stated-key";

        internal const string Digest = "stated-digest";

        public MintedOwnerApiKey Mint() => new(Key, OwnerCredentialLookup.ForDigest(Digest));

        public bool TryDigest(ReadOnlySpan<char> presentedKey, out OwnerCredentialLookup lookup)
        {
            lookup = presentedKey.SequenceEqual(Key) ? OwnerCredentialLookup.ForDigest(Digest) : default;

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
                ? new ClientPublicKey(written, OwnerCredentialLookup.ForDigest(Fingerprint))
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
            // A caller acting for nobody's mail, which is the only shape the administrative surface produces: the owner
            // every act here names comes from its own argument rather than from whoever was admitted.
            principals.Current.Returns(AuthorizedPrincipal.Caller(AdministratorIdentity, [granted]));

            this.Credentials = Substitute.For<IOwnerCredentialStore>();
            this.AnswerCreateWith(OwnerCredentialWriteOutcome.Written);
            this.AnswerReplaceWith(OwnerCredentialWriteOutcome.Written);
            this.Credentials.SetEnabledAsync(
                    Arg.Any<MailOwnerId>(),
                    Arg.Any<Guid>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(OwnerCredentialWriteOutcome.Written);
            this.Credentials.DeleteAsync(Arg.Any<MailOwnerId>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(OwnerCredentialWriteOutcome.Written);
            this.Credentials.ReadForOwnerAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
                .Returns([]);

            this.PasswordHasher = new RecordingPasswordHasher();

            this.Auditor = Substitute.For<IOwnerCredentialAuditor>();

            this.Owners = Substitute.For<IMailOwnerDirectory>();
            this.Owners.ReadOwnersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<MailOwnerRecord>>(
                    [new MailOwnerRecord(Owner, "owner", DocumentWrittenAtRuntime: false)]));

            this.Administration = new OwnerCredentialAdministration(
                new AccessAuthorization(principals),
                this.Owners,
                this.Credentials,
                this.PasswordHasher,
                new StatedApiKeyMinter(),
                new StatedPublicKeyReader(),
                this.Auditor,
                new FakeTimeProvider(ActedAt));
        }

        internal OwnerCredentialAdministration Administration { get; }

        internal IMailOwnerDirectory Owners { get; }

        internal IOwnerCredentialStore Credentials { get; }

        internal RecordingPasswordHasher PasswordHasher { get; }

        internal IOwnerCredentialAuditor Auditor { get; }

        /// <summary>Gets the grant the store was handed, which is what a test asserting a resolved grant reads.</summary>
        internal IReadOnlyList<MailFathomPermission>? WrittenGrant { get; private set; }

        internal void AnswerCreateWith(OwnerCredentialWriteOutcome outcome) => this.Credentials.CreateAsync(
                Arg.Any<Guid>(),
                Arg.Any<MailOwnerId>(),
                Arg.Any<OwnerCredentialMethod>(),
                Arg.Any<OwnerCredentialLookup>(),
                Arg.Any<string>(),
                Arg.Do<IReadOnlyList<MailFathomPermission>>(grant => this.WrittenGrant = grant),
                Arg.Any<CancellationToken>())
            .Returns(outcome);

        internal void AnswerReplaceWith(OwnerCredentialWriteOutcome outcome) => this.Credentials.ReplaceMaterialAsync(
                Arg.Any<MailOwnerId>(),
                Arg.Any<Guid>(),
                Arg.Any<OwnerCredentialMethod>(),
                Arg.Any<OwnerCredentialLookup>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(outcome);
    }
}
