// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the credential routes answer, and above all what no answer and no refusal ever carries.</summary>
/// <remarks>
/// The boundary is the last place in this process where a password exists as a string, so the cases that matter most
/// are the ones nothing else can state: that a refusal describes the rule rather than the value, that an answer carries
/// no hash, and that a request naming an identifier out of the wrong listing is corrected rather than acted on.
/// </remarks>
public sealed class OwnerCredentialEndpointsTests
{
    private const string AdministratorIdentity = "operations";

    private const string Password = "correcthorsebatterystaple";

    private static readonly DateTimeOffset Moment = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CredentialId = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task ListOwnersAsync_ADeploymentHoldingSeveralOwners_ReportsEachIdentifier()
    {
        // Arrange
        var owners = Substitute.For<IMailOwnerDirectory>();
        owners.ReadOwnersAsync(OwnerCredentialEndpoints.MaximumListedOwners, Arg.Any<CancellationToken>())
            .Returns([SyntheticMailOwner.Deployment, SyntheticMailOwner.Another]);

        // Act
        var result = await OwnerCredentialEndpoints.ListOwnersAsync(owners, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [SyntheticMailOwner.Deployment.Value, SyntheticMailOwner.Another.Value],
            result.Value!.Owners);
    }

    /// <summary>The bound is the route's rather than the store's, so a roster nobody bounded cannot arrive through this answer.</summary>
    [Fact]
    public async Task ListOwnersAsync_Always_ReadsNoMoreOwnersThanTheRouteBounds()
    {
        // Arrange
        var owners = Substitute.For<IMailOwnerDirectory>();
        owners.ReadOwnersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        // Act
        await OwnerCredentialEndpoints.ListOwnersAsync(owners, TestContext.Current.CancellationToken);

        // Assert
        await owners.Received(1).ReadOwnersAsync(
            OwnerCredentialEndpoints.MaximumListedOwners,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A listing says what exists and whose it is; a record of what the password was is not part of that.</summary>
    [Fact]
    public async Task ListAsync_AnOwnerHoldingACredential_ReportsItWithoutAnythingDerivedFromThePassword()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminRead);
        harness.Credentials.ReadForOwnerAsync(SyntheticMailOwner.Deployment, Arg.Any<CancellationToken>())
            .Returns([AHeldCredential()]);

        // Act
        var result = await OwnerCredentialEndpoints.ListAsync(
            SyntheticMailOwner.Deployment.Value,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var listing = Assert.IsType<Ok<OwnerCredentialListResponse>>(result.Result).Value!;
        var credential = Assert.Single(listing.Credentials);

        Assert.Equal(SyntheticMailOwner.Deployment.Value, listing.Owner);
        Assert.Equal(CredentialId, credential.Id);
        Assert.Equal("owner", credential.Username);
        Assert.True(credential.Enabled);
    }

    [Fact]
    public async Task ListAsync_ARequestNamingNoOwner_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminRead);

        // Act
        var result = await OwnerCredentialEndpoints.ListAsync(
            Guid.Empty,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        await harness.Credentials.DidNotReceiveWithAnyArgs().ReadForOwnerAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProvisionAsync_AUsernameAndAPasswordTheDeploymentAccepts_ReportsTheIdentifierTheActMinted()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            new OwnerCredentialProvisioningRequest("Owner", Password),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var provisioned = Assert.IsType<Ok<OwnerCredentialProvisionedResponse>>(result.Result).Value!;

        Assert.NotEqual(Guid.Empty, provisioned.CredentialId);
    }

