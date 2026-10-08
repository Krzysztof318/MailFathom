// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Paging;

/// <summary>One page of an administrative listing walked in identifier order.</summary>
/// <typeparam name="TEntry">What one row of the listing reads as.</typeparam>
/// <param name="Entries">The rows, in identifier order.</param>
/// <param name="ContinuesAfter">The identifier the following page continues after, or <see langword="null" /> when this page is the last.</param>
public sealed record AdministrativeListingPage<TEntry>(IReadOnlyList<TEntry> Entries, Guid? ContinuesAfter);
