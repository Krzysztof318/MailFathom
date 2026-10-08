// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Paging;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailFathom.Host.Api;

/// <summary>Reads the page an administrative listing was asked for, and writes the cursor its answer continues from.</summary>
/// <remarks>
/// The organizations, mail accounts, and users are each walked the same way — a page size and the cursor the previous
/// page returned, nothing else — so the reading and both refusals are held here once rather than worded three times.
/// Every refusal is <c>400</c>, for the reason every other paged administrative reading gives: a cursor this listing
/// did not issue is a mistake in the request the caller wrote rather than a missing resource.
/// </remarks>
internal static class AdminListingRequest
{
    /// <summary>Reads the page a request asked one listing for.</summary>
    /// <param name="listing">The listing the request was sent to.</param>
    /// <param name="pageSize">How many rows the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="query">The page to read, when the request names one.</param>
    /// <param name="refusal">What the caller is told when it does not.</param>
    /// <returns><see langword="true" /> when the reading may go ahead.</returns>
    internal static bool TryResolve(
        AdministrativeListing listing,
        int? pageSize,
        string? cursor,
        [NotNullWhen(true)] out AdministrativeListingQuery? query,
        [NotNullWhen(false)] out ProblemHttpResult? refusal)
    {
        query = null;
        refusal = null;

        Guid? after = null;

        if (cursor is not null)
        {
            if (!AdministrativeListingCursor.TryDecode(cursor, listing, out var continuesAfter))
            {
                refusal = TypedResults.Problem(
                    "The continuation cursor is not one this listing issued.",
                    statusCode: StatusCodes.Status400BadRequest);

                return false;
            }

            after = continuesAfter;
        }

        query = AdministrativeListingQuery.Create(pageSize, after);

        if (query is null)
        {
            refusal = TypedResults.Problem(
                $"A page holds between 1 and {AdministrativeListingQuery.MaximumPageSize} records.",
                statusCode: StatusCodes.Status400BadRequest);

            return false;
        }

        return true;
    }

    /// <summary>Writes the cursor the page after one continues from.</summary>
    /// <param name="listing">The listing the page was read from.</param>
    /// <param name="continuesAfter">The identifier the following page continues after, or <see langword="null" /> at the end.</param>
    /// <returns>The cursor, or <see langword="null" /> when the page was the last.</returns>
    internal static string? NextCursor(AdministrativeListing listing, Guid? continuesAfter) =>
        continuesAfter is { } after ? AdministrativeListingCursor.Encode(listing, after) : null;
}
