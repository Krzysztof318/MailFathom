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
public sealed class OwnerPasswordCredentialAdministrationTests
{
    private const string AdministratorIdentity = "admin-key";

    private const string AcceptablePassword = "correcthorsebatterystaple";

    private static readonly MailOwnerId Owner = MailOwnerId.Create(new Guid("0197c0de-0000-7000-8000-00000000ffff"));

    private static readonly Guid CredentialId = new("0197c0de-0000-7000-8000-000000000001");

    private static readonly DateTimeOffset ActedAt = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProvisionAsync_ACallerGrantedTheCredentialWrite_StoresTheHashUnderAMintedIdentifier()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var provisioning = await harness.Administration.ProvisionAsync(
            Owner,
            OwnerCredentialUsername.Create("owner"),
            AcceptablePassword.AsMemory(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.Written, provisioning.Outcome);
        Assert.NotEqual(Guid.Empty, provisioning.CredentialId);

        await harness.Credentials.Received(1).CreateAsync(
            provisioning.CredentialId,
            Owner,
            Arg.Is<OwnerCredentialUsername>(username => username.Value == "owner"),
            AdministrationHarness.StoredHash,
            Arg.Any<CancellationToken>());
    }

    /// <summary>The plaintext is turned into a stored representation inside the call and never reaches the store.</summary>
    [Fact]
    public async Task ProvisionAsync_APassword_ReachesTheStoreOnlyAsWhatTheHasherProduced()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await harness.Administration.ProvisionAsync(
            Owner,
            OwnerCredentialUsername.Create("owner"),
            AcceptablePassword.AsMemory(),
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Credentials.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(),
            Arg.Any<MailOwnerId>(),
            Arg.Any<OwnerCredentialUsername>(),
            Arg.Is<string>(stored => stored != null && stored.Contains(AcceptablePassword, StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The policy is checked here as well as at the boundary. Reaching it is an entrypoint that did not check rather
    /// than an operator's mistake, so it raises instead of composing an answer somebody would read.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_APasswordThePolicyRefuses_ThrowsWithoutNamingWhatWasWritten()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() => harness.Administration.ProvisionAsync(
            Owner,
            OwnerCredentialUsername.Create("owner"),
            "short".AsMemory(),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain("short", refusal.Message, StringComparison.Ordinal);

        await harness.Credentials.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(),
            Arg.Any<MailOwnerId>(),
            Arg.Any<OwnerCredentialUsername>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_AWrittenCredential_IsRecordedAgainstTheAdministratorThatAskedForIt()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var provisioning = await harness.Administration.ProvisionAsync(
            Owner,
            OwnerCredentialUsername.Create("owner"),
            AcceptablePassword.AsMemory(),
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Auditor.Received(1).RecordCredentialChangeAsync(
            Arg.Is<OwnerCredentialChange>(change =>
                change != null
                && change.Act == OwnerCredentialAct.Provisioned
                && change.CredentialId == provisioning.CredentialId
                && change.Owner == Owner
                && change.ActingAdministrator == AdministratorIdentity
                && change.OccurredAt == ActedAt),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A record of a mistyped identifier is a record of an attempt, and would read alike beside a real change.</summary>
    [Theory]
    [InlineData(OwnerCredentialWriteOutcome.UnknownOwner)]
    [InlineData(OwnerCredentialWriteOutcome.UsernameTaken)]
    public async Task ProvisionAsync_AnActThatChangedNothing_IsNotWrittenDown(OwnerCredentialWriteOutcome outcome)
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.Credentials.CreateAsync(
                Arg.Any<Guid>(),
                Arg.Any<MailOwnerId>(),
                Arg.Any<OwnerCredentialUsername>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(outcome);

        // Act
        var provisioning = await harness.Administration.ProvisionAsync(
            Owner,
            OwnerCredentialUsername.Create("owner"),
            AcceptablePassword.AsMemory(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(outcome, provisioning.Outcome);

        await harness.Auditor.DidNotReceive().RecordCredentialChangeAsync(
            Arg.Any<OwnerCredentialChange>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RotatePasswordAsync_ACallerGrantedTheCredentialWrite_ReplacesTheStoredHashAndRecordsIt()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var outcome = await harness.Administration.RotatePasswordAsync(
            Owner,
            CredentialId,
            AcceptablePassword.AsMemory(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OwnerCredentialWriteOutcome.Written, outcome);

        await harness.Credentials.Received(1).ReplacePasswordAsync(
            Owner,
            CredentialId,
            AdministrationHarness.StoredHash,
            Arg.Any<CancellationToken>());

        await harness.Auditor.Received(1).RecordCredentialChangeAsync(
            Arg.Is<OwnerCredentialChange>(change => change != null && change.Act == OwnerCredentialAct.PasswordRotated),
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
        await harness.Administration.SetEnabledAsync(Owner, CredentialId, enabled, TestContext.Current.CancellationToken);

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
        var held = new OwnerPasswordCredential(
            CredentialId,
            Owner,
            OwnerCredentialUsername.Create("owner"),
            Enabled: true,
            Version: 1,
            ActedAt,
            ActedAt);

        harness.Credentials.ReadForOwnerAsync(Owner, Arg.Any<CancellationToken>()).Returns([held]);

        // Act
        var credentials = await harness.Administration.ReadCredentialsAsync(Owner, TestContext.Current.CancellationToken);

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
            harness.Administration.ReadCredentialsAsync(Owner, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    [Fact]
    public async Task ProvisionAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(MailFathomPermission.AdminRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.ProvisionAsync(
                Owner,
                OwnerCredentialUsername.Create("owner"),
                AcceptablePassword.AsMemory(),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCredentialsWrite, refusal.RequiredPermission);

        await harness.Credentials.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(),
            Arg.Any<MailOwnerId>(),
            Arg.Any<OwnerCredentialUsername>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
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

            this.Credentials = Substitute.For<IOwnerPasswordCredentialStore>();
            this.Credentials.CreateAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<MailOwnerId>(),
                    Arg.Any<OwnerCredentialUsername>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(OwnerCredentialWriteOutcome.Written);
            this.Credentials.ReplacePasswordAsync(
                    Arg.Any<MailOwnerId>(),
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(OwnerCredentialWriteOutcome.Written);
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

            this.Administration = new OwnerPasswordCredentialAdministration(
                new AccessAuthorization(principals),
                this.Credentials,
                this.PasswordHasher,
                this.Auditor,
                new FakeTimeProvider(ActedAt));
        }

        internal OwnerPasswordCredentialAdministration Administration { get; }

        internal IOwnerPasswordCredentialStore Credentials { get; }

        internal RecordingPasswordHasher PasswordHasher { get; }

        internal IOwnerCredentialAuditor Auditor { get; }
    }
}
