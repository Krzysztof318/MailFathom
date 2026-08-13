// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Runs;

/// <summary>What one pass did to one account's outstanding classification run.</summary>
/// <remarks>
/// Counts alone. A verdict is derived data about somebody's mail and a folder alias is MailFathom's own name for a
/// folder, so what a pass reports to a log is how many messages reached each outcome and nothing about any of them.
/// </remarks>
public sealed record SpamClassificationWalk
{
    /// <summary>A pass that found nothing to do.</summary>
    public static readonly SpamClassificationWalk Empty = new();

    /// <summary>Gets how many occurrences the pass recorded a verdict for.</summary>
    public int ClassifiedEmailCount { get; init; }

    /// <summary>Gets how many of the occurrences the pass reached carry a spam verdict.</summary>
    public int SpamEmailCount { get; init; }

    /// <summary>Gets how many of them carry a verdict that concluded nothing either way.</summary>
    public int UndeterminedEmailCount { get; init; }

    /// <summary>Gets how many occurrences the pass passed over because they were already decided under the run's profile.</summary>
    public int SkippedEmailCount { get; init; }

    /// <summary>Gets how many occurrences the pass could reach no verdict about.</summary>
    public int UnclassifiableEmailCount { get; init; }

    /// <summary>Gets how many occurrences the pass asked the mailbox to change, or would have in a dry run.</summary>
    public int ActedEmailCount { get; init; }

    /// <summary>Gets whether the run still has mail in front of it when the pass ended.</summary>
    public bool EmailsRemain { get; init; }

    /// <summary>Gets whether the pass did anything worth reporting.</summary>
    public bool IsEmpty =>
        this.ClassifiedEmailCount == 0
        && this.SkippedEmailCount == 0
        && this.UnclassifiableEmailCount == 0
        && !this.EmailsRemain;
}
