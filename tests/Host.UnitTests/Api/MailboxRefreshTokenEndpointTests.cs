// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the administrative surface's only write route decides about a request before it stores anything.</summary>
/// <remarks>
/// The route is reached with a long-lived mailbox credential in the body, so what is asserted here is the shape of
/// every refusal and the fact that a refusal stores nothing. Authentication is deliberately not among them: the route
/// carries no requirement of its own and inherits the group's, so it is asserted where that inheritance happens, in
/// <see cref="AdminApiEndpointsTests" />.
/// </remarks>
public sealed class MailboxRefreshTokenEndpointTests
{
    private static readonly MailAccountId Workspace = MailAccountId.Create("workspace");

    private readonly IMailboxRefreshTokenStore store = Substitute.For<IMailboxRefreshTokenStore>();

    [Fact]
    public async Task StoreAsync_AGrantForAServedAccount_StoresItAndAnswersWithNoBody()
    {
        // Arrange
        var request = new MailboxRefreshTokenRequest("workspace", "a-refresh-token");

        // Act
        var result = await MailboxRefreshTokenEndpoint.StoreAsync(
            request,
            this.RecorderServing(Workspace),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
        await this.store.Received(1).SaveTokenAsync(
            Workspace,
            Arg.Any<MailboxRefreshToken>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Naming the account is the whole of what makes this actionable: only the caller knows what they meant.</summary>
    [Fact]
    public async Task StoreAsync_AnAccountThisDeploymentDoesNotConfigure_IsRefusedNamingIt()
    {
        // Arrange
        var request = new MailboxRefreshTokenRequest("archive", "a-refresh-token");

        // Act
        var result = await MailboxRefreshTokenEndpoint.StoreAsync(
            request,
            this.RecorderServing(Workspace),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains("'archive'", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        await this.store.DidNotReceive().SaveTokenAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailboxRefreshToken>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "a-refresh-token")]
    [InlineData("", "a-refresh-token")]
    [InlineData("   ", "a-refresh-token")]
    [InlineData("workspace", null)]
    [InlineData("workspace", "")]
    [InlineData("workspace", "   ")]
    public async Task StoreAsync_ABodyMissingEitherField_IsRefusedWithoutStoringAnything(
        string? account,
        string? refreshToken)
    {
        // Arrange
        var request = new MailboxRefreshTokenRequest(account, refreshToken);

        // Act
        var result = await MailboxRefreshTokenEndpoint.StoreAsync(
            request,
            this.RecorderServing(Workspace),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        await this.store.DidNotReceive().SaveTokenAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailboxRefreshToken>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A request that carried no body at all binds as nothing, and is the same refusal rather than a fault.</summary>
    [Fact]
    public async Task StoreAsync_NoBodyAtAll_IsRefusedRatherThanFailing()
    {
        // Act
        var result = await MailboxRefreshTokenEndpoint.StoreAsync(
            request: null,
            this.RecorderServing(Workspace),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
    }

    /// <summary>
    /// A refusal is written here rather than taken from the failure, because an administrative endpoint publishes no
    /// exception message. The two must therefore not coincide, which is what this asserts.
    /// </summary>
    [Fact]
    public async Task StoreAsync_AnUnservedAccount_ReportsItsOwnSentenceRatherThanTheFailuresMessage()
    {
        // Arrange
        var request = new MailboxRefreshTokenRequest("archive", "a-refresh-token");
        var raised = new MailAccountNotAccessibleException(MailAccountId.Create("archive")).Message;

        // Act
        var result = await MailboxRefreshTokenEndpoint.StoreAsync(
            request,
            this.RecorderServing(Workspace),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.NotEqual(raised, refusal.ProblemDetails.Detail);
        Assert.Contains("configures no mail account", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The body is where the token arrives, and a record that rendered it would put a mailbox credential into whatever
    /// log template, diagnostic, or exception message happened to format the request.
    /// </summary>
    [Fact]
    public void ToString_ARequestCarryingAToken_RedactsIt()
    {
        // Arrange
        var request = new MailboxRefreshTokenRequest("workspace", "a-refresh-token");

        // Act
        var rendered = request.ToString();

        // Assert
        Assert.DoesNotContain("a-refresh-token", rendered, StringComparison.Ordinal);
        Assert.Contains("workspace", rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// The field names are the agreement with <c>mfctl</c>, which serializes its own record of this shape through a
    /// source-generated context. Nothing else compares the two, so a rename on either side would compile, serialize,
    /// and fail only against a real deployment as a body whose fields all bound to nothing.
    /// </summary>
    [Fact]
    public void Deserialized_TheBodyTheCommandSends_BindsEveryFieldTheRouteReads()
    {
        // Arrange
        const string Body = """{"account":"workspace","refreshToken":"a-refresh-token"}""";

        // Act
        var request = JsonSerializer.Deserialize<MailboxRefreshTokenRequest>(Body, JsonSerializerOptions.Web);

        // Assert
        Assert.NotNull(request);
        Assert.Equal("workspace", request.Account);
        Assert.Equal("a-refresh-token", request.RefreshToken);
    }

    private MailboxRefreshTokenRecorder RecorderServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccounts.Returns([.. servedAccountIds.Select(SyntheticServedAccount.Of)]);

        return new MailboxRefreshTokenRecorder(catalog, this.store);
    }
}
