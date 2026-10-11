// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Cli.Administration.Users;

namespace MailFathom.Cli.Commands;

/// <summary>How <c>user show --effective</c> and <c>account show --effective</c> print a record as it is served.</summary>
/// <remarks>
/// One line per property: what it is, which step of the resolution order supplied it, whose settings policy holds it
/// against the person the record is about, and whether whoever read it may change it. The two commands print the
/// same lines because the deployment resolves a user's record and a mail account by the one order.
/// </remarks>
internal static class EffectiveSettingsOutput
{
    /// <summary>Writes a record as the settings policies governing it resolve it.</summary>
    /// <param name="console">Where the record is written.</param>
    /// <param name="unresolved">The sentence saying which settings policy the deployment could not read, where the properties were not resolved.</param>
    /// <param name="properties">The properties as the deployment resolved them, or <see langword="null" /> from a deployment that reports none.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="console" /> is <see langword="null" />.</exception>
    internal static void Write(ICliConsole console, string? unresolved, IReadOnlyList<UserRecordProperty>? properties)
    {
        ArgumentNullException.ThrowIfNull(console);

        if (unresolved is not null)
        {
            console.WriteLine($"  {ConsoleSafeText.SanitizeExplanation(unresolved)}");

            return;
        }

        foreach (var property in properties ?? [])
        {
            console.WriteLine(Describe(property));
        }
    }

    /// <summary>Writes one resolved setting as a line: what it is, where it came from, and who holds it.</summary>
    /// <remarks>
    /// Every part the deployment answered with is reduced before it is written. A value is what somebody stated — a
    /// name is one a person sets for themselves — so it could otherwise carry the characters a terminal acts on into
    /// an administrator's listing.
    /// </remarks>
    private static string Describe(UserRecordProperty property)
    {
        var value = property.Value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } stated
            ? ConsoleSafeText.Sanitize(stated.ValueKind == JsonValueKind.String ? stated.GetString() : stated.GetRawText())
            : "(nothing stated)";
        var heldBy = property.HeldBy is { } holder
            ? $", held by the {ConsoleSafeText.Sanitize(holder.ToLowerInvariant())}"
            : string.Empty;
        var changeable = property switch
        {
            { Changeable: true } => string.Empty,
            { Source: { } source } when source.EndsWith("Forced", StringComparison.Ordinal) => ", not changeable while the policy forces it",
            _ => ", not changeable under your grant",
        };

        return $"  {ConsoleSafeText.Sanitize(property.Path)}: {value}  [{ConsoleSafeText.Sanitize(property.Source)}{heldBy}{changeable}]";
    }
}
