// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Organizations;

/// <summary>What an administrative act on an organization or a user's membership did, or why it did nothing.</summary>
public enum OrganizationWriteOutcome
{
    /// <summary>The act was performed.</summary>
    Written = 0,

    /// <summary>No organization carries the identifier the act named.</summary>
    UnknownOrganization = 1,

    /// <summary>Another organization already signs in under the short name.</summary>
    ShortNameTaken = 2,

    /// <summary>The organization still has members, so deleting it was refused.</summary>
    StillHasMembers = 3,

    /// <summary>No user carries the identifier the act named.</summary>
    UnknownUser = 4,

    /// <summary>The organization a user was moving into or out of already holds one of their usernames.</summary>
    UsernameTaken = 5,

    /// <summary>The deployment already holds <see cref="Organization.MaximumListed" /> organizations, so recording another was refused.</summary>
    OrganizationCeilingReached = 6,
}
