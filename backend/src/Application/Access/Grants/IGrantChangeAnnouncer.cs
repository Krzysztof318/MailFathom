// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Grants;

/// <summary>Tells every replica that something a user's grant is computed from has changed.</summary>
/// <remarks>
/// <para>
/// Every replica keeps the grants it computed, so a committed change to a role, an assignment, a group's membership,
/// or the organization a user belongs to has to reach all of them before the next request is answered with what they
/// remember. This is how it does: the replica that committed the change forgets at once, and the others hear of it
/// over the deployment's signal backplane where one is declared and on their own interval where none is.
/// </para>
/// <para>
/// It is raised after the change commits and never before, and it fails nothing. A change that could not be
/// announced still reaches every replica within the interval, which is the guarantee; the announcement only shortens
/// the wait.
/// </para>
/// </remarks>
public interface IGrantChangeAnnouncer
{
    /// <summary>Announces that a change to what users are granted committed.</summary>
    /// <returns>A task that completes once this replica forgot what it computed and the announcement was handed on or dropped.</returns>
    Task AnnounceAsync();
}
