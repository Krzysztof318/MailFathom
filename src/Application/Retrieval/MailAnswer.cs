// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval;

/// <summary>What one run produced: the answer, in the words the model wrote it in.</summary>
/// <param name="Text">The answer, which is never empty.</param>
/// <remarks>
/// <para>
/// The text alone, because everything else a run produced is on the observation the caller handed in. The mail it was
/// written from, how much of the mailbox it reached, and how its retrieval degraded are facts about the run rather than
/// about the answer, and a run that ends without an answer has all of them and none of this.
/// </para>
/// <para>
/// Splitting them that way is what lets one caller publish citations and record a run from the same place, whether the
/// run reached this type or threw instead.
/// </para>
/// </remarks>
public sealed record MailAnswer(string Text);
