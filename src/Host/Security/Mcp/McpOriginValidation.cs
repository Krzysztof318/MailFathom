// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Security.Transport;
using MailFathom.Mcp;
using Microsoft.Net.Http.Headers;

namespace MailFathom.Host.Security.Mcp;

/// <summary>Refuses an MCP request whose browser origin the deployment does not serve.</summary>
/// <remarks>
/// <para>
/// It runs ahead of authentication rather than behind it, because the two answer different questions and the origin
/// question does not depend on the answer to the other one. It also runs behind CORS, so a browser's preflight is
/// answered by the middleware that owns preflight and never reaches this check as a request to refuse.
/// </para>
/// <para>
/// The refusal is a <c>403</c> with no body. There is nothing useful to say: a browser will not surface it to the page
/// that caused it, and a non-browser caller never reaches it because it sends no origin.
/// </para>
/// </remarks>
internal static class McpOriginValidation
{
    /// <summary>Applies the origin policy to every request addressed to the MCP endpoint.</summary>
    /// <param name="app">The application being composed.</param>
    /// <returns>The application, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app" /> is <see langword="null" />.</exception>
    internal static WebApplication UseMcpOriginValidation(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var originPolicy = app.Services.GetRequiredService<McpOriginPolicy>();

        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(McpEndpointRoute.Path),
            mcpEndpoint => mcpEndpoint.Use(async (context, next) =>
            {
                if (originPolicy.Permits(context.Request.Headers[HeaderNames.Origin]))
                {
                    await next(context);

                    return;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            }));

        return app;
    }
}
