// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Organizations;

/// <summary>The organizations a deployment holds, and the rows it could not read as one.</summary>
/// <param name="Organizations">The organizations this deployment serves, ordered by short name.</param>
/// <param name="Unreadable">The rows whose stored short name is not one this build accepts, which are refused one at a time rather than refusing the listing.</param>
/// <remarks>
/// The two travel together because an operator who cannot see the broken row cannot repair it. A short name is written
/// through a route that judges it, so a row failing here was written by an older build, edited in the database, or
/// restored from a backup — and the listing is the only place it is visible at all, its members having lost nothing but
/// the ability to type the prefix their login begins with.
/// </remarks>
public sealed record OrganizationListing(
    IReadOnlyList<Organization> Organizations,
    IReadOnlyList<UnreadableOrganization> Unreadable);

/// <summary>One organization whose stored row this build will not read.</summary>
/// <param name="Id">The identifier every act on the organization names it by, which is what an operator repairs it with.</param>
/// <param name="DisplayName">The name an operator reads it by, which is stored text rather than a login.</param>
/// <param name="Correction">The sentence naming what the row must hold instead.</param>
/// <remarks>
/// The stored short name itself is not carried. It is the one value on the row that failed every rule this deployment
/// has about what a short name may contain, so repeating it would put text of unknown shape into a listing, a log, and
/// whatever renders them; the identifier names the row and the correction says what it must become.
/// </remarks>
public sealed record UnreadableOrganization(Guid Id, string DisplayName, string Correction);
