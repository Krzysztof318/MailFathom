// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Host.Security;
using Microsoft.IdentityModel.Protocols;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers finding one authorization server's discovery document across the addresses the MCP specification names.</summary>
/// <remarks>
/// What is worth stating here is which failures mean "not at this address" and are followed by the next candidate, and
/// which mean the caller went away. A server publishing at the third address must still be found when the first two
/// answer with a 404, with something that is not a document, or not at all.
/// </remarks>
public sealed class OAuthAuthorizationServerMetadataRetrieverTests
{
    private const string ProfileName = "workforce";

    private const string Issuer = "https://sso.example.test/realms/mailfathom";

    private static readonly string[] CandidateAddresses =
    [
        "https://sso.example.test/.well-known/oauth-authorization-server/realms/mailfathom",
        "https://sso.example.test/.well-known/openid-configuration/realms/mailfathom",
        "https://sso.example.test/realms/mailfathom/.well-known/openid-configuration",
    ];

    /// <summary>
    /// A candidate that hangs until the client's own timeout raises a cancellation nobody asked for. Reading it as the
    /// caller giving up would end discovery at the first unresponsive address, so the server that publishes at the next
    /// one would never be found.
    /// </summary>
    [Fact]
    public async Task GetConfigurationAsync_AnEarlierCandidateTimingOut_StillFindsTheDocumentAtTheNextAddress()
    {
        // Arrange
        var documents = Substitute.For<IDocumentRetriever>();
        documents.GetDocumentAsync(CandidateAddresses[0], Arg.Any<CancellationToken>())
            .Throws(new TaskCanceledException("The request timed out.", new TimeoutException()));
        documents.GetDocumentAsync(CandidateAddresses[1], Arg.Any<CancellationToken>())
            .Returns(DiscoveryDocument(Issuer));

        var retriever = new OAuthAuthorizationServerMetadataRetriever(ProfileName, Issuer, CandidateAddresses);

        // Act
        var configuration = await retriever.GetConfigurationAsync(
            CandidateAddresses[0],
            documents,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Issuer, configuration.Issuer);
    }

    /// <summary>RFC 8414 section 3.3 requires the document to report the issuer it was found for, which is what stops a discoverable address naming an issuer the operator never trusted.</summary>
    [Fact]
    public async Task GetConfigurationAsync_ADocumentReportingAnotherIssuer_IsPassedOverForTheNextCandidate()
    {
        // Arrange
        var documents = Substitute.For<IDocumentRetriever>();
        documents.GetDocumentAsync(CandidateAddresses[0], Arg.Any<CancellationToken>())
            .Returns(DiscoveryDocument("https://sso.example.test/realms/other"));
        documents.GetDocumentAsync(CandidateAddresses[1], Arg.Any<CancellationToken>())
            .Returns(DiscoveryDocument(Issuer));

        var retriever = new OAuthAuthorizationServerMetadataRetriever(ProfileName, Issuer, CandidateAddresses);

        // Act
        var configuration = await retriever.GetConfigurationAsync(
            CandidateAddresses[0],
            documents,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Issuer, configuration.Issuer);
    }

    /// <summary>The failure names the operator's own profile and neither the issuer nor the addresses, because an exception message travels further than the configuration that produced it.</summary>
    [Fact]
    public async Task GetConfigurationAsync_NoCandidateAnswering_FailsNamingTheProfileAlone()
    {
        // Arrange
        var documents = Substitute.For<IDocumentRetriever>();
        documents.GetDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new IOException("The address answered 404."));

        var retriever = new OAuthAuthorizationServerMetadataRetriever(ProfileName, Issuer, CandidateAddresses);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => retriever.GetConfigurationAsync(
            CandidateAddresses[0],
            documents,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(ProfileName, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Issuer, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("well-known", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Caller cancellation is the one cancellation that ends the search, because there is no longer a request to find a document for.</summary>
    [Fact]
    public async Task GetConfigurationAsync_TheCallerCancelling_StopsWithoutTryingTheRemainingCandidates()
    {
        // Arrange
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        var documents = Substitute.For<IDocumentRetriever>();
        documents.GetDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException(caller.Token));

        var retriever = new OAuthAuthorizationServerMetadataRetriever(ProfileName, Issuer, CandidateAddresses);

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            retriever.GetConfigurationAsync(CandidateAddresses[0], documents, caller.Token));

        await documents.Received(1).GetDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A document with no key set address, so retrieval ends at the document itself rather than fetching keys the test does not describe.</summary>
    private static string DiscoveryDocument(string issuer) =>
        $$"""{"issuer":"{{issuer}}","response_types_supported":["code"],"subject_types_supported":["public"]}""";
}
