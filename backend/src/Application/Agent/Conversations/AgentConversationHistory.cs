// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Conversations;

/// <summary>Which of a conversation's two readings an entry belongs to, and which one a read returns.</summary>
/// <remarks>
/// <para>
/// A conversation is one ordered record, and the two readings are two ways of reading it rather than two stores. The
/// visible history is what was said to a person and by one. The technical history is all of it: every visible entry,
/// and beside them every entry written to compose a model input — a tool the model called, what the tool answered, what
/// a call was charged, and every summary a compaction produced. So an entry belonging to the visible history belongs to
/// the technical one as well, and an entry belonging to the technical history alone is one a person is never shown.
/// </para>
/// <para>
/// Every entry kind states which of the two it belongs to, and the hierarchy being closed is what makes that a checked
/// statement rather than a convention: a kind that did not say could not be declared.
/// </para>
/// </remarks>
public enum AgentConversationHistory
{
    /// <summary>What was said to a person or by one, which a person reads back and a turn is composed from.</summary>
    Visible = 0,

    /// <summary>What was written to compose a model input rather than to be read by somebody, which no person is shown.</summary>
    Technical = 1,
}
