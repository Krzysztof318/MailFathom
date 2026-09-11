// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Access.Sessions;
using MailFathom.Domain.Failures;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Sessions;

/// <summary>Covers the one decision that keeps a database outage from signing every client of a deployment out.</summary>
/// <remarks>
/// A client meets <c>401</c> by clearing what it holds and asking a person for a password, so the status this handler
/// writes is the whole of what makes an outage survivable: answering <c>503</c> leaves every session live, and
/// answering anything a client reads as a refused credential loses every one the outage covered. Nothing else in the
/// composition decides it, which is why it is covered here rather than inferred from the routes.
/// </remarks>
public sealed class ClientSessionStoreUnavailableHandlerTests
{
    /// <summary>A store that could not answer is reported as the deployment being unavailable, carrying the code a client branches on.</summary>
    [Fact]
    public async Task TryHandleAsync_AStoreThatCouldNotBeReached_AnswersUnavailableWithItsOwnErrorCode()
    {
        // Arrange
        var context = RequestWithABody();

        // Act
        var handled = await new ClientSessionStoreUnavailableHandler().TryHandleAsync(
            context,
            Unreachable(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);

        var problem = await ReadBodyAsync(context);

        Assert.Equal(
            MailFathomErrorCode.ClientSessionStoreUnavailable.Value,
            problem.GetProperty(RouteAuthorization.ErrorCodeExtension).GetInt32());
    }

    /// <summary>The refusal a client reads as a password prompt is the one status this must never be.</summary>
    [Fact]
    public async Task TryHandleAsync_AStoreThatCouldNotBeReached_NeverAnswersAsUnauthenticated()
    {
        // Arrange
        var context = RequestWithABody();

        // Act
        await new ClientSessionStoreUnavailableHandler().TryHandleAsync(
            context,
            Unreachable(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    /// <summary>Every other failure is left to the pipeline, so this handler widens nothing into a `503` that is not one.</summary>
    [Fact]
    public async Task TryHandleAsync_AnyOtherFailure_IsLeftForThePipelineToAnswer()
    {
        // Arrange
        var context = RequestWithABody();

        // Act
        var handled = await new ClientSessionStoreUnavailableHandler().TryHandleAsync(
            context,
            new InvalidOperationException("Something else went wrong."),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static ClientSessionStoreUnavailableException Unreachable() =>
        new("The deployment's client sessions could not be reached.", new InvalidOperationException("No connection."));

    /// <summary>A context the handler can actually write a problem document into, which needs a body stream and the services the result writer resolves.</summary>
    private static DefaultHttpContext RequestWithABody()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);

        return await JsonSerializer.DeserializeAsync<JsonElement>(
            context.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
