// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security.Transport;

/// <summary>Reads, once per request, what the user it acts for holds, before any route asks.</summary>
internal static class CallerGrantResolution
{
    /// <summary>Reads the grant of the user every request on a mail-serving surface acts for, and attaches it to the request.</summary>
    /// <param name="app">The application being composed.</param>
    /// <returns>The application, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Behind authorization, which is where a surface's own scheme runs and the caller is established, and ahead of
    /// every route, every tool call, and every use case, each of which reads the principal synchronously. A request
    /// this never ran for is answered as one whose user holds nothing, so a branch that bypassed it would refuse rather
    /// than admit. It runs whether or not any surface authenticates, because a surface configuring no credential still
    /// serves a user whose grant is read the same way.
    /// </remarks>
    internal static IApplicationBuilder UseCallerGrantResolution(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            await context.RequestServices
                .GetRequiredService<TransportAuthorizedPrincipalSource>()
                .ResolveGrantAsync(context.RequestAborted);

            await next(context);
        });
    }
}
