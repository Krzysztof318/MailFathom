// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.DefaultAdministrator;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Access.DefaultAdministrator;

/// <summary>Covers what a start does about the default administrator and the password setting it was started with.</summary>
public sealed class DefaultAdministratorBootstrapTests
{
    private const string SettingName = "MAILFATHOM_ADMIN_PASSWORD";

    private const string ChosenPassword = "a-password-long-enough";

    private static readonly UserId Administrator = UserId.Create(Guid.Parse("2b6e4f1a-7c3d-4e8f-9a0b-1c2d3e4f5a6b"));

    [Fact]
    public async Task StartAsync_AFirstStartCarryingASetting_GivesTheAdministratorThatPasswordOnTheMailHalfAlone()
    {
        // Arrange
        var harness = new BootstrapHarness(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: false));

        // Act
        var start = await harness.StartAsync(ChosenPassword);

        // Assert
        Assert.Equal(Administrator, start.Administrator);
        Assert.Equal(DefaultAdministratorPasswordOutcome.Applied, start.PasswordSetting);
        Assert.Equal(1, harness.Hasher.HashCount);

        await harness.Store.Received(1).ApplyPasswordSettingAsync(
            Administrator,
            Arg.Any<Guid>(),
            Arg.Is<UserCredentialLookup>(lookup => lookup.Value == DefaultAdministratorBootstrap.Username),
            RecordingPasswordHasher.StoredHash,
            Arg.Is<IReadOnlyList<MailFathomPermission>>(permissions =>
                permissions!.SequenceEqual(MailFathomPermission.PublishedFor(ProtectedSurface.Mail))),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The setting is read once per deployment, so changing it later cannot change a password somebody already rotated.</summary>
    [Fact]
    public async Task StartAsync_ASettingAnEarlierStartApplied_IsIgnored()
    {
        // Arrange
        var harness = new BootstrapHarness(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: true));

        // Act
        var start = await harness.StartAsync(ChosenPassword);

        // Assert
        Assert.Null(start.PasswordSetting);
        Assert.Equal(0, harness.Hasher.HashCount);
        await harness.Store.DidNotReceiveWithAnyArgs().ApplyPasswordSettingAsync(default, default, default, default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_AStartCarryingNoSetting_RecordsTheAdministratorAndAppliesNothing()
    {
        // Arrange
        var harness = new BootstrapHarness(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: false));

        // Act
        var start = await harness.StartAsync(passwordSetting: null);

        // Assert
        Assert.Equal(Administrator, start.Administrator);
        Assert.Null(start.PasswordSetting);
        await harness.Store.ReceivedWithAnyArgs(1).RecordOnceAsync(default, default, default, TestContext.Current.CancellationToken);
        await harness.Store.DidNotReceiveWithAnyArgs().ApplyPasswordSettingAsync(default, default, default, default!, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Removing the default administrator is final, so a later start brings back nobody to give the setting to.</summary>
    [Fact]
    public async Task StartAsync_ADeploymentThatRemovedTheAdministrator_EstablishesNobody()
    {
        // Arrange
        var harness = new BootstrapHarness(new DefaultAdministratorRecord(Administrator: null, PasswordSettingApplied: false));

        // Act
        var start = await harness.StartAsync(ChosenPassword);

        // Assert
        Assert.Null(start.Administrator);
        Assert.Null(start.PasswordSetting);
        Assert.False(start.SignsInWithShippedPassword);
        await harness.Store.DidNotReceiveWithAnyArgs().ApplyPasswordSettingAsync(default, default, default, default!, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>The shipped value is public whatever its length, so holding it to the minimum would protect nothing.</summary>
    [Fact]
    public async Task StartAsync_TheShippedValue_IsAppliedDespiteBeingShorterThanThePolicy()
    {
        // Arrange
        var harness = new BootstrapHarness(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: false));

        // Act
        var start = await harness.StartAsync(DefaultAdministratorBootstrap.ShippedPassword);

        // Assert
        Assert.Equal(DefaultAdministratorPasswordOutcome.Applied, start.PasswordSetting);
    }

    /// <summary>A value an operator chose and the policy refuses stops the start before a password is written, and the refusal never quotes it.</summary>
    [Fact]
    public async Task StartAsync_AnyOtherValueThePolicyRefuses_StopsTheStartWithoutApplyingIt()
    {
        // Arrange
        var harness = new BootstrapHarness(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: false));

        // Act
        var refusal = await Assert.ThrowsAsync<DefaultAdministratorUnusableException>(() => harness.StartAsync("too-short"));

        // Assert
        Assert.Equal(MailFathomErrorCode.DefaultAdministratorUnusable, refusal.ErrorCode);
        Assert.Contains(SettingName, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("too-short", refusal.Message, StringComparison.Ordinal);
        await harness.Store.DidNotReceiveWithAnyArgs().ApplyPasswordSettingAsync(default, default, default, default!, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A setting already applied is ignored whatever it now holds, so a value the policy would refuse no longer stops a start.</summary>
    [Fact]
    public async Task StartAsync_AValueThePolicyRefusesAfterTheSettingWasApplied_IsIgnored()
    {
        // Arrange
        var harness = new BootstrapHarness(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: true));

        // Act
        var start = await harness.StartAsync("too-short");

        // Assert
        Assert.Equal(Administrator, start.Administrator);
        Assert.Null(start.PasswordSetting);
    }

    /// <summary>Applying a password to a username somebody else signs in with would sign that person in as the administrator.</summary>
    [Fact]
    public async Task StartAsync_AUsernameAnotherUserHolds_StopsTheStart()
    {
        // Arrange
        var harness = new BootstrapHarness(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: false));
        harness.AnswerApplyWith(DefaultAdministratorPasswordOutcome.UsernameTaken);

        // Act
        var refusal = await Assert.ThrowsAsync<DefaultAdministratorUnusableException>(() => harness.StartAsync(ChosenPassword));

        // Assert
        Assert.Equal(MailFathomErrorCode.DefaultAdministratorUnusable, refusal.ErrorCode);
    }

    [Fact]
    public async Task StartAsync_AnAdministratorWhosePasswordStillVerifiesAgainstTheShippedValue_ReportsIt()
    {
        // Arrange
        var harness = new BootstrapHarness(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: true));
        harness.HoldPasswordOf(Administrator);
        harness.Hasher.Answer = PasswordVerification.Succeeded;

        // Act
        var start = await harness.StartAsync(passwordSetting: null);

        // Assert
        Assert.True(start.SignsInWithShippedPassword);
    }

    [Fact]
    public async Task StartAsync_AnAdministratorWhosePasswordWasRotated_ReportsNothing()
    {
        // Arrange
        var harness = new BootstrapHarness(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: true));
        harness.HoldPasswordOf(Administrator);
        harness.Hasher.Answer = PasswordVerification.Failed;

        // Act
        var start = await harness.StartAsync(passwordSetting: null);

        // Assert
        Assert.False(start.SignsInWithShippedPassword);
    }

    /// <summary>A credential signed in as <c>admin</c> that belongs to somebody else says nothing about the administrator's own password.</summary>
    [Fact]
    public async Task StartAsync_AShippedPasswordOnAnotherUsersCredential_IsNotTheAdministrators()
    {
        // Arrange
        var harness = new BootstrapHarness(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: true));
        harness.HoldPasswordOf(UserId.Create(Guid.Parse("9f8e7d6c-5b4a-4392-8170-6f5e4d3c2b1a")));
        harness.Hasher.Answer = PasswordVerification.Succeeded;

        // Act
        var start = await harness.StartAsync(passwordSetting: null);

        // Assert
        Assert.False(start.SignsInWithShippedPassword);
    }

    [Fact]
    public async Task StartAsync_UnderAnyPrincipalButTheProcess_IsRefused()
    {
        // Arrange
        var harness = new BootstrapHarness(
            new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: false),
            AuthorizedPrincipal.Caller("an-operator", [MailFathomPermission.AdminRead]));

        // Act
        var start = () => harness.StartAsync(ChosenPassword);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(start);
    }

