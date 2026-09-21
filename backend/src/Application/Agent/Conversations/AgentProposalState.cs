// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>Where one proposed action stands.</summary>
/// <remarks>
/// <para>
/// Four values rather than three, because <see cref="Failed" /> is not a way of declining: it is what an action the
/// person did approve became when carrying it out did not work. Folding the two would tell somebody who pressed the
/// control that nothing was done for the same reason as somebody who pressed nothing, and the two owe different
/// answers — one of them is offered the attempt again.
/// </para>
/// <para>
/// <see cref="Pending" /> is the state of a proposal nothing has been recorded against, so it is never written: the
/// record holds a resolution or it does not, and the absence is what this member names when the conversation is read
/// back. That is also what lets the same proposal be offered a second time at a later place in the conversation
/// without the first offer's outcome following it.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AgentProposalState>))]
public enum AgentProposalState
{
    /// <summary>Nobody has answered it yet, and the controls are still offered.</summary>
    Pending = 0,

    /// <summary>The person approved it, which is what allows it to be carried out.</summary>
    Accepted = 1,

    /// <summary>It was approved and carrying it out did not work.</summary>
    Failed = 2,

    /// <summary>The person refused it, and nothing was done.</summary>
    Declined = 3,
}
