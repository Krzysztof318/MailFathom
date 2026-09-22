// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Answering;

/// <summary>What a running answer is doing, which its status line says in the person's language.</summary>
/// <remarks>
/// A closed set rather than a line the composition writes, so the status line is this deployment's own words in the
/// person's language and never text a model produced: a model's status could name a thread, and it could be written in
/// whatever language the mail it had just read was.
/// </remarks>
public enum AgentActivity
{
    /// <summary>The run has started and has looked nothing up yet.</summary>
    ReadingQuestion = 0,

    /// <summary>The run is searching the person's mail.</summary>
    SearchingMail = 1,

    /// <summary>The run is reading one thread of mail.</summary>
    ReadingThread = 2,

    /// <summary>The run is reading the person's calendar.</summary>
    ReadingCalendar = 3,

    /// <summary>The run is reading the person's tasks.</summary>
    ReadingTasks = 4,

    /// <summary>The run is composing something the person will be asked to approve.</summary>
    PreparingProposal = 5,

    /// <summary>The run is writing the answer itself.</summary>
    ComposingAnswer = 6,
}
