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
/// </remarks>
internal sealed record SyntheticCorpusPlan(
    int Seed,
    int Count,
    DateTimeOffset LatestSentAt,
    int SpanDays,
    int MaximumAttachmentBytes);
