// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.AI.Orchestration;

/// <summary>What one AI operation is, apart from the endpoint it runs against and the parameters it runs with.</summary>
/// <remarks>
/// <para>
/// The three things an operation decides for itself. Everything else an agent is composed from — the chat client, the
/// generation parameters, the envelope around the instruction — is the same for every operation and is supplied by
/// <see cref="AgentComposition" />.
/// </para>
/// <para>
/// <see cref="Tools" /> is the capability rather than a description of one: an operation that only reads declares no
/// tool that mutates a mailbox, and there is nothing else it would have to say so in.
/// </para>
/// </remarks>
/// <param name="Name">What a run of this operation reports itself as.</param>
/// <param name="Instruction">What the operation tells the model about its task, before the envelope is placed around it.</param>
/// <param name="Tools">Everything the operation may do besides answer, which for a reading operation is what it may look up.</param>
internal sealed record AgentOperation(string Name, string Instruction, IReadOnlyList<AITool> Tools);
