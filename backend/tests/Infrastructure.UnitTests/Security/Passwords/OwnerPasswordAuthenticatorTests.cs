// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Security.Passwords;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
        Assert.NotNull(result.Admitted);
        Assert.Equal(CredentialId, result.Admitted.CredentialId);
        Assert.Equal(Owner, result.Admitted.Owner);
        Assert.Equal(AuthenticatorHarness.Grant, result.Admitted.Permissions);
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

        await harness.Credentials.Received(1).FindAsync(
            OwnerCredentialMethod.Password,
            Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == "owner"),
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

        await harness.Credentials.DidNotReceiveWithAnyArgs()
            .FindAsync(default, default, TestContext.Current.CancellationToken);
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

    /// <summary>
    /// The guessing allowance must never cap an owner's own parallelism. Basic re-presents the credential on every
    /// request and this deployment keeps no session, so an owner whose client has more calls in flight than the surface
    /// allows guesses is the ordinary case rather than an unusual one, and every one of them carries a right password.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_MoreWorkingSignInsInFlightAtOnceThanTheSurfaceAllowsAttempts_AreEveryOneServed()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        var reads = harness.HoldsUntilReleased(enabled: true);
        const int Allowed = 1;
        const int Concurrent = 12;

        // Act
        var inFlight = Enumerable
            .Range(0, Concurrent)
            .Select(_ => harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: Allowed))
            .ToArray();

        await reads.WaitUntilWaitingAsync(Concurrent);
        reads.Release();

        var results = await Task.WhenAll(inFlight);

        // Assert
        Assert.All(results, static result => Assert.True(result.Succeeded));
    }

    /// <summary>
    /// What a burst is bounded by is the derivations it may have in flight, which is what stops a caller opening
    /// hundreds of connections from making this process derive hundreds of times at once — a separate bound from the
    /// allowance, and a much larger one, because the allowance is about guessing and this is about cost.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_MoreGuessesInFlightThanOneAxisAdmitsDerivations_RefusesTheSurplusWithoutVerifyingIt()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        var reads = harness.HoldsUntilReleased(enabled: true);
        const int Surplus = 3;
        var admitted = PasswordAttemptLimiter.ConcurrentVerificationsPerPartition;

        // Act
        var inFlight = Enumerable
            .Range(0, admitted)
            .Select(_ => harness.AuthenticateAsync(Header("owner", "wrongpassword")))
            .ToArray();

        await reads.WaitUntilWaitingAsync(admitted);

        var surplus = await Task.WhenAll(Enumerable
            .Range(0, Surplus)
            .Select(_ => harness.AuthenticateAsync(Header("owner", "wrongpassword"))));

        reads.Release();
        await Task.WhenAll(inFlight);

        // Assert
        Assert.All(surplus, static result =>
            Assert.Equal(OwnerPasswordRejection.TooManyAttempts, result.Rejection));
        Assert.Equal(admitted, harness.PasswordHasher.VerificationCount);
    }

    /// <summary>A wrong password holds its capacity for the minute the allowance is stated over, and gives it back then rather than never.</summary>
    [Fact]
    public async Task AuthenticateAsync_AGuessAndThenTheWindow_AdmitsAnAttemptAgain()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        const int Allowed = 1;

        // Act
        var spent = await harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: Allowed);
        var whileHeld = await harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: Allowed);
        harness.Clock.Advance(PasswordAttemptLimiter.SpentAttemptWindow);
        var afterTheWindow = await harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: Allowed);

        // Assert
        Assert.Equal(OwnerPasswordRejection.CredentialUnrecognized, spent.Rejection);
        Assert.Equal(OwnerPasswordRejection.TooManyAttempts, whileHeld.Rejection);
        Assert.Equal(OwnerPasswordRejection.CredentialUnrecognized, afterTheWindow.Rejection);
    }

    /// <summary>An address every caller shares is not a source, so where the caller supplies none the username is the whole bound and one guesser closes nobody else's sign-in.</summary>
    [Fact]
    public async Task AuthenticateAsync_GuessesAtTwoUsernamesWithNoSourceToTellCallersApart_BoundsEachUsernameOnItsOwn()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        const int Allowed = 1;

        // Act
        var firstOwner = await harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: Allowed, source: null);
        var secondOwner = await harness.AuthenticateAsync(Header("other", Password), attemptsPerMinute: Allowed, source: null);
        var firstOwnerAgain = await harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: Allowed, source: null);

        // Assert
        Assert.Equal(OwnerPasswordRejection.CredentialUnrecognized, firstOwner.Rejection);
        Assert.Equal(OwnerPasswordRejection.CredentialUnrecognized, secondOwner.Rejection);
        Assert.Equal(OwnerPasswordRejection.TooManyAttempts, firstOwnerAgain.Rejection);
    }

    /// <summary>Where an address does tell callers apart it is a bound of its own, which is what catches one host spreading its guesses across many usernames.</summary>
    [Fact]
    public async Task AuthenticateAsync_GuessesAtTwoUsernamesFromOneSource_SpendsThatSourcesAllowance()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        const int Allowed = 1;

        // Act
        var firstOwner = await harness.AuthenticateAsync(Header("owner", Password), attemptsPerMinute: Allowed);
        var secondOwner = await harness.AuthenticateAsync(Header("other", Password), attemptsPerMinute: Allowed);

        // Assert
        Assert.Equal(OwnerPasswordRejection.CredentialUnrecognized, firstOwner.Rejection);
        Assert.Equal(OwnerPasswordRejection.TooManyAttempts, secondOwner.Rejection);
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

        await harness.Credentials.Received(1).RewriteMaterialAsync(
            Owner,
            CredentialId,
            StoredHash,
            RecordingPasswordHasher.DerivedHash,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A rotation can commit inside the two derivations a rehash spends, which is the case rotation exists for. The
    /// write therefore names the record it verified against, so what an administrator replaced is not put back by a
    /// request that read the old one.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_ARehashOfACredentialSomebodyRotatedMeanwhile_WritesOverTheVerifiedRecordOnly()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        harness.Holds(enabled: true);
        harness.PasswordHasher.Result = PasswordVerification.SucceededAndShouldBeRehashed;

        // Act
        await harness.AuthenticateAsync(Header("owner", Password));

        // Assert
        await harness.Credentials.DidNotReceive().RewriteMaterialAsync(
            Arg.Any<MailOwnerId>(),
            Arg.Any<Guid>(),
            Arg.Is<string>(static verified => verified != StoredHash),
            Arg.Any<string>(),
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
        harness.Credentials.RewriteMaterialAsync(
                Arg.Any<MailOwnerId>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the write did not commit"));

        // Act
        var result = await harness.AuthenticateAsync(Header("owner", Password));

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(CredentialId, result.Admitted?.CredentialId);
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
            .RewriteMaterialAsync(default, default, default!, default!, TestContext.Current.CancellationToken);
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

    /// <summary>
    /// A per-partition ceiling bounds a partition rather than this process: every distinct username is a fresh
    /// partition with a fresh one, and behind a declared proxy the username is the only axis there is. A caller
    /// varying the name would otherwise have this process derive once per connection it cared to open, each
    /// derivation deliberately expensive, which is what the ceiling the whole surface shares refuses.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_MoreGuessesInFlightUnderDistinctUsernamesThanTheSurfaceAdmits_RefusesTheSurplus()
    {
        // Arrange
        using var harness = new AuthenticatorHarness();
        var reads = harness.HoldsNothingUntilReleased();
        const int Surplus = 3;
        var admitted = PasswordAttemptLimiter.ConcurrentVerificationsPerSurface;

        // Act
        var inFlight = Enumerable
            .Range(0, admitted)
            .Select(ordinal => harness.AuthenticateAsync(Header($"stranger-{ordinal}", "wrongpassword"), source: null))
            .ToArray();

        await reads.WaitUntilWaitingAsync(admitted);

        var surplus = await Task.WhenAll(Enumerable
            .Range(admitted, Surplus)
            .Select(ordinal => harness.AuthenticateAsync(Header($"stranger-{ordinal}", "wrongpassword"), source: null)));

        reads.Release();
        await Task.WhenAll(inFlight);

        // Assert
        Assert.All(surplus, static result =>
            Assert.Equal(OwnerPasswordRejection.TooManyAttempts, result.Rejection));
        Assert.Equal(admitted, harness.PasswordHasher.VerificationCount);
    }

    private static string Header(string userId, string password) =>
        $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId}:{password}"))}";

    /// <summary>Counts what the authenticator asked of a hasher, and decides what each comparison answers.</summary>
    /// <remarks>
    /// Hand-written rather than substituted, because the members take the password as a <see cref="ReadOnlySpan{T}" />
    /// and a dynamic proxy cannot carry a by-ref-like argument through its invocation. Counting the comparisons is the
    /// point of the double: what several tests here assert is how much work a refusal cost.
    /// </remarks>
    /// <summary>Holds every credential read open until it is released, and says how many are waiting.</summary>
    /// <remarks>Nothing here is a clock: the waiting is what makes the calls overlap, and the test releases them itself rather than timing anything.</remarks>
    private sealed class CredentialReadGate
    {
        private readonly TaskCompletionSource released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int waiting;

        /// <summary>Gets how many credential reads are waiting on this gate right now.</summary>
        internal int Waiting => Volatile.Read(ref this.waiting);

        /// <summary>Lets every waiting read, and every later one, answer.</summary>
        internal void Release() => this.released.TrySetResult();

        /// <summary>Waits until at least <paramref name="count" /> reads are in flight.</summary>
        /// <remarks>The test's own cancellation token is what ends the wait if they never are, so a broken arrangement fails rather than hanging.</remarks>
        internal async Task WaitUntilWaitingAsync(int count)
        {
            while (this.Waiting < count)
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();

                await Task.Yield();
            }
        }

        internal async Task<ResolvedOwnerCredential?> AnswerAsync(ResolvedOwnerCredential? credential)
        {
            Interlocked.Increment(ref this.waiting);

            try
            {
                await this.released.Task;
            }
            finally
            {
                Interlocked.Decrement(ref this.waiting);
            }

            return credential;
        }
    }

    private sealed class RecordingPasswordHasher : IPasswordHasher
    {
        internal const string DerivedHash = "$mf1$derived$";

        internal const string DecoyHash = "$mf1$decoy$";

        private readonly Lock recording = new();
        private readonly List<string> verifiedAgainst = [];

        internal PasswordVerification Result { get; set; } = PasswordVerification.Succeeded;

        internal int VerificationCount
        {
            get
            {
                lock (this.recording)
                {
                    return this.verifiedAgainst.Count;
                }
            }
        }

        internal IReadOnlyList<string> VerifiedAgainst
        {
            get
            {
                lock (this.recording)
                {
                    return [.. this.verifiedAgainst];
                }
            }
        }

        public string HashDecoy() => DecoyHash;

        public string Hash(ReadOnlySpan<char> password) => DerivedHash;

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password)
        {
            lock (this.recording)
            {
                this.verifiedAgainst.Add(storedHash);
            }

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
        private readonly PasswordAttemptLimiter attemptLimiter;

        internal AuthenticatorHarness()
        {
            this.Clock = new FakeTimeProvider();
            this.attemptLimiter = new PasswordAttemptLimiter(this.Clock);

            this.Credentials = Substitute.For<IOwnerCredentialStore>();
            this.Credentials.FindAsync(
                    Arg.Any<OwnerCredentialMethod>(),
                    Arg.Any<OwnerCredentialLookup>(),
                    Arg.Any<CancellationToken>())
                .Returns((ResolvedOwnerCredential?)null);

            this.PasswordHasher = new RecordingPasswordHasher();

            this.Authenticator = new OwnerPasswordAuthenticator(
                this.Credentials,
                this.PasswordHasher,
                this.attemptLimiter,
                new DecoyPasswordHash(this.PasswordHasher),
                NullLogger<OwnerPasswordAuthenticator>.Instance);
        }

        /// <summary>Gets the grant the stored credential carries, which an admitted request is answered with.</summary>
        internal static IReadOnlyList<MailFathomPermission> Grant { get; } =
            [MailFathomPermission.MailRead, MailFathomPermission.MailSend];

        internal OwnerPasswordAuthenticator Authenticator { get; }

        internal IOwnerCredentialStore Credentials { get; }

        internal RecordingPasswordHasher PasswordHasher { get; }

        internal FakeTimeProvider Clock { get; }

        public void Dispose() => this.attemptLimiter.Dispose();

        internal void Holds(bool enabled) =>
            this.Credentials.FindAsync(
                    OwnerCredentialMethod.Password,
                    Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == "owner"),
                    Arg.Any<CancellationToken>())
                .Returns(Credential(enabled));

        /// <summary>Holds every credential read open until the gate is released, so callers are genuinely in flight together.</summary>
        /// <param name="enabled">Whether the credential the store then answers with still authenticates, as <see cref="Holds" /> decides it.</param>
        /// <returns>The gate, which reports how many reads are waiting and releases them all.</returns>
        /// <remarks>
        /// Without it nothing on this path yields — the substitute answers from an already-completed task and the hasher
        /// is synchronous — so a set of calls started together would run one after another and a claim about concurrency
        /// would be proved by nothing.
        /// </remarks>
        internal CredentialReadGate HoldsUntilReleased(bool enabled)
        {
            var gate = new CredentialReadGate();

            this.Credentials.FindAsync(
                    OwnerCredentialMethod.Password,
                    Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == "owner"),
                    Arg.Any<CancellationToken>())
                .Returns(_ => gate.AnswerAsync(Credential(enabled)));

            return gate;
        }

        /// <summary>Holds every credential read open and answers that nobody holds the username, whichever one was presented.</summary>
        /// <returns>The gate, which reports how many reads are waiting and releases them all.</returns>
        /// <remarks>An unknown username still costs a derivation, against the decoy record, which is what makes varying the name an attack on this process rather than a way of being refused cheaply.</remarks>
        internal CredentialReadGate HoldsNothingUntilReleased()
        {
            var gate = new CredentialReadGate();

            this.Credentials.FindAsync(
                    Arg.Any<OwnerCredentialMethod>(),
                    Arg.Any<OwnerCredentialLookup>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => gate.AnswerAsync(null));

            return gate;
        }

        private static ResolvedOwnerCredential Credential(bool enabled) => new(
            CredentialId,
            Owner,
            OwnerCredentialMethod.Password,
            Grant,
            enabled,
            StoredHash);

        internal Task<OwnerPasswordAuthenticationResult> AuthenticateAsync(
            string? authorizationHeaderValue,
            int attemptsPerMinute = AttemptsPerMinute,
            string surfaceName = SurfaceName,
            string? source = "203.0.113.7") =>
            this.Authenticator.AuthenticateAsync(
                surfaceName,
                authorizationHeaderValue,
                source,
                attemptsPerMinute,
                TestContext.Current.CancellationToken);
    }
}
