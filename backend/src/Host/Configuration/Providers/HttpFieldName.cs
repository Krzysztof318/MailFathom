// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Providers;

/// <summary>Reads a declared header name against what a field name may be, and against what a request writes for itself.</summary>
/// <remarks>
/// Written here rather than taken from the framework because nothing in it publishes the check: the header collections
/// validate on the way in and report a failure as an exception at the point of use, which for a configured value means
/// a start that succeeded and a provider call that threw. What this buys is the refusal at startup instead.
/// </remarks>
internal static class HttpFieldName
{
    /// <summary>The names an operator may not declare, because the client construction or the transport writes each of them.</summary>
    /// <remarks>
    /// <c>Authorization</c> is the credential's own and is what the declared key or the Microsoft Entra token is written
    /// as, so a declaration here would be a second credential silently winning or losing against the one an operator
    /// configured. The other four frame the message, and a value of an operator's on any of them describes a request
    /// other than the one actually sent.
    /// </remarks>
    private static readonly string[] WrittenByTheRequest =
    [
        "Authorization",
        "Host",
        "Content-Length",
        "Content-Type",
        "Transfer-Encoding",
    ];

    /// <summary>Reports whether a name is a field name at all.</summary>
    /// <param name="name">The declared name, already trimmed.</param>
    /// <returns><see langword="true" /> when every character is one a field name may carry.</returns>
    /// <remarks>The token rule of RFC 9110, which is the whole of what a field name may be: no space, no colon, and no character above ASCII.</remarks>
    public static bool IsFieldName(string name) => name.Length > 0 && name.All(IsTokenCharacter);

    /// <summary>Reports whether a name is one the request writes for itself.</summary>
    /// <param name="name">The declared name, already trimmed.</param>
    /// <returns><see langword="true" /> when an operator may not declare it.</returns>
    /// <remarks>Compared without case, because a field name is case-insensitive and a declaration spelling one differently still reaches the same header.</remarks>
    public static bool IsWrittenByTheRequestItself(string name) =>
        WrittenByTheRequest.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static bool IsTokenCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character, StringComparison.Ordinal);
}
