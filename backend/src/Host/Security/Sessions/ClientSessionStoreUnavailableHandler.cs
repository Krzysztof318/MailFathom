// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Sessions;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Diagnostics;

namespace MailFathom.Host.Security.Sessions;

/// <summary>Answers a request whose session could not be reached with <c>503</c> rather than with the <c>500</c> an unhandled failure would produce.</summary>
/// <remarks>
/// <para>
/// <b>The status is the point, and <c>401</c> is the one it must never be.</b> A client meets <c>401</c> by clearing
/// what it holds and asking for a password, so a database that could not be reached for a minute would sign every
/// signed-in client of the deployment out and lose every session the outage covered. Refusing as unavailable is what
/// makes an outage something the sessions survive.
/// </para>
/// <para>
/// It is a pipeline-wide handler rather than a <c>catch</c> on each route because the failure arrives from two
/// different places and only one of them is a route: the exchange, the renewal, and the revocation raise it from a
/// handler the framework invokes, and every other request on the surface raises it from the authentication handler,
/// which has no answer of its own to return. One registration covers both, and it is added only where a client
/// endpoint is served, because nothing else in the deployment holds a client session.
/// </para>
/// <para>
/// What reaches the caller is the message the store composed, which names the record and the operator's next step and
/// carries no connection string, no presented value, and nothing the server said. The provider's own failure stays in
/// the inner exception, and the warning written below is the only record of it: handling an exception here is what
/// stops the middleware from logging it, so a handler that wrote nothing would answer the whole deployment <c>503</c>
/// with nothing in the log to act on.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this handler for the exception-handling middleware.")]
internal sealed partial class ClientSessionStoreUnavailableHandler(ILogger<ClientSessionStoreUnavailableHandler> logger)
    : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not ClientSessionStoreUnavailableException unreachable)
        {
            return false;
        }

        this.LogSessionStoreUnavailable(unreachable);

        await TypedResults.Problem(
                unreachable.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [RouteAuthorization.ErrorCodeExtension] = unreachable.ErrorCode.Value,
                })
            .ExecuteAsync(httpContext);

        return true;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A client request was refused as unavailable because this deployment's sessions could not be reached.")]
    private partial void LogSessionStoreUnavailable(Exception failure);
}
