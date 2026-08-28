// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
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
/// no stored material, and that a request naming an identifier out of the wrong listing is corrected rather than acted
/// on. One route serves four methods, so the cases per method are about what each has to be handed and what each
/// answers with.
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
        var harness = new EndpointHarness(MailFathomPermission.AdminRead);
        harness.Owners.ReadOwnersAsync(OwnerCredentialEndpoints.MaximumListedOwners, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailOwnerRecord>>(
            [
                new MailOwnerRecord(SyntheticMailOwner.Deployment, "owner", DocumentWrittenAtRuntime: false),
                new MailOwnerRecord(SyntheticMailOwner.Another, "second", DocumentWrittenAtRuntime: false),
            ]));

        // Act
        var result = await OwnerCredentialEndpoints.ListOwnersAsync(
            harness.Administration,
            TestContext.Current.CancellationToken);

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
        var harness = new EndpointHarness(MailFathomPermission.AdminRead);

        // Act
        await OwnerCredentialEndpoints.ListOwnersAsync(
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Owners.Received(1).ReadOwnersAsync(
            OwnerCredentialEndpoints.MaximumListedOwners,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The roster is where an administrator learns which owner identifiers exist, so it is admitted by the use case's
    /// own grant rather than by the route's alone — a second entrypoint reaching the port directly would be one that
    /// never asked.
    /// </summary>
    [Fact]
    public async Task ListOwnersAsync_ACallerHoldingNoAdministrativeRead_IsRefusedBeneathTheRoute()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.MailRead);

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => OwnerCredentialEndpoints.ListOwnersAsync(
            harness.Administration,
            TestContext.Current.CancellationToken));

        await harness.Owners.DidNotReceiveWithAnyArgs()
            .ReadOwnersAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>A listing says what exists, how it is presented, and what it grants — never anything the secret produced.</summary>
    [Fact]
    public async Task ListAsync_AnOwnerHoldingACredential_ReportsWhatItIsAndWhatItGrants()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminRead);
        harness.Credentials.ReadForOwnerAsync(SyntheticMailOwner.Deployment, Arg.Any<CancellationToken>())
            .Returns([AHeldCredential(OwnerCredentialMethod.Password, "owner")]);

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
        Assert.Equal(OwnerCredentialMethod.Password.Name, credential.Method);
        Assert.Equal("owner", credential.Lookup);
        Assert.Equal([MailFathomPermission.MailRead.Name], credential.Permissions);
        Assert.True(credential.Enabled);
    }

    /// <summary>
    /// A key's lookup is derived from the key itself, so publishing it would publish something an offline search can
    /// walk back to the credential. A username and a mapping say nothing a listing was not already for.
    /// </summary>
    [Fact]
    public async Task ListAsync_ACredentialWhoseLookupIsDerivedFromItsSecret_WithholdsThatValue()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminRead);
        harness.Credentials.ReadForOwnerAsync(SyntheticMailOwner.Deployment, Arg.Any<CancellationToken>())
            .Returns([AHeldCredential(OwnerCredentialMethod.ApiKey, "a-digest")]);

        // Act
        var result = await OwnerCredentialEndpoints.ListAsync(
            SyntheticMailOwner.Deployment.Value,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var listing = Assert.IsType<Ok<OwnerCredentialListResponse>>(result.Result).Value!;

        Assert.Null(Assert.Single(listing.Credentials).Lookup);
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
        await harness.Credentials.DidNotReceiveWithAnyArgs()
            .ReadForOwnerAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProvisionAsync_AUsernameAndAPasswordTheDeploymentAccepts_ReportsTheIdentifierTheActMinted()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            PasswordRequest("Owner", Password),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var provisioned = Assert.IsType<Ok<OwnerCredentialProvisionedResponse>>(result.Result).Value!;

        Assert.NotEqual(Guid.Empty, provisioned.CredentialId);
        Assert.Equal("owner", provisioned.Lookup);
        Assert.Null(provisioned.Key);
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
            PasswordRequest("  Owner@Example.Test  ", Password),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Credentials.Received(1).CreateAsync(
            Arg.Any<Guid>(),
            SyntheticMailOwner.Deployment,
            OwnerCredentialMethod.Password,
            Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == "owner@example.test"),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<MailFathomPermission>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The key exists in this one answer and nowhere else, which is what the answer has to say by carrying it.</summary>
    [Fact]
    public async Task ProvisionAsync_AnApiKey_AnswersWithTheMintedKeyAndWithholdsTheDigestItIsResolvedBy()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            new OwnerCredentialProvisioningRequest(
                OwnerCredentialMethod.ApiKey.Name,
                Username: null,
                Password: null,
                PublicKey: null,
                Issuer: null,
                Subject: null,
                Permissions: null),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var provisioned = Assert.IsType<Ok<OwnerCredentialProvisionedResponse>>(result.Result).Value!;

        Assert.Equal(StatedApiKeyMinter.Key, provisioned.Key);
        Assert.Null(provisioned.Lookup);
    }

    /// <summary>A client's key is answered with the fingerprint its assertions must name, which nothing else reports.</summary>
    [Fact]
    public async Task ProvisionAsync_AClientPublicKey_AnswersWithTheFingerprintTheClientMustName()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            new OwnerCredentialProvisioningRequest(
                OwnerCredentialMethod.PublicKey.Name,
                Username: null,
                Password: null,
                StatedPublicKeyReader.ReadableKey,
                Issuer: null,
                Subject: null,
                Permissions: null),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var provisioned = Assert.IsType<Ok<OwnerCredentialProvisionedResponse>>(result.Result).Value!;

        Assert.Equal(StatedPublicKeyReader.Fingerprint, provisioned.Lookup);
        Assert.Null(provisioned.Key);
    }

    [Fact]
    public async Task ProvisionAsync_AnOAuthSubject_StoresTheMappingUnderTheIssuerAndSubjectTogether()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            new OwnerCredentialProvisioningRequest(
                OwnerCredentialMethod.OAuthSubject.Name,
                Username: null,
                Password: null,
                PublicKey: null,
                "https://login.example/",
                "subject-1",
                Permissions: null),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var provisioned = Assert.IsType<Ok<OwnerCredentialProvisionedResponse>>(result.Result).Value!;

        Assert.Equal("https://login.example/ subject-1", provisioned.Lookup);
    }

    /// <summary>Each method needs a value only it needs, and a request missing it is corrected rather than half-written.</summary>
    [Theory]
    [InlineData("public-key", null)]
    [InlineData("public-key", "  ")]
    [InlineData("oauth-subject", null)]
    public async Task ProvisionAsync_AMethodMissingWhatOnlyItNeeds_IsRefusedWithoutTouchingTheStore(
        string method,
        string? value)
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            new OwnerCredentialProvisioningRequest(
                method,
                Username: null,
                Password: null,
                value,
                value,
                Subject: null,
                Permissions: null),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);

        await harness.Credentials.DidNotReceiveWithAnyArgs().CreateAsync(
            default,
            default,
            default,
            default,
            default,
            default!,
            TestContext.Current.CancellationToken);
    }

    /// <summary>A method nobody publishes is a typo worth naming the published ones back for.</summary>
    /// <remarks>A published name written in another case is not one of these: the comparison is deliberately case-insensitive, because the value is written by hand in a configuration file and on a command line.</remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("apikey")]
    [InlineData("passwrod")]
    public async Task ProvisionAsync_AMethodThisDeploymentDoesNotPublish_IsRefusedNamingTheOnesItDoes(string? method)
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            new OwnerCredentialProvisioningRequest(
                method,
                "owner",
                Password,
                PublicKey: null,
                Issuer: null,
                Subject: null,
                Permissions: null),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);

        Assert.All(
            OwnerCredentialMethod.All,
            published => Assert.Contains(published.Name, refusal, StringComparison.Ordinal));
    }

    /// <summary>A grant is written as names, so one nobody publishes is named back rather than dropped from the set.</summary>
    [Fact]
    public async Task ProvisionAsync_AGrantNamingSomethingUnpublished_IsRefusedNamingWhatWasWritten()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            PasswordRequest("owner", Password, ["mailfathom.mail.read", "mailfathom.mail.teleport"]),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);

        Assert.Contains("mailfathom.mail.teleport", refusal, StringComparison.Ordinal);
    }

    /// <summary>A credential reaches one owner's mail, so a permission of the administrative surface is refused here too.</summary>
    [Fact]
    public async Task ProvisionAsync_AGrantNamingAnAdministrativePermission_IsRefused()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            PasswordRequest("owner", Password, [MailFathomPermission.AdminErase.Name]),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);

        Assert.Contains(MailFathomPermission.AdminErase.Name, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionAsync_ANarrowedGrant_ReachesTheStoreAsTheParsedPermissions()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            PasswordRequest("owner", Password, [MailFathomPermission.MailRead.Name]),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Credentials.Received(1).CreateAsync(
            Arg.Any<Guid>(),
            Arg.Any<MailOwnerId>(),
            Arg.Any<OwnerCredentialMethod>(),
            Arg.Any<OwnerCredentialLookup>(),
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<MailFathomPermission>>(grant =>
                grant != null && grant.Count == 1 && grant[0] == MailFathomPermission.MailRead),
            Arg.Any<CancellationToken>());
    }

    /// <summary>One value resolves one credential, so the second one to ask for it is told the value is taken.</summary>
    [Fact]
    public async Task ProvisionAsync_AValueAnotherCredentialIsAlreadyResolvedBy_IsAnsweredAsAConflict()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.AnswerCreateWith(OwnerCredentialWriteOutcome.LookupTaken);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            PasswordRequest("owner", Password),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status409Conflict);

        Assert.Contains(OwnerCredentialMethod.Password.Name, refusal, StringComparison.Ordinal);
    }

    /// <summary>The listing an operator revokes from is bounded, so a credential written past that bound would authenticate where nothing lists it — which is why the ceiling is a refusal rather than a row.</summary>
    [Fact]
    public async Task ProvisionAsync_AnOwnerAlreadyHoldingAsManyCredentialsAsOneMay_IsAnsweredAsAConflict()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.AnswerCreateWith(OwnerCredentialWriteOutcome.OwnerAtCredentialCeiling);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            PasswordRequest("owner", Password),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status409Conflict);

        Assert.Contains(
            OwnerCredential.MaximumListedPerOwner.ToString(CultureInfo.InvariantCulture),
            refusal,
            StringComparison.Ordinal);
    }

    /// <summary>An owner identifier copied out of the wrong place is a correction an administrator can act on rather than a write that silently did nothing.</summary>
    [Fact]
    public async Task ProvisionAsync_AnOwnerThisDeploymentHoldsNoRecordFor_IsRefusedNamingTheIdentifier()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.AnswerCreateWith(OwnerCredentialWriteOutcome.UnknownOwner);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Another.Value,
            PasswordRequest("owner", Password),
            harness.Administration,
            harness.PublicKeys,
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
            PasswordRequest("owner", password),
            harness.Administration,
            harness.PublicKeys,
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
            PasswordRequest(username, Password),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        Assert.Contains(OwnerCredentialUsername.DescribeAcceptedForm(), refusal, StringComparison.Ordinal);
    }

    /// <summary>A rotation writes the record the hasher produced and never the plaintext, which is the one assertion the store's own type cannot make.</summary>
    [Fact]
    public async Task ReplaceMaterialAsync_APasswordTheDeploymentAccepts_WritesTheHashRatherThanWhatWasTyped()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ReplaceMaterialAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            new OwnerCredentialMaterialRequest(OwnerCredentialMethod.Password.Name, "owner", Password, PublicKey: null),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<OwnerCredentialRotatedResponse>>(result.Result);

        await harness.Credentials.Received(1).ReplaceMaterialAsync(
            SyntheticMailOwner.Deployment,
            CredentialId,
            OwnerCredentialMethod.Password,
            Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == "owner"),
            Arg.Is<string>(stored => stored != Password),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Rotating a key hands over a new one, which is the whole reason the answer carries a body at all.</summary>
    [Fact]
    public async Task ReplaceMaterialAsync_AnApiKey_AnswersWithTheKeyTheClientMustPresentFromNowOn()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ReplaceMaterialAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            new OwnerCredentialMaterialRequest(
                OwnerCredentialMethod.ApiKey.Name,
                Username: null,
                Password: null,
                PublicKey: null),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var rotated = Assert.IsType<Ok<OwnerCredentialRotatedResponse>>(result.Result).Value!;

        Assert.Equal(StatedApiKeyMinter.Key, rotated.Key);
        Assert.Null(rotated.Lookup);
    }

    /// <summary>A client sending a new key is resolved by that key's fingerprint from then on, so the answer is the fingerprint its assertions must name.</summary>
    [Fact]
    public async Task ReplaceMaterialAsync_AClientPublicKey_AnswersWithTheFingerprintTheClientMustNameFromNowOn()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ReplaceMaterialAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            new OwnerCredentialMaterialRequest(
                OwnerCredentialMethod.PublicKey.Name,
                Username: null,
                Password: null,
                StatedPublicKeyReader.ReadableKey),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var rotated = Assert.IsType<Ok<OwnerCredentialRotatedResponse>>(result.Result).Value!;

        Assert.Equal(StatedPublicKeyReader.Fingerprint, rotated.Lookup);
        Assert.Null(rotated.Key);

        await harness.Credentials.Received(1).ReplaceMaterialAsync(
            SyntheticMailOwner.Deployment,
            CredentialId,
            OwnerCredentialMethod.PublicKey,
            Arg.Is<OwnerCredentialLookup>(lookup => lookup.Value == StatedPublicKeyReader.Fingerprint),
            StatedPublicKeyReader.ReadableKey,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The boundary reads the key rather than letting the use case raise on it. Nothing in this process maps that
    /// exception to a response, so an operator pasting a private key or a truncated block would be answered with a
    /// <c>500</c> instead of the sentence naming what a key may be — and a rotation nobody could correct from the
    /// answer.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----pasted-----END RSA PRIVATE KEY-----")]
    public async Task ReplaceMaterialAsync_APublicKeyThisDeploymentCannotRead_IsRefusedWithoutReachingTheStore(
        string? written)
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ReplaceMaterialAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            new OwnerCredentialMaterialRequest(
                OwnerCredentialMethod.PublicKey.Name,
                Username: null,
                Password: null,
                written),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<ProblemHttpResult>(result.Result);

        await harness.Credentials.DidNotReceive().ReplaceMaterialAsync(
            Arg.Any<MailOwnerId>(),
            Arg.Any<Guid>(),
            Arg.Any<OwnerCredentialMethod>(),
            Arg.Any<OwnerCredentialLookup>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Provisioning reads the key by the same rule, so the same mistake is answered rather than raised there too.</summary>
    [Fact]
    public async Task ProvisionAsync_APublicKeyThisDeploymentCannotRead_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ProvisionAsync(
            SyntheticMailOwner.Deployment.Value,
            new OwnerCredentialProvisioningRequest(
                OwnerCredentialMethod.PublicKey.Name,
                Username: null,
                Password: null,
                "-----BEGIN PUBLIC KEY-----truncated",
                Issuer: null,
                Subject: null,
                Permissions: null),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<ProblemHttpResult>(result.Result);

        await harness.Credentials.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(),
            Arg.Any<MailOwnerId>(),
            Arg.Any<OwnerCredentialMethod>(),
            Arg.Any<OwnerCredentialLookup>(),
            Arg.Any<string?>(),
            Arg.Any<IReadOnlyList<MailFathomPermission>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A mapped subject is not this deployment's to reissue, so it says so rather than writing half of one.</summary>
    [Fact]
    public async Task ReplaceMaterialAsync_AMethodPresentingWhatThisDeploymentDidNotIssue_IsRefusedWithoutWriting()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ReplaceMaterialAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            new OwnerCredentialMaterialRequest(
                OwnerCredentialMethod.OAuthSubject.Name,
                Username: null,
                Password: null,
                PublicKey: null),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);

        Assert.Contains(OwnerCredentialMethod.OAuthSubject.Name, refusal, StringComparison.Ordinal);

        await harness.Credentials.DidNotReceiveWithAnyArgs().ReplaceMaterialAsync(
            default,
            default,
            default,
            default,
            default,
            TestContext.Current.CancellationToken);
    }

    /// <summary>An identifier out of the wrong owner's listing rotates nothing, and the answer says which pair the deployment could not find.</summary>
    [Fact]
    public async Task ReplaceMaterialAsync_ACredentialTheOwnerDoesNotHold_IsRefusedNamingBothIdentifiers()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);
        harness.AnswerReplaceWith(OwnerCredentialWriteOutcome.UnknownCredential);

        // Act
        var result = await OwnerCredentialEndpoints.ReplaceMaterialAsync(
            SyntheticMailOwner.Deployment.Value,
            CredentialId,
            new OwnerCredentialMaterialRequest(OwnerCredentialMethod.Password.Name, "owner", Password, PublicKey: null),
            harness.Administration,
            harness.PublicKeys,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        Assert.Contains(SyntheticMailOwner.Deployment.Value.ToString(), refusal, StringComparison.Ordinal);
        Assert.Contains(CredentialId.ToString(), refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceMaterialAsync_ARequestNamingNoCredential_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var harness = new EndpointHarness(MailFathomPermission.AdminCredentialsWrite);

        // Act
        var result = await OwnerCredentialEndpoints.ReplaceMaterialAsync(
            SyntheticMailOwner.Deployment.Value,
            Guid.Empty,
            new OwnerCredentialMaterialRequest(OwnerCredentialMethod.Password.Name, "owner", Password, PublicKey: null),
            harness.Administration,
            harness.PublicKeys,
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
        await harness.Credentials.DidNotReceiveWithAnyArgs()
            .SetEnabledAsync(default, default, default, TestContext.Current.CancellationToken);
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

    private static OwnerCredentialProvisioningRequest PasswordRequest(
        string? username,
        string? password,
        IReadOnlyList<string>? permissions = null) => new(
        OwnerCredentialMethod.Password.Name,
        username,
        password,
        PublicKey: null,
        Issuer: null,
        Subject: null,
        permissions);

    private static OwnerCredential AHeldCredential(OwnerCredentialMethod method, string lookup) => new(
        CredentialId,
        SyntheticMailOwner.Deployment,
        method,
        OwnerCredentialLookup.ForDigest(lookup),
        [MailFathomPermission.MailRead],
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

        public string HashDecoy() => StoredHash;

        public string Hash(ReadOnlySpan<char> password)
        {
            this.HashCount++;

            return StoredHash;
        }

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password) =>
            PasswordVerification.Failed;
    }

    /// <summary>Mints one stated key, so a test can assert which value reached the answer and which reached the row.</summary>
    /// <remarks>Hand-written rather than the real minter, which is internal to the infrastructure boundary, and rather than a substitute, whose proxy cannot carry the span the digest is read from.</remarks>
    private sealed class StatedApiKeyMinter : IOwnerApiKeyMinter
    {
        internal const string Key = "mfk_stated-key";

        private const string Digest = "stated-digest";

        public MintedOwnerApiKey Mint() => new(Key, OwnerCredentialLookup.ForDigest(Digest));

        public bool TryDigest(ReadOnlySpan<char> presentedKey, out OwnerCredentialLookup lookup)
        {
            lookup = presentedKey.SequenceEqual(Key) ? OwnerCredentialLookup.ForDigest(Digest) : default;

            return lookup.IsSpecified;
        }
    }

    /// <summary>Reads one stated key and refuses everything else, so both branches are reachable from a route test.</summary>
    private sealed class StatedPublicKeyReader : IClientPublicKeyReader
    {
        internal const string ReadableKey = "-----BEGIN PUBLIC KEY-----readable-----END PUBLIC KEY-----";

        internal const string Fingerprint = "stated-fingerprint";

        public bool TryRead(string? written, out ClientPublicKey? publicKey)
        {
            publicKey = written == ReadableKey
                ? new ClientPublicKey(written, OwnerCredentialLookup.ForDigest(Fingerprint))
                : null;

            return publicKey is not null;
        }

        public string DescribeAcceptedForm() => "A client public key is a PEM 'PUBLIC KEY' block.";
    }

    /// <summary>Builds the use case the routes are handed, over a store and an auditor a test can read.</summary>
    private sealed class EndpointHarness
    {
        internal EndpointHarness(MailFathomPermission granted)
        {
            var principals = Substitute.For<IAuthorizedPrincipalSource>();
            // A caller acting for nobody's mail, which is the only shape the administrative surface produces: the owner
            // every act here names comes from the request rather than from whoever was admitted.
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

            this.Owners = Substitute.For<IMailOwnerDirectory>();
            this.Owners.ReadOwnersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

            this.Administration = new OwnerCredentialAdministration(
                new AccessAuthorization(principals),
                this.Owners,
                this.Credentials,
                this.PasswordHasher,
                new StatedApiKeyMinter(),
                new StatedPublicKeyReader(),
                Substitute.For<IOwnerCredentialAuditor>(),
                new FakeTimeProvider(Moment));
        }

        internal OwnerCredentialAdministration Administration { get; }

        internal IClientPublicKeyReader PublicKeys { get; } = new StatedPublicKeyReader();

        internal IMailOwnerDirectory Owners { get; }

        internal IOwnerCredentialStore Credentials { get; }

        internal RecordingPasswordHasher PasswordHasher { get; }

        internal void AnswerCreateWith(OwnerCredentialWriteOutcome outcome) => this.Credentials.CreateAsync(
                Arg.Any<Guid>(),
                Arg.Any<MailOwnerId>(),
                Arg.Any<OwnerCredentialMethod>(),
                Arg.Any<OwnerCredentialLookup>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<MailFathomPermission>>(),
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
