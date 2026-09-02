// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools.Results;

namespace MailFathom.Mcp.Tools.Summaries;

/// <summary>Reports whether the raw content of a listed email is stored locally, as the protocol spells it.</summary>
/// <remarks>
/// The transport carries its own enumeration for the reason <see cref="ListEmailsDirection" /> does: the member names are
/// the published wire values, so they belong to the boundary that publishes them rather than to the domain that happens
/// to describe the same states today.
/// </remarks>
internal enum ListedEmailContentAvailability
{
    /// <summary>The raw content is stored locally and a content read will find it.</summary>
    Available = 0,

    /// <summary>The email was deliberately stored without its content because it exceeded the configured size limit.</summary>
    ExceededSizeLimit = 1,

    /// <summary>The email was stored without its content because local storage was at its ceiling, and it is fetched once there is room.</summary>
    /// <remarks>
    /// A caller reading it is told a different thing from the state above: the content is absent for now rather than for
    /// good, so asking again later is worth doing where asking again about an oversized message never is.
    /// </remarks>
    AwaitingStorageHeadroom = 2,
}