    private sealed class BootstrapHarness
    {
        internal BootstrapHarness(DefaultAdministratorRecord record, AuthorizedPrincipal? principal = null)
        {
            this.Store.RecordOnceAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(record);
            this.AnswerApplyWith(DefaultAdministratorPasswordOutcome.Applied);

            this.Bootstrap = new DefaultAdministratorBootstrap(
                AccessAuthorizations.ForPrincipal(principal ?? AuthorizedPrincipal.Process),
                this.Store,
                this.Credentials,
                this.Hasher,
                new FakeTimeProvider(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)));
        }

        internal IDefaultAdministratorStore Store { get; } = Substitute.For<IDefaultAdministratorStore>();

        internal IUserCredentialStore Credentials { get; } = Substitute.For<IUserCredentialStore>();

        internal RecordingPasswordHasher Hasher { get; } = new();

        private DefaultAdministratorBootstrap Bootstrap { get; }

        internal Task<DefaultAdministratorStart> StartAsync(string? passwordSetting) =>
            this.Bootstrap.StartAsync(passwordSetting, SettingName, TestContext.Current.CancellationToken);

        internal void AnswerApplyWith(DefaultAdministratorPasswordOutcome outcome) => this.Store.ApplyPasswordSettingAsync(
                Arg.Any<UserId>(),
                Arg.Any<Guid>(),
                Arg.Any<UserCredentialLookup>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<MailFathomPermission>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(outcome);

        internal void HoldPasswordOf(UserId user) => this.Credentials
            .FindPasswordAsync(Arg.Any<UserCredentialLogin>(), Arg.Any<CancellationToken>())
            .Returns(new ResolvedUserCredential(
                Guid.Parse("5d4c3b2a-1f0e-4d9c-8b7a-6f5e4d3c2b1a"),
                user,
                UserCredentialMethod.Password,
                MailFathomPermission.PublishedFor(ProtectedSurface.Mail),
                Enabled: true,
                Material: RecordingPasswordHasher.StoredHash,
                UserEndpointAccess.Everywhere));
    }

    /// <summary>Answers verification as a test states and counts derivations.</summary>
    /// <remarks>Hand-written because the members take the password as a <see cref="ReadOnlySpan{T}" />, which a dynamic proxy cannot carry.</remarks>
    private sealed class RecordingPasswordHasher : IPasswordHasher
    {
        internal const string StoredHash = "stored-hash";

        internal int HashCount { get; private set; }

        internal PasswordVerification Answer { get; set; } = PasswordVerification.Failed;

        public string HashDecoy() => StoredHash;

        public string Hash(ReadOnlySpan<char> password)
        {
            this.HashCount++;

            return StoredHash;
        }

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password) => this.Answer;
    }
}
