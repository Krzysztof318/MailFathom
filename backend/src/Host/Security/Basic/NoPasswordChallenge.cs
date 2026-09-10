// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security.Basic;

/// <summary>Marks a route whose refusal must not name the password method, because a browser would answer it itself.</summary>
/// <remarks>
/// <para>
/// The password challenge exists for a person: a client only asks for a username and a password when
/// <c>WWW-Authenticate: Basic</c> tells it to. A browser is that client, and it answers such a challenge with a dialog
/// of its own — which is the right thing on a page somebody navigated to and the wrong thing on a background request
/// a page made, where the dialog opens over whatever they were reading and asks for a credential the page already
/// holds.
/// </para>
/// <para>
/// Every request the client makes on its own behalf opts out of that by omitting its credentials, which is what the
/// Fetch Standard turns the prompt off for. This marker is for the requests it does not compose: a route reached by a
/// library that builds its own call and takes no option for the credentials mode. Marking such a route leaves the bare
/// bearer challenge every method on the surface produces, which is what the caller was carrying anyway.
/// </para>
/// </remarks>
internal sealed class NoPasswordChallenge
{
    /// <summary>The one instance, since the type carries nothing and is read by its presence alone.</summary>
    internal static readonly NoPasswordChallenge Instance = new();

    private NoPasswordChallenge()
    {
    }
}
