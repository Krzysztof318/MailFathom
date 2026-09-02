// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Configuration;

/// <summary>Decides when a declared configuration path covers a key a document or a caller supplied.</summary>
/// <remarks>
/// Both things this assembly declares about a setting — the store it is persisted in and whether it may be persisted
/// at all — are declared against a path rather than an exact key, because a path is often a section: refusing only
/// <c>Persistence:Password</c> would admit <c>Persistence:Password:SecretReference</c>, which is the half that decides
/// the credential, and routing only <c>Accounts</c> would leave <c>Accounts:0:DisplayName</c> in the root document.
/// One reading of "covers" is what keeps the two declarations from drifting into two answers.
/// </remarks>
internal static class SettingPath
{
    /// <summary>Reports whether a declared path names a key, or a section the key sits beneath.</summary>
    /// <param name="declaredPath">The path a catalog entry or a deny-list entry declares.</param>
    /// <param name="key">The configuration key to judge.</param>
    /// <returns><see langword="true" /> when the path names the key or an ancestor section of it.</returns>
    /// <remarks>
    /// Configuration keys are compared case-insensitively by every provider in the pipeline, so a document writing
    /// <c>persistence:password</c> names the same setting and is covered as one.
    /// </remarks>
    public static bool Covers(string declaredPath, string key) =>
        key.Equals(declaredPath, StringComparison.OrdinalIgnoreCase)
        || key.StartsWith($"{declaredPath}:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Finds the declared paths a set of configuration keys reaches.</summary>
    /// <param name="declaredPaths">The declared paths, in the order they are to be reported.</param>
    /// <param name="keys">The configuration keys a document flattened to.</param>
    /// <returns>The declared paths at least one key reaches, ordered as they are declared, empty when none is reached.</returns>
    public static IReadOnlyList<string> FindReachedIn(IEnumerable<string> declaredPaths, IEnumerable<string> keys)
    {
        var candidates = keys.ToArray();

        return [.. declaredPaths.Where(path => candidates.Any(key => Covers(path, key)))];
    }
}
