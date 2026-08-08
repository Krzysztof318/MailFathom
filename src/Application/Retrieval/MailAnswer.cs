// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval;

/// <summary>What one run produced: the answer, and the mail it was written from.</summary>
/// <param name="Text">The answer, which is never empty.</param>
/// <param name="Passages">The passages the run retrieved, in the order it retrieved them.</param>
/// <remarks>
/// <para>
/// The passages travel with the answer because they are what makes it checkable: each carries the stable identifier of
/// the message it was cut from, so a reader can fetch that message rather than take the answer's word for it.
/// </para>
/// <para>
/// They are what the run retrieved rather than what the model demonstrably used. Nothing outside the model knows which
/// of them it drew on, so claiming the narrower set would be stating something this system cannot observe.
/// </para>
/// </remarks>
public sealed record MailAnswer(string Text, IReadOnlyList<EmailKnowledgePassage> Passages);
