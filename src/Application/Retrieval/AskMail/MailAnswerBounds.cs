// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>How much of one run's outcome a single answer publishes.</summary>
/// <remarks>
/// <para>
/// These bound the response rather than the run. What the run itself may draw out of a mailbox is
/// <see cref="EmailKnowledgeBounds" />, applied per lookup where the passages are built; a run makes several lookups, so
/// neither bound implies the other and both are needed.
/// </para>
/// <para>
/// Both are cut rather than refused, which is the opposite of how a request bound behaves and is deliberate. A request
/// larger than a limit is the caller's to correct, so refusing hands the decision back to them; an answer larger than a
/// limit has already been generated and paid for, and refusing it would discard a real answer over its length. What
/// makes cutting safe is that it is reported: a caller is told which of the two was cut instead of reading a shortened
/// answer as a complete one.
/// </para>
/// </remarks>
public sealed record MailAnswerBounds
{
    private MailAnswerBounds(int maximumAnswerCharacters, int maximumCitations)
    {
        this.MaximumAnswerCharacters = maximumAnswerCharacters;
        this.MaximumCitations = maximumCitations;
    }

    /// <summary>Gets the bounds a deployment that states none receives.</summary>
    /// <remarks>
    /// Twenty thousand characters is far above the answer a model produces under the output-token budget a deployment
    /// declares by default, and it is a ceiling on an endpoint that was reconfigured or replaced rather than an estimate
    /// of a well-behaved one. Twenty citations is above what a run's lookups can distinctly reach in ordinary use, for
    /// the same reason.
    /// </remarks>
    public static MailAnswerBounds Default { get; } = new(20_000, 20);

    /// <summary>Gets the greatest number of characters one answer may carry.</summary>
    public int MaximumAnswerCharacters { get; }

    /// <summary>Gets the greatest number of emails one answer may cite.</summary>
    public int MaximumCitations { get; }

    /// <summary>Creates bounds, refusing values no answer could be published under.</summary>
    /// <param name="maximumAnswerCharacters">The greatest number of characters one answer may carry.</param>
    /// <param name="maximumCitations">The greatest number of emails one answer may cite.</param>
    /// <returns>The validated bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either value is below one.</exception>
    public static MailAnswerBounds Create(int maximumAnswerCharacters, int maximumCitations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAnswerCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCitations, 1);

        return new MailAnswerBounds(maximumAnswerCharacters, maximumCitations);
    }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "at most {0} characters citing at most {1} emails",
        this.MaximumAnswerCharacters,
        this.MaximumCitations);
}
