// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Localization;

/// <summary>A sentence the service itself writes for a person to read, named once so no caller can misspell its key.</summary>
/// <remarks>The member's name is the key its sentence is stored under in every language's resource file.</remarks>
public enum ApplicationText
{
    /// <summary>The agent's note written into a conversation when the person stops the answer being composed.</summary>
    AgentRunStoppedNote = 0,
}
