// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Web;

namespace MailFathom.Cli.Authorization;

/// <summary>What an authorization server put in the address it redirected the person's browser to.</summary>
/// <param name="Code">The authorization code, present only on an approved request.</param>
/// <param name="State">The anti-forgery value the redirect echoed, which the caller checks before redeeming anything.</param>
/// <param name="Error">The error code from a refused request, absent from an approved one.</param>
/// <remarks>
/// Parsed rather than acted on. Deciding what the three fields mean belongs to the command, and keeping the parsing a
/// pure function is what lets it be covered without a socket.
/// </remarks>
internal sealed record MailboxRedirect(string? Code, string? State, string? Error)
{
    /// <summary>Reads a redirect out of the query string the browser arrived with.</summary>
    /// <param name="query">The query, with or without its leading question mark.</param>
    /// <returns>The redirect.</returns>
    /// <remarks>
    /// Every field is optional, because the request comes from a machine this process does not own: a scan, a stray
    /// browser prefetch, and a refused authorization all arrive here, and none of them may be read as an approval. An
    /// empty value is reported as absent rather than as an empty code, so one comparison decides both.
    /// </remarks>
    internal static MailboxRedirect FromQuery(string? query)
    {
        var parsed = HttpUtility.ParseQueryString(query ?? string.Empty);

        return new MailboxRedirect(Read("code"), Read("state"), Read("error"));

        string? Read(string name) => string.IsNullOrWhiteSpace(parsed[name]) ? null : parsed[name];
    }

    /// <inheritdoc />
    /// <remarks>Redacted by construction, because the authorization code is redeemable for a refresh token.</remarks>
    public override string ToString() => "***";
}

/// <summary>Waits for the authorization server to send the person's browser back with a code.</summary>
/// <remarks>
/// A port rather than a class the command constructs, because the implementation binds a socket and the whole of what
/// the command decides — a refused authorization, a mismatched anti-forgery value, a redirect that carries neither code
/// nor error — is reachable in a test only when this can be substituted.
/// </remarks>
internal interface IMailboxRedirectAwaiter : IDisposable
{
    /// <summary>Waits for one redirect and answers the browser that delivered it.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>What the redirect carried.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the wait is cancelled before a redirect arrives.</exception>
    Task<MailboxRedirect> WaitForRedirectAsync(CancellationToken cancellationToken);
}
