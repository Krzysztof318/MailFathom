// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Chat;

/// <summary>What one model of a chain answered, and which model of the chain that was.</summary>
/// <param name="Alias">The deployment's own name for the model that produced this, which is what a log line names.</param>
/// <param name="Text">What it answered, absent where the call ended without text.</param>
/// <remarks>
/// A chain's answer carries its author because a fallback answering is invisible otherwise: the alias a capability was
/// configured with is the model it asked <em>first</em>, and a line naming that one for an answer the fallback produced
/// sends whoever reads it to the wrong endpoint. It is the same attribution the answering run records against its own
/// ledger, carried to the capabilities that log an outcome instead of recording one.
/// </remarks>
internal sealed record ChatModelAnswer(string Alias, string? Text);
