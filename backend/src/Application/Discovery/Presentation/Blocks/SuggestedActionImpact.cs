// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>What taking a suggested action would change, which is what decides how a client offers it.</summary>
/// <remarks>
/// The three members are ordered by what they cost to undo, which is why they are worth distinguishing at all: opening
/// a thread costs nothing, filing a message is reversible by whoever filed it, and a message that has left the
/// deployment cannot be recalled. A client draws the third differently from the first whether or not the suggestion
/// asks for confirmation.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SuggestedActionImpact>))]
public enum SuggestedActionImpact
{
    /// <summary>Nothing changes; the action only shows the person something.</summary>
    ReadsOnly = 0,

    /// <summary>Something in the mailbox changes, and whoever took the action can change it back.</summary>
    ChangesMailbox = 1,

    /// <summary>Mail leaves the deployment, which nothing here can undo.</summary>
    SendsMail = 2,
}
