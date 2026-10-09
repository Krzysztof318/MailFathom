// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.DefaultAdministrator;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Startup;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>Covers what a start records about the default administrator and what it tells an operator to act on.</summary>
/// <remarks>Which password a start applies, and when, is the bootstrap's and is covered beside it; this asserts the reports every start repeats.</remarks>
public sealed class DefaultAdministratorStartupGateTests
{
    private const string StoredHash = "$mf1$stored$";

    private static readonly UserId Administrator = UserId.Create(Guid.Parse("0197c0de-0000-7000-8000-00000000a001"));

    /// <summary>The shipped password is the one every copy of the assets carries, so every start says so until it changes rather than only the first.</summary>
    [Fact]
    public async Task StartAsync_TheAdministratorStillSigningInWithTheShippedPassword_WarnsAndRecordsIt()
    {
        // Arrange
        var harness = new GateHarness();
        harness.HoldsPassword(DefaultAdministratorBootstrap.ShippedPassword);

        // Act
        await harness.StartAsync();

        // Assert
        Assert.Equal(Administrator, harness.Recorded.User);
        Assert.True(harness.Gates.Completed);
        Assert.Contains(
            harness.Logs.Records,
            static record => record.Level == LogLevel.Warning && record.Message.Contains("mfctl credential rotate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_ARotatedPassword_WarnsAboutNothing()
    {
        // Arrange
        var harness = new GateHarness();
        harness.HoldsPassword("a-passphrase-somebody-chose");

        // Act
        await harness.StartAsync();

        // Assert
        Assert.DoesNotContain(harness.Logs.Records, static record => record.Level >= LogLevel.Warning);
    }

    /// <summary>A restriction written while a proxy was named stays stored after the proxy is removed from the configuration, so the start that finds it says what it is now judged against.</summary>
    [Fact]
    public async Task StartAsync_ARestrictedCredentialWithNoProxyNamed_WarnsThatThePeerIsJudged()
    {
        // Arrange
        var harness = new GateHarness();
        harness.HoldsPassword("a-passphrase-somebody-chose");
        harness.Credentials.AnyRestrictedToSourceNetworksAsync(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        await harness.StartAsync();

        // Assert
        Assert.Contains(
            harness.Logs.Records,
            static record => record.Level == LogLevel.Warning && record.Message.Contains("ReverseProxy:TrustedProxies", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_ARestrictedCredentialBehindANamedProxy_WarnsAboutNothing()
    {
        // Arrange
        var harness = new GateHarness();
        harness.HoldsPassword("a-passphrase-somebody-chose");
        harness.Credentials.AnyRestrictedToSourceNetworksAsync(Arg.Any<CancellationToken>()).Returns(true);
        harness.ReverseProxy.TrustedProxies.Add("192.0.2.10");

        // Act
        await harness.StartAsync();

        // Assert
        Assert.DoesNotContain(harness.Logs.Records, static record => record.Level >= LogLevel.Warning);
    }

    /// <summary>An endpoint authenticating nobody serves its callers as the default administrator, so removing it leaves that endpoint serving nobody, which the start reports.</summary>
    [Fact]
    public async Task StartAsync_ARemovedAdministratorOnAnEndpointAuthenticatingNobody_WarnsAndRecordsNobody()
    {
        // Arrange
        var harness = new GateHarness();
        harness.Store.RecordOnceAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new DefaultAdministratorRecord(Administrator: null, PasswordSettingApplied: true));
        harness.AdminEndpoint.Enabled = true;

        // Act
        await harness.StartAsync();

        // Assert
        Assert.Null(harness.Recorded.User);
        Assert.Contains(
            harness.Logs.Records,
            static record => record.Level == LogLevel.Warning && record.Message.Contains("serves no caller", StringComparison.Ordinal));
    }

    /// <summary>A refused value is inert only while <c>admin</c> holds a password, so the start says it was neither applied nor recorded rather than calling it recorded.</summary>
    [Fact]
    public async Task StartAsync_ARefusedValueWhileTheAdministratorHoldsAPassword_WarnsThatItWasNotRecorded()
    {
        // Arrange
        var harness = new GateHarness();
        harness.Store.RecordOnceAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: false));
        harness.Settings[DefaultAdministratorStartupGate.PasswordVariableName] = "too-short";
        harness.Credentials.ReadForUserAsync(Administrator, Arg.Any<CancellationToken>())
            .Returns([
                new UserCredential(
                    Guid.Parse("0197c0de-0000-7000-8000-00000000c001"),
                    Administrator,
                    UserCredentialMethod.Password,
                    UserCredentialLookup.ForUsername(UserCredentialUsername.Create(DefaultAdministratorBootstrap.Username)),
                    Permissions: null,
                    Enabled: true,
                    Version: 1,
                    new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)),
            ]);

        // Act
        await harness.StartAsync();

        // Assert
        Assert.Contains(
            harness.Logs.Records,
            static record => record.Level == LogLevel.Warning && record.Message.Contains("not recorded as applied", StringComparison.Ordinal));
        Assert.DoesNotContain(
            harness.Logs.Records,
            static record => record.Message.Contains("was recorded as applied", StringComparison.Ordinal));
    }

    private sealed class GateHarness
    {
        internal GateHarness()
        {
            this.Store.RecordOnceAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new DefaultAdministratorRecord(Administrator, PasswordSettingApplied: true));
        }

        internal IDefaultAdministratorStore Store { get; } = Substitute.For<IDefaultAdministratorStore>();

        internal IUserCredentialStore Credentials { get; } = Substitute.For<IUserCredentialStore>();

        internal AdminEndpointOptions AdminEndpoint { get; } = new();

        internal Dictionary<string, string?> Settings { get; } = [];

        internal ReverseProxyOptions ReverseProxy { get; } = new();

        internal RecordedDefaultAdministrator Recorded { get; } = new();

        internal HostStartupGates Gates { get; } = new(HostStartupGate.DefaultAdministrator);

        internal RecordingLoggerProvider Logs { get; } = new();

        private OneKnownPasswordHasher Hasher { get; } = new();

        internal void HoldsPassword(string password)
        {
            this.Hasher.Known = password;
            this.Credentials.FindPasswordAsync(Arg.Any<UserCredentialLogin>(), Arg.Any<CancellationToken>())
                .Returns(new ResolvedUserCredential(
                    Guid.Parse("0197c0de-0000-7000-8000-00000000c001"),
                    Administrator,
                    UserCredentialMethod.Password,
                    MailFathomPermission.PublishedFor(ProtectedSurface.Mail),
                    Enabled: true,
                    StoredHash,
                    new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: false)));
        }

        internal async Task StartAsync()
        {
            ServiceCollection services = [];
            services.AddSingleton(this.Credentials);
            services.AddSingleton(_ => new DefaultAdministratorBootstrap(
                AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process),
                this.Store,
                this.Credentials,
                this.Hasher,
                new FakeTimeProvider(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero))));

            await using var provider = services.BuildServiceProvider();
            using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(this.Logs));

            var gate = new DefaultAdministratorStartupGate(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new ConfigurationBuilder().AddInMemoryCollection(this.Settings).Build(),
                Options.Create(this.AdminEndpoint),
                Options.Create(this.ReverseProxy),
                this.Recorded,
                this.Gates,
                loggerFactory.CreateLogger<DefaultAdministratorStartupGate>());

            await gate.StartAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Recognizes one password against the one stored record, without deriving anything.</summary>
    /// <remarks>Hand-written rather than substituted, because the members take the password as a <see cref="ReadOnlySpan{T}" /> and a dynamic proxy cannot carry a by-ref-like argument through its invocation.</remarks>
    private sealed class OneKnownPasswordHasher : IPasswordHasher
    {
        internal string Known { get; set; } = string.Empty;

        public string HashDecoy() => "$mf1$decoy$";

        public string Hash(ReadOnlySpan<char> password) => StoredHash;

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password) =>
            string.Equals(storedHash, StoredHash, StringComparison.Ordinal) && password.SequenceEqual(this.Known)
                ? PasswordVerification.Succeeded
                : PasswordVerification.Failed;
    }
}
