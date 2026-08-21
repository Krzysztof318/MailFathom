// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation.SensitiveDecoys;

/// <summary>One fabricated secret or identifier, planted in the message that carries it.</summary>
/// <param name="Kind">What was planted, and what is expected to find it.</param>
/// <param name="Placement">Where in the sentence the value was written, which decides what character follows it.</param>
/// <param name="Sentence">The paragraph the message carries, value included.</param>
/// <remarks>
/// The placement is carried beside the kind because it is the second half of what a decoy tests. A corpus whose
/// listing reported only the category would say a message carries a provider token and leave a reader unable to tell
/// a rule that finds no provider token at all from one that finds it everywhere except at the end of a sentence.
/// </remarks>
internal sealed record SensitiveDecoy(
    SensitiveDecoyKind Kind,
    SensitiveDecoyPlacement Placement,
    string Sentence);
