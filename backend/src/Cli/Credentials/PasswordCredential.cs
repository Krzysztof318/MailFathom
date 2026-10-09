// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Http.Headers;
using System.Text;

namespace MailFathom.Cli.Credentials;

/// <summary>A username and password kept as the one credential a profile presents, and the header every request carries it in.</summary>
/// <remarks>
/// <para>
/// A password profile stores what a Basic sign-in sends, in the same place and under the same protection an API key
/// is stored, because it is the same kind of thing: a credential that stays valid until the deployment stops accepting
/// it. It is kept with its scheme in front so the stored value says how it is presented; an API key or a token never
/// contains a space, so the prefix cannot be mistaken for one.
/// </para>
/// <para>
/// It exists for the default administrator above all: a deployment's first start gives <c>admin</c> a password and
/// nothing else, and signing in with it is how the first credential of any other kind gets provisioned.
/// </para>
/// </remarks>
internal static class PasswordCredential
{
    private const string Scheme = "Basic";

    /// <summary>Composes the stored form of a username and password.</summary>
    /// <param name="login">What the person signs in as, <c>SHORTNAME/username</c> for a member of an organization.</param>
    /// <param name="password">The password.</param>
    /// <returns>The value a profile stores.</returns>
    internal static string Compose(string login, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(login);
        ArgumentException.ThrowIfNullOrEmpty(password);

        return $"{Scheme} {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}"))}";
    }

    /// <summary>Composes the <c>Authorization</c> header a stored credential is presented in.</summary>
    /// <param name="credential">What the profile stores: a composed password, or an API key or token presented as a bearer credential.</param>
    /// <returns>The header value.</returns>
    internal static AuthenticationHeaderValue AuthorizationFor(string credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return credential.StartsWith($"{Scheme} ", StringComparison.Ordinal)
            ? new AuthenticationHeaderValue(Scheme, credential[(Scheme.Length + 1)..])
            : new AuthenticationHeaderValue("Bearer", credential);
    }
}
