// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Prepares each request's scope with the accounts of the user its caller acts for, before anything in it reads an account's settings.</summary>
/// <remarks>
/// <para>
/// A request on a surface serving one person's mail acts for one user, and every account setting it reads — a folder
/// mapping, a transport policy, a sender identity — is one of that user's accounts. So the scope is given exactly those,
/// read through the served-user cache, rather than a snapshot holding every account the deployment serves. It runs behind
/// authorization, which is where the caller is established, and ahead of the endpoint, whose parameters are resolved from
/// the scope it prepares.
/// </para>
/// <para>
/// A request whose caller acts for nobody — an administrator's, or one refused before here — is left unprepared and
/// reads no account. So is one whose caller cannot be attributed to a user, because the deployment holds nobody or
/// several: the endpoint raises that same failure where it asks, which is where it is classified and answered.
/// </para>
/// </remarks>
internal static class ActingUserMailSettings
{
    /// <summary>Prepares the request's scope, then hands the request on.</summary>
    /// <param name="context">The request.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <returns>A task that completes once the rest of the pipeline has.</returns>
    internal static async Task PrepareAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (ActingUserOf(context.RequestServices.GetRequiredService<IAuthorizedPrincipalSource>()) is { } user)
        {
            await context.RequestServices
                .GetRequiredService<ScopedMailSynchronizationSettings>()
                .UseUserSettingsAsync(user, context.RequestAborted);
        }

        await next(context);
    }

    private static UserId? ActingUserOf(IAuthorizedPrincipalSource principals)
    {
        try
        {
            return principals.Current?.User;
        }
        catch (DeploymentUserUnresolvedException)
        {
            return null;
        }
    }
}
