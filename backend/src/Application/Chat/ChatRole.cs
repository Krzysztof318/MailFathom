// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Chat;

/// <summary>Names who a turn of a conversation came from.</summary>
/// <remarks>
/// Three members rather than four: a tool turn is absent because this boundary sends no tools and receives no tool
/// call. Adding one is part of whatever gives the model tools, not of the transport that carries text.
/// </remarks>
public enum ChatRole
{
    /// <summary>The standing instruction the model is given before the conversation.</summary>
    System = 0,

    /// <summary>A turn from whoever is asking.</summary>
    User = 1,

    /// <summary>A turn the model produced, replayed as part of a longer conversation.</summary>
    Assistant = 2,
}
