// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Paging;

/// <summary>The deployment-wide administrative listings walked one page at a time in identifier order.</summary>
/// <remarks>
/// Each one names the listing a cursor was issued by, so a cursor carried from one listing to another is refused rather
/// than read as a position in a set it never described.
/// </remarks>
public enum AdministrativeListing
{
    /// <summary>The organizations a deployment holds.</summary>
    Organizations = 0,

    /// <summary>The mail accounts a deployment holds.</summary>
    MailAccounts = 1,

    /// <summary>The users a deployment holds records for.</summary>
    Users = 2,
}