    /// <summary>The username is folded before it is stored, so two spellings of one name cannot become two credentials.</summary>
    [Fact]
    public async Task ProvisionAsync_AUsernameSpelledWithCapitals_StoresItInItsCanonicalForm()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            new OwnerCredentialProvisioningRequest("  Owner@Example.Test  ", Password),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Credentials.Received(1).CreateAsync(
            Arg.Any<Guid>(),
            SyntheticMailOwner.Deployment,
            Arg.Is<OwnerCredentialUsername>(username => username.Value == "owner@example.test"),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A username names one credential across the deployment, so the second one to ask for it is told the name is taken rather than told nothing happened.</summary>
    [Fact]
    public async Task ProvisionAsync_AUsernameAnotherCredentialAlreadyHolds_IsAnsweredAsAConflict()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.Credentials.CreateAsync(
                Arg.Any<Guid>(),
                Arg.Any<MailOwnerId>(),
                Arg.Any<OwnerCredentialUsername>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(OwnerCredentialWriteOutcome.UsernameTaken);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            new OwnerCredentialProvisioningRequest("owner", Password),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status409Conflict);
    }

    /// <summary>An owner identifier copied out of the wrong place is a correction an administrator can act on rather than a write that silently did nothing.</summary>
    [Fact]
    public async Task ProvisionAsync_AnOwnerThisDeploymentHoldsNoRecordFor_IsRefusedNamingTheIdentifier()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.Credentials.CreateAsync(
                Arg.Any<Guid>(),
                Arg.Any<MailOwnerId>(),
                Arg.Any<OwnerCredentialUsername>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(OwnerCredentialWriteOutcome.UnknownOwner);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Another.Value,
            new OwnerCredentialProvisioningRequest("owner", Password),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        Assert.Contains(SyntheticMailOwner.Another.Value.ToString(), refusal, StringComparison.Ordinal);
    }

    /// <summary>A refusal reaches a terminal, a log, and a script's output, so it says which rule was broken and repeats nothing that was typed.</summary>
    [Theory]
    [InlineData("short")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ProvisionAsync_APasswordThePolicyRefuses_IsRefusedWithoutRepeatingIt(string? password)
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            new OwnerCredentialProvisioningRequest("owner", password),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);

        if (!string.IsNullOrEmpty(password))
        {
            Assert.DoesNotContain(password, refusal, StringComparison.Ordinal);
        }

