// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Chat;

/// <summary>One turn of a conversation: who it came from, what it said, and at most one picture it said it about.</summary>
/// <param name="Role">Who the turn came from.</param>
/// <param name="Text">What the turn said, which is never blank even where the turn is mostly its picture.</param>
/// <param name="Image">The picture the turn carries, or <see langword="null" /> for a turn of text alone.</param>
/// <remarks>
/// <para>
/// Text always, and a picture where a caller has one. An image arrived with its own bounds and its own privacy question
/// — the octet ceiling one request may carry, and the fact that no content scan applies to a photograph — and both are
/// answered where they belong: the ceiling on the plan the adapter runs on, the disclosure on
/// <see cref="ChatImage" />. An audio clip and a tool result remain capabilities rather than shapes this type is
/// missing, and each waits for the caller that needs it.
/// </para>
/// <para>
/// One picture rather than several, because the caller this exists for describes one attachment per call and a turn
/// carrying a set would need to say what the model is being asked about the set. A second image is a second call.
/// </para>
/// <para>
/// The text of a turn is untrusted and frequently personal: a question a person typed, and passages of somebody's mail.
/// It is never logged, never traced, and never carried into a failure, and the image is treated the same way.
/// </para>
/// </remarks>
public sealed record ChatMessage(ChatRole Role, string Text, ChatImage? Image = null);
