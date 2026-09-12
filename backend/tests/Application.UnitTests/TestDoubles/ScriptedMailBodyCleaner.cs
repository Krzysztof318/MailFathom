// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Cleaning;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>A cleaner that answers a scripted proposal per attempt, and records what it was asked.</summary>
/// <remarks>
/// One entry per attempt rather than one answer repeated, because what the pass owes a reader is a single retry: a
/// script of two says whether the second attempt happened at all, and the recorded asks say whether the outline it was
/// asked with was composed once rather than per attempt.
/// </remarks>
internal sealed class ScriptedMailBodyCleaner : IMailBodyCleaner
{
    private readonly IReadOnlyList<MailBodyCleaningProposal> answers;

    private ScriptedMailBodyCleaner(bool isActive, IReadOnlyList<MailBodyCleaningProposal> answers)
    {
        this.IsActive = isActive;
        this.answers = answers;
    }

    /// <inheritdoc />
    public bool IsActive { get; }

    /// <summary>Gets the outlines the pass asked about, in the order it asked.</summary>
    public List<CleanableMailBody> Asked { get; } = [];

    /// <summary>Creates a cleaner that answers each attempt with the next scripted proposal.</summary>
    public static ScriptedMailBodyCleaner Proposing(params MailBodyCleaningProposal[] answers) =>
        new(isActive: true, answers);

    /// <summary>Creates a cleaner the deployment did not turn on, which answers without being asked anything.</summary>
    public static ScriptedMailBodyCleaner Inactive() =>
        new(isActive: false, [MailBodyCleaningProposal.Withheld(MailBodyCleaningWithholding.NotActivated)]);

    /// <summary>Creates a cleaner whose every attempt proposes the segments given.</summary>
    public static ScriptedMailBodyCleaner Keeping(params MailBodyCleaningSegment[] segments) =>
        Proposing(MailBodyCleaningProposal.Proposing(segments));

    /// <inheritdoc />
    public Task<MailBodyCleaningProposal> ProposeAsync(CleanableMailBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        this.Asked.Add(body);

        // Past the script's end the last answer stands, which is what a producer that answers the same way twice looks
        // like. A double that changed its answer once the script ran out would report the retry as something else.
        return Task.FromResult(this.answers[Math.Min(this.Asked.Count - 1, this.answers.Count - 1)]);
    }
}
