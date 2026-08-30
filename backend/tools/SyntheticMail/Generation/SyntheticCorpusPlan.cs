// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>Everything that decides what a corpus is, so that two runs agreeing on it agree on every message.</summary>
/// <param name="Seed">What the whole corpus is derived from.</param>
/// <param name="Count">How many messages to produce.</param>
/// <param name="LatestSentAt">The newest date a generated message carries.</param>
/// <param name="SpanDays">How far back from <paramref name="LatestSentAt" /> the dates reach.</param>
/// <param name="MaximumAttachmentBytes">The ceiling on one attachment, and zero for a corpus that carries none.</param>
/// <param name="SensitivePercentage">How often a message carries fabricated sensitive material, in messages per hundred, and zero for a corpus that carries none.</param>
/// <param name="Languages">The languages AI-generated messages are written in, distributed across by the seed, and empty for a corpus the seeded vocabulary writes.</param>
/// <param name="Topics">The topics AI-generated messages are written about, distributed across by the seed, and empty for a corpus the seeded vocabulary writes.</param>
/// <remarks>
/// <para>
/// The date range is stated rather than derived from the clock, which is the difference between a corpus that can be
/// reproduced and one that only looks like it can. A run that specifies neither is given today's date and reports it,
/// so repeating the run exactly is a matter of copying the line it printed.
/// </para>
/// <para>
/// The bounds are enforced where the arguments are parsed rather than here, so a plan a test builds by hand is not
/// held to a command line's limits.
/// </para>
/// <para>
/// The last two parameters are the one axis a seed no longer decides on its own. A corpus with named languages is one
/// whose message content comes from a source the generator is handed, and the names say what the source is asked for,
/// distributed across the batch by the seed like everything else it draws. The two move together — both named or both
/// empty — because a corpus is either one the vocabulary writes or one a source writes, and <see cref="SyntheticEmailGenerator" />
/// is where a plan that mixes the two is refused. With both empty the plan describes exactly the corpus it described
/// before the axis existed, and a run that asks for neither is identical to one from before.
/// </para>
/// </remarks>
internal sealed record SyntheticCorpusPlan(
    int Seed,
    int Count,
    DateTimeOffset LatestSentAt,
    int SpanDays,
    int MaximumAttachmentBytes,
    int SensitivePercentage,
    IReadOnlyList<string> Languages,
    IReadOnlyList<SyntheticMailTopic> Topics);
