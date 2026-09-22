// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Localization;

/// <summary>A sentence the service itself writes for a person to read, named once so no caller can misspell its key.</summary>
/// <remarks>The member's name is the key its sentence is stored under in every language's resource file.</remarks>
internal enum ApplicationText
{
    /// <summary>The agent's note written into a conversation when the person stops the answer being composed.</summary>
    AgentRunStoppedNote = 0,

    /// <summary>The status line an Agent answer shows before it has looked anything up.</summary>
    AgentStatusReadingQuestion = 1,

    /// <summary>The status line while the Agent searches the person's mail.</summary>
    AgentStatusSearchingMail = 2,

    /// <summary>The status line while the Agent reads one thread of mail.</summary>
    AgentStatusReadingThread = 3,

    /// <summary>The status line while the Agent reads the person's calendar.</summary>
    AgentStatusReadingCalendar = 4,

    /// <summary>The status line while the Agent reads the person's tasks.</summary>
    AgentStatusReadingTasks = 5,

    /// <summary>The status line while the Agent composes something the person will be asked to approve.</summary>
    AgentStatusPreparingProposal = 6,

    /// <summary>The status line while the Agent writes the answer itself.</summary>
    AgentStatusComposingAnswer = 7,
}
