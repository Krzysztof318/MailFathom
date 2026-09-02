// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Cli.Administration;

/// <summary>Writes the filters one administrative request carries as the query string it sends them in.</summary>
/// <remarks>
/// <para>
/// One place decides how a filter is escaped and how an absent one is left out, because every route asking for a
/// narrowed reading needs both decisions and each answered them again. A rule name may carry a space, a cursor is
/// base64url, and a verdict is a word an operator typed, so a query string assembled by hand is a defect waiting for
/// the first value that is not a bare word.
/// </para>
/// <para>
/// A filter the caller named nothing for is left out rather than sent empty, so the request says what the operator
/// said: the deployment reads an absent account as every account it serves and an absent size as its own default, and
/// a parameter present but blank would be one more shape for it to have an opinion about.
/// </para>
/// </remarks>
internal sealed class AdminQueryString
{
    private readonly List<string> filters = [];

    /// <summary>Adds a filter carrying a word an operator wrote.</summary>
    /// <param name="name">The parameter's name, which is this tool's own rather than anything typed.</param>
    /// <param name="value">The value, or <see langword="null" /> or empty to leave the filter out.</param>
    /// <returns>This builder, so one request's filters read as one expression.</returns>
    internal AdminQueryString Add(string name, string? value)
    {
        if (value is { Length: > 0 } named)
        {
            this.filters.Add($"{name}={Uri.EscapeDataString(named)}");
        }

        return this;
    }

    /// <summary>Adds a filter carrying a count.</summary>
    /// <param name="name">The parameter's name, which is this tool's own rather than anything typed.</param>
    /// <param name="value">The value, or <see langword="null" /> to leave the filter out.</param>
    /// <returns>This builder, so one request's filters read as one expression.</returns>
    internal AdminQueryString Add(string name, int? value)
    {
        if (value is { } counted)
        {
            this.filters.Add($"{name}={counted.ToString(CultureInfo.InvariantCulture)}");
        }

        return this;
    }

    /// <summary>Adds a filter naming one record by its identifier.</summary>
    /// <param name="name">The parameter's name, which is this tool's own rather than anything typed.</param>
    /// <param name="value">The value, or <see langword="null" /> to leave the filter out.</param>
    /// <returns>This builder, so one request's filters read as one expression.</returns>
    /// <remarks>Written in the hyphenated form invariantly, which is the one every administrative route reads an identifier in.</remarks>
    internal AdminQueryString Add(string name, Guid? value)
    {
        if (value is { } identified)
        {
            this.filters.Add($"{name}={identified.ToString("D", CultureInfo.InvariantCulture)}");
        }

        return this;
    }

    /// <summary>Writes what was added as a query string.</summary>
    /// <returns>An empty string where no filter was named, and the filters after a <c>?</c> otherwise.</returns>
    public override string ToString() =>
        this.filters.Count == 0 ? string.Empty : $"?{string.Join('&', this.filters)}";
}
