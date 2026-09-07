// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>What one derivation produced: the marks it settled, or the reason it produced none this time.</summary>
/// <remarks>
/// The two outcomes are unequal on purpose, and the whole of what a caller does with one follows from which it holds.
/// A settled answer — including a settled answer of no marks at all — is written down and takes the message out of the
/// queue. A withheld one is not written down and stops the pass, because every reason a derivation is withheld is a
/// condition that outlives one message and would be met again by the next.
/// </remarks>
public sealed record EmailEnrichmentDerivation
{
    private EmailEnrichmentDerivation(
        IReadOnlyList<EmailEnrichmentMark> marks,
        EmailEnrichmentWithholding? withheld)
    {
        this.Marks = marks;
        this.Withheld = withheld;
    }

    /// <summary>Gets what was derived, which is empty for a settled answer of nothing and for a withheld one alike.</summary>
    public IReadOnlyList<EmailEnrichmentMark> Marks { get; }

    /// <summary>Gets why nothing was derived, or <see langword="null" /> where the answer is settled.</summary>
    public EmailEnrichmentWithholding? Withheld { get; }

    /// <summary>Gets whether the answer is one to write down.</summary>
    public bool IsSettled => this.Withheld is null;

    /// <summary>Records a settled derivation.</summary>
    /// <param name="marks">What was derived, at most one mark per aspect and empty where there was nothing to say.</param>
    /// <returns>The derivation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="marks" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when two marks carry the same aspect.</exception>
    public static EmailEnrichmentDerivation Settled(IReadOnlyList<EmailEnrichmentMark> marks)
    {
        ArgumentNullException.ThrowIfNull(marks);

        if (marks.Select(mark => mark.Aspect).Distinct().Count() != marks.Count)
        {
            throw new ArgumentException(
                "A message carries at most one mark of each aspect.",
                nameof(marks));
        }

        return new EmailEnrichmentDerivation(marks, withheld: null);
    }

    /// <summary>Records that nothing was derived, and why.</summary>
    /// <param name="withholding">The condition that stopped it.</param>
    /// <returns>The derivation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="withholding" /> is not a defined member.</exception>
    public static EmailEnrichmentDerivation Withholding(EmailEnrichmentWithholding withholding)
    {
        if (!Enum.IsDefined(withholding))
        {
            throw new ArgumentOutOfRangeException(
                nameof(withholding),
                withholding,
                "A withheld derivation names one of the conditions this system stops on.");
        }

        return new EmailEnrichmentDerivation([], withholding);
    }
}
