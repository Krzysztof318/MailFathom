// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Chat;

/// <summary>One turn of a conversation: who it came from and what it said.</summary>
/// <param name="Role">Who the turn came from.</param>
/// <param name="Text">What the turn said.</param>
/// <remarks>
/// <para>
/// Text alone. A turn carrying an image, an audio clip, or a tool result is a capability rather than a shape this type
/// is missing, and every one of them would arrive with its own bounds, its own privacy question, and its own provider
/// support matrix. The port that carries them is the port that has a caller needing them.
/// </para>
/// <para>
/// The text of a turn is untrusted and frequently personal: a question a person typed, and — once retrieval exists
/// above this boundary — passages of their mail. It is never logged, never traced, and never carried into a failure.
/// </para>
/// </remarks>
public sealed record ChatMessage(ChatRole Role, string Text);