        Assert.Equal(0, harness.PasswordHasher.HashCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("owner:name")]
    [InlineData("Zażółć")]
    public async Task ProvisionAsync_AUsernameOutsideTheAcceptedForm_IsRefusedNamingWhatIsAccepted(string? username)
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            new OwnerCredentialProvisioningRequest(username, Password),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        Assert.Contains(OwnerCredentialUsername.DescribeAcceptedForm(), refusal, StringComparison.Ordinal);
    }

    /// <summary>Nothing is reported that the caller did not send, because a response echoing any part of it would read a password back out of the service.</summary>
    [Fact]
    public async Task RotatePasswordAsync_APasswordTheDeploymentAccepts_IsAnsweredWithNoBody()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.RotatePasswordAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            new OwnerCredentialPasswordRequest(Password),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
        await harness.Credentials.Received(1).ReplacePasswordAsync(
            SyntheticMailOwner.Deployment,
            CredentialId,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A rotation writes the record the hasher produced and never the plaintext, which is the one assertion the store's own type cannot make.</summary>
    [Fact]
    public async Task RotatePasswordAsync_APasswordTheDeploymentAccepts_WritesTheHashRatherThanWhatWasTyped()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await OwnerCredentialEndpoints.RotatePasswordAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            new OwnerCredentialPasswordRequest(Password),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Credentials.Received(1).ReplacePasswordAsync(
            Arg.Any<MailOwnerId>(),
            Arg.Any<Guid>(),
            Arg.Is<string>(stored => stored != Password),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An identifier out of the wrong owner's listing rotates nothing, and the answer says which pair the deployment could not find.</summary>
    [Fact]
    public async Task RotatePasswordAsync_ACredentialTheOwnerDoesNotHold_IsRefusedNamingBothIdentifiers()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.Credentials.ReplacePasswordAsync(
                Arg.Any<MailOwnerId>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(OwnerCredentialWriteOutcome.UnknownCredential);

        // Act
        var result = await OwnerCredentialEndpoints.RotatePasswordAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            new OwnerCredentialPasswordRequest(Password),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        Assert.Contains(SyntheticMailOwner.Deployment.Value.ToString(), refusal, StringComparison.Ordinal);
        Assert.Contains(CredentialId.ToString(), refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RotatePasswordAsync_ARequestNamingNoCredential_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.RotatePasswordAsync(
            SyntheticMailOwner.Deployment.Value,
            Guid.Empty,
            new OwnerCredentialPasswordRequest(Password),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        Assert.Equal(0, harness.PasswordHasher.HashCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetEnabledAsync_EitherDecision_WritesWhatTheRequestAskedFor(bool enabled)
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.SetEnabledAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            new OwnerCredentialEnablementRequest(enabled),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
        await harness.Credentials.Received(1).SetEnabledAsync(
            SyntheticMailOwner.Deployment,
            CredentialId,
            enabled,
            Arg.Any<CancellationToken>());
    }

    /// <summary>An absent decision is not a decision to disable, so a body that stated neither is refused rather than acted on.</summary>
    [Fact]
    public async Task SetEnabledAsync_ARequestStatingNeitherDecision_IsRefusedWithoutWritingEither()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.SetEnabledAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            new OwnerCredentialEnablementRequest(null),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        await harness.Credentials.DidNotReceiveWithAnyArgs().SetEnabledAsync(default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeleteAsync_ACredentialTheOwnerHolds_IsAnsweredWithNoBody()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.DeleteAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
        await harness.Credentials.Received(1).DeleteAsync(
            SyntheticMailOwner.Deployment,
            CredentialId,
            Arg.Any<CancellationToken>());
    }

    /// <summary>Reading who holds a credential and deciding who may read somebody's mail are separately granted, so the reading grant reaches none of the writes.</summary>
    [Fact]
    public async Task EveryWrite_ACallerHoldingOnlyTheReadingGrant_IsRefusedByTheUseCase()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminRead);

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => OwnerCredentialEndpoints.DeleteAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            harness.Administration,
            TestContext.Current.CancellationToken));
    }

    private static OwnerPasswordCredential AHeldCredential() => new(
        CredentialId,
        SyntheticMailOwner.Deployment,
        OwnerCredentialUsername.Create("owner"),
        Enabled: true,
        Version: 1,
        Moment,
        Moment);

    private static string AssertRefusal(IResult result, int statusCode)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);

        Assert.Equal(statusCode, problem.StatusCode);
        Assert.NotNull(problem.ProblemDetails.Detail);

        return problem.ProblemDetails.Detail;
    }

    /// <summary>Counts what the route asked of a hasher, and answers with a fixed stored representation.</summary>
    /// <remarks>Hand-written rather than substituted, because the members take the password as a <see cref="ReadOnlySpan{T}" /> and a dynamic proxy cannot carry a by-ref-like argument through its invocation.</remarks>
    private sealed class RecordingPasswordHasher : IPasswordHasher
    {
        internal const string StoredHash = "$mf1$stored$";

        internal int HashCount { get; private set; }

        public string Hash(ReadOnlySpan<char> password)
        {
            this.HashCount++;

            return StoredHash;
        }

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password) =>
            PasswordVerification.Failed;
    }

    /// <summary>Builds the use case the routes are handed, over a store and an auditor a test can read.</summary>
    private sealed class EndpointHarness
    {
        internal EndpointHarness(MailFathomPermission granted)
        {
            var principals = Substitute.For<IAuthorizedPrincipalSource>();
            principals.Current.Returns(AuthorizedPrincipal.CallerActingFor(
                SyntheticMailOwner.Deployment,
                AdministratorIdentity,
                [granted]));

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

            this.Administration = new OwnerPasswordCredentialAdministration(
                new AccessAuthorization(principals),
                this.Credentials,
                this.PasswordHasher,
                Substitute.For<IOwnerCredentialAuditor>(),
                new FakeTimeProvider(Moment));
        }

        internal OwnerPasswordCredentialAdministration Administration { get; }

        internal IOwnerPasswordCredentialStore Credentials { get; }

        internal RecordingPasswordHasher PasswordHasher { get; }
    }
}
