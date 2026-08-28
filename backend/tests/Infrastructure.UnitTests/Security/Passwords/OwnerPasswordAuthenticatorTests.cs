// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Security.Passwords;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.Passwords;

/// <summary>Covers which presented credentials authenticate, what every refusal costs, and what a refusal is allowed to distinguish.</summary>
public sealed class OwnerPasswordAuthenticatorTests
{
    private const string SurfaceName = "Client";

    private const string Password = "correcthorsebattery";

    private const string StoredHash = "$mf1$stored$";

    private const int AttemptsPerMinute = 10;

    private static readonly MailOwnerId Owner = MailOwnerId.Create(new Guid("0197c0de-0000-7000-8000-00000000ffff"));

    private static readonly Guid CredentialId = new("0197c0de-0000-7000-8000-000000000001");

    [Fact]
    public async Task AuthenticateAsync_TheStoredCredential_AuthenticatesNamingTheCredentialAndItsOwner()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        harness.Holds(enabled: true);

        // Act
        var result = await harness.AuthenticateAsync(Header("owner", Password));

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(CredentialId, result.AuthenticatedCredentialId);
        Assert.Equal(Owner, result.Owner);
        Assert.Null(result.Rejection);
    }

    /// <summary>The username is folded before it is resolved, so a person's capitalization reaches the one stored spelling.</summary>
    [Fact]
    public async Task AuthenticateAsync_AUsernameTypedWithCapitalsAndSpace_ResolvesTheCanonicalOne()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        harness.Holds(enabled: true);

        // Act
        var result = await harness.AuthenticateAsync(Header("  Owner  ", Password));

        // Assert
        Assert.True(result.Succeeded);

        await harness.Credentials.Received(1).FindByUsernameAsync(
            Arg.Is<OwnerCredentialUsername>(username => username.Value == "owner"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unknown username, a wrong password, and a disabled credential are one answer. Telling them apart is what would
    /// let a caller enumerate the accounts this deployment holds.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_AUsernameNobodyHolds_IsRefusedAsAnUnrecognizedCredential()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(Header("nobody", Password));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(OwnerPasswordRejection.CredentialUnrecognized, result.Rejection);
    }

    [Fact]
    public async Task AuthenticateAsync_TheWrongPassword_IsRefusedAsAnUnrecognizedCredential()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        harness.Holds(enabled: true);

        // Act
        var result = await harness.AuthenticateAsync(Header("owner", "the-wrong-password"));

        // Assert
        Assert.Equal(OwnerPasswordRejection.CredentialUnrecognized, result.Rejection);
    }

    [Fact]
    public async Task AuthenticateAsync_ADisabledCredentialAndTheRightPassword_IsRefusedAsAnUnrecognizedCredential()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        harness.Holds(enabled: false);

        // Act
        var result = await harness.AuthenticateAsync(Header("owner", Password));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(OwnerPasswordRejection.CredentialUnrecognized, result.Rejection);
    }

    /// <summary>
    /// A username that resolves nothing is still compared, against a record derived at construction. Without it the
    /// refusal would return in the time of one indexed read and a client could time the held accounts apart from the
    /// rest.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_AUsernameNobodyHolds_StillSpendsOneVerificationAgainstSomethingOtherThanAStoredRecord()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();

        // Act
        await harness.AuthenticateAsync(Header("nobody", Password));

        // Assert
        Assert.Equal(1, harness.PasswordHasher.VerificationCount);
        Assert.DoesNotContain(StoredHash, harness.PasswordHasher.VerifiedAgainst, StringComparer.Ordinal);
    }

    /// <summary>A held username and one nobody holds spend the same work, which is what the two costs being equal means.</summary>
    [Fact]
    public async Task AuthenticateAsync_AHeldUsernameAndOneNobodyHolds_SpendTheSameNumberOfVerifications()
    {
        // Arrange
        using var held = new AuthenticatorHarness();
        held.Holds(enabled: true);
        using var unheld = new AuthenticatorHarness();

        // Act
        await held.AuthenticateAsync(Header("owner", "the-wrong-password"));
        await unheld.AuthenticateAsync(Header("nobody", "the-wrong-password"));

        // Assert
        Assert.Equal(held.PasswordHasher.VerificationCount, unheld.PasswordHasher.VerificationCount);
    }

    [Theory]
    [InlineData(null, OwnerPasswordRejection.CredentialMissing)]
    [InlineData("", OwnerPasswordRejection.CredentialMissing)]
    [InlineData("   ", OwnerPasswordRejection.CredentialMissing)]
    [InlineData("Bearer an-opaque-api-key", OwnerPasswordRejection.CredentialMalformed)]
    [InlineData("Basic not base64!", OwnerPasswordRejection.CredentialMalformed)]
    public async Task AuthenticateAsync_AHeaderCarryingNoBasicCredential_IsRefusedWithoutReachingTheStore(
        string? headerValue,
        OwnerPasswordRejection expected)
    {
        // Arrange
        using var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(headerValue);

        // Assert
        Assert.Equal(expected, result.Rejection);
        Assert.Equal(0, harness.PasswordHasher.VerificationCount);

        await harness.Credentials.DidNotReceiveWithAnyArgs().FindByUsernameAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>A name no username can be folded from is refused before any capacity is spent and before any hash is computed.</summary>
    [Fact]
    public async Task AuthenticateAsync_AUserIdThatIsNoUsername_IsRefusedWithoutVerifyingAnything()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();

        // Act
        var result = await harness.AuthenticateAsync(Header("owner name", Password));

        // Assert
        Assert.Equal(OwnerPasswordRejection.UsernameUnusable, result.Rejection);
        Assert.Equal(0, harness.PasswordHasher.VerificationCount);
    }

    /// <summary>The bound is what stops a guessing client spending this deployment's key derivations without limit.</summary>
    [Fact]
    public async Task AuthenticateAsync_MoreAttemptsThanTheSurfaceAllows_RefusesTheSurplusWithoutVerifyingThem()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        const int Allowed = 3;

        // Act
        var results = new List<OwnerPasswordAuthenticationResult>();

        for (var attempt = 0; attempt < Allowed + 2; attempt++)
        {
            results.Add(await harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: Allowed));
        }

        // Assert
        Assert.Equal(Allowed, results.Count(result => result.Rejection == OwnerPasswordRejection.CredentialUnrecognized));
        Assert.Equal(2, results.Count(result => result.Rejection == OwnerPasswordRejection.TooManyAttempts));
        Assert.Equal(Allowed, harness.PasswordHasher.VerificationCount);
    }

    /// <summary>Basic re-presents the credential on every request and this deployment keeps no session, so a bucket a working password spent would bound an owner's request rate rather than anybody's guessing.</summary>
    [Fact]
    public async Task AuthenticateAsync_MoreWorkingSignInsThanTheSurfaceAllowsAttempts_AreEveryOneServed()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        harness.Holds(enabled: true);
        const int Allowed = 2;

        // Act
        var results = await Task.WhenAll(Enumerable
            .Range(0, Allowed + 3)
            .Select(_ => harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: Allowed)));

        // Assert
        Assert.All(results, result => Assert.True(result.Succeeded));
    }

    /// <summary>A wrong password spends the capacity a right one does not, which is what leaves the bound on the guessing intact.</summary>
    [Fact]
    public async Task AuthenticateAsync_AWorkingSignInBeforeTheGuessing_LeavesTheGuessersCapacityWhereItWas()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        harness.Holds(enabled: true);
        const int Allowed = 1;

        // Act
        var accepted = await harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: Allowed);
        var guessed = await harness.AuthenticateAsync(Header("owner", "wrongpassword"), attemptsPerMinute: Allowed);
        var surplus = await harness.AuthenticateAsync(Header("owner", "wrongpassword"), attemptsPerMinute: Allowed);

        // Assert
        Assert.True(accepted.Succeeded);
        Assert.Equal(OwnerPasswordRejection.CredentialUnrecognized, guessed.Rejection);
        Assert.Equal(OwnerPasswordRejection.TooManyAttempts, surplus.Rejection);
    }

    /// <summary>Two surfaces keep separate buckets, so a client that spent one endpoint's attempts has not spent the other's.</summary>
    [Fact]
    public async Task AuthenticateAsync_TheSameCallerOnASecondSurface_IsNotRefusedForTheFirstSurfacesSpentAttempts()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();

        // Act
        var first = await harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: 1);
        var second = await harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: 1);
        var otherSurface = await harness.AuthenticateAsync(
            Header("owner", Password),
            attemptsPerMinute: 1,
            surfaceName: "Mcp");

        // Assert
        Assert.Equal(OwnerPasswordRejection.CredentialUnrecognized, first.Rejection);
        Assert.Equal(OwnerPasswordRejection.TooManyAttempts, second.Rejection);
        Assert.Equal(OwnerPasswordRejection.CredentialUnrecognized, otherSurface.Rejection);
    }

    /// <summary>A raised iteration count is taken up on the one request that has the plaintext to derive from.</summary>
    [Fact]
    public async Task AuthenticateAsync_ARecordBehindTheCurrentPolicy_RewritesItAndStillServesTheRequest()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        harness.Holds(enabled: true);
        harness.PasswordHasher.Result = PasswordVerification.SucceededAndShouldBeRehashed;

        // Act
        var result = await harness.AuthenticateAsync(Header("owner", Password));

        // Assert
        Assert.True(result.Succeeded);

        await harness.Credentials.Received(1).RewritePasswordHashAsync(
            Owner,
            CredentialId,
            RecordingPasswordHasher.DerivedHash,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A rehash is a strengthening the request never asked for, so its failure must not cost a caller who proved their password.</summary>
    [Fact]
    public async Task AuthenticateAsync_ARehashThatFailsToCommit_StillServesTheRequest()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        harness.Holds(enabled: true);
        harness.PasswordHasher.Result = PasswordVerification.SucceededAndShouldBeRehashed;
        harness.Credentials.RewritePasswordHashAsync(
                Arg.Any<MailOwnerId>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the write did not commit"));

        // Act
        var result = await harness.AuthenticateAsync(Header("owner", Password));

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(CredentialId, result.AuthenticatedCredentialId);
    }

    [Fact]
    public async Task AuthenticateAsync_AVerificationThatNeedsNoRehash_LeavesTheStoredRecordAlone()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        harness.Holds(enabled: true);

        // Act
        await harness.AuthenticateAsync(Header("owner", Password));

        // Assert
        await harness.Credentials.DidNotReceiveWithAnyArgs()
            .RewritePasswordHashAsync(default, default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AuthenticateAsync_ASurfaceAllowingNoAttempts_ThrowsBecauseThatWouldRefuseEveryRequestRatherThanBoundOne()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: 0));
    }

    private static string Header(string userId, string password) =>
        $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId}:{password}"))}";

    /// <summary>Counts what the authenticator asked of a hasher, and decides what each comparison answers.</summary>
    /// <remarks>
    /// Hand-written rather than substituted, because the members take the password as a <see cref="ReadOnlySpan{T}" />
    /// and a dynamic proxy cannot carry a by-ref-like argument through its invocation. Counting the comparisons is the
    /// point of the double: what several tests here assert is how much work a refusal cost.
    /// </remarks>
    private sealed class RecordingPasswordHasher : IPasswordHasher
    {
        internal const string DerivedHash = "$mf1$derived$";

        internal const string DecoyHash = "$mf1$decoy$";

        private readonly List<string> verifiedAgainst = [];

        internal PasswordVerification Result { get; set; } = PasswordVerification.Succeeded;

        internal int VerificationCount => this.verifiedAgainst.Count;

        internal IReadOnlyList<string> VerifiedAgainst => this.verifiedAgainst;

        public string HashDecoy() => DecoyHash;

        public string Hash(ReadOnlySpan<char> password) => DerivedHash;

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password)
        {
            this.verifiedAgainst.Add(storedHash);

            return string.Equals(storedHash, StoredHash, StringComparison.Ordinal)
                && password.SequenceEqual(Password)
                    ? this.Result
                    : PasswordVerification.Failed;
        }
    }

    /// <summary>Builds the authenticator over a real attempt limiter and a substituted store.</summary>
    /// <remarks>The limiter is real because what it decides is part of the behaviour under test; the store is the boundary whose interaction is the contract.</remarks>
    private sealed class AuthenticatorHarness : IDisposable
    {
        private readonly PasswordAttemptLimiter attemptLimiter = new();

        internal AuthenticatorHarness()
        {
            this.Credentials = Substitute.For<IOwnerPasswordCredentialStore>();
            this.Credentials.FindByUsernameAsync(Arg.Any<OwnerCredentialUsername>(), Arg.Any<CancellationToken>())
                .Returns((ResolvedOwnerPasswordCredential?)null);

            this.PasswordHasher = new RecordingPasswordHasher();

            this.Authenticator = new OwnerPasswordAuthenticator(
                this.Credentials,
                this.PasswordHasher,
                this.attemptLimiter,
                new DecoyPasswordHash(this.PasswordHasher),
                NullLogger<OwnerPasswordAuthenticator>.Instance);
        }

        internal OwnerPasswordAuthenticator Authenticator { get; }

        internal IOwnerPasswordCredentialStore Credentials { get; }

        internal RecordingPasswordHasher PasswordHasher { get; }

        public void Dispose() => this.attemptLimiter.Dispose();

        internal void Holds(bool enabled) =>
            this.Credentials.FindByUsernameAsync(
                    Arg.Is<OwnerCredentialUsername>(username => username.Value == "owner"),
                    Arg.Any<CancellationToken>())
                .Returns(new ResolvedOwnerPasswordCredential(CredentialId, Owner, enabled, StoredHash));

        internal Task<OwnerPasswordAuthenticationResult> AuthenticateAsync(
            string? authorizationHeaderValue,
            int attemptsPerMinute = AttemptsPerMinute,
            string surfaceName = SurfaceName) =>
            this.Authenticator.AuthenticateAsync(
                surfaceName,
                authorizationHeaderValue,
                source: "203.0.113.7",
                attemptsPerMinute,
                TestContext.Current.CancellationToken);
    }
}
