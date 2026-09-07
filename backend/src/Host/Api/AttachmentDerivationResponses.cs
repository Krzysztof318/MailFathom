// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Administration;
using MailFathom.Application.Emails.AttachmentText.Limits;

namespace MailFathom.Host.Api;

/// <summary>How many attachments one skip reason accounts for, as the administrative endpoint reports it.</summary>
/// <param name="Outcome">Why those attachments yielded no text, as the outcome's own name.</param>
/// <param name="AttachmentCount">How many attachments ended that way.</param>
/// <remarks>
/// Counted rather than listed, which is what makes it safe to serve: an aggregate over a reason says how much of a
/// mailbox a deployment cannot read without naming one message, one filename, or one sender.
/// </remarks>
internal sealed record AttachmentSkipResponse(string Outcome, long AttachmentCount)
{
    /// <summary>Describes one skip reason on the wire.</summary>
    /// <param name="skip">The counted reason.</param>
    /// <returns>The response body.</returns>
    internal static AttachmentSkipResponse For(AttachmentSkipCount skip) => new(skip.Outcome, skip.AttachmentCount);
}

/// <summary>How far reading a mailbox's attachments and images has come, as the administrative endpoint reports it.</summary>
/// <param name="EmailsWithAttachmentCount">The stored messages carrying at least one attachment.</param>
/// <param name="ReadEmailCount">How many of those this deployment has already read.</param>
/// <param name="OutstandingEmailCount">How many of those it has not.</param>
/// <param name="OutstandingInputOctetCount">The octets those unread attachments would be opened over, which is the unit the extraction ceiling counts in.</param>
/// <param name="OutstandingAttachmentCount">The attachments they carry, which bounds how many description calls the remainder could make.</param>
/// <param name="DocumentTextAttachmentCount">The attachments that yielded document text.</param>
/// <param name="DescribedImageCount">The images a provider described.</param>
/// <param name="IndexedCharacterCount">The characters that document text added to the lexical index, which is storage growth rather than provider spend.</param>
/// <param name="SkippedAttachmentCount">How many attachments yielded nothing, across every reason.</param>
/// <param name="Skips">One entry per reason, ordered by count and then by name.</param>
internal sealed record AttachmentDerivationCoverageResponse(
    int EmailsWithAttachmentCount,
    int ReadEmailCount,
    int OutstandingEmailCount,
    long OutstandingInputOctetCount,
    long OutstandingAttachmentCount,
    long DocumentTextAttachmentCount,
    long DescribedImageCount,
    long IndexedCharacterCount,
    long SkippedAttachmentCount,
    IReadOnlyList<AttachmentSkipResponse> Skips)
{
    /// <summary>Describes one mailbox's attachment coverage on the wire.</summary>
    /// <param name="coverage">The coverage.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="coverage" /> is <see langword="null" />.</exception>
    internal static AttachmentDerivationCoverageResponse For(AttachmentDerivationCoverage coverage)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        return new AttachmentDerivationCoverageResponse(
            coverage.EmailsWithAttachmentCount,
            coverage.ReadEmailCount,
            coverage.Outstanding.OutstandingEmailCount,
            coverage.Outstanding.OutstandingInputOctetCount,
            coverage.Outstanding.OutstandingAttachmentCount,
            coverage.DocumentTextAttachmentCount,
            coverage.DescribedImageCount,
            coverage.IndexedCharacterCount,
            coverage.SkippedAttachmentCount,
            [.. coverage.Skips.Select(AttachmentSkipResponse.For)]);
    }
}

/// <summary>Where one attachment ceiling's period stands, as the administrative endpoint reports it.</summary>
/// <param name="Step">The step this period bounds, as the step's own name.</param>
/// <param name="PeriodStartsAt">When the period began.</param>
/// <param name="PeriodEndsAt">When it rolls over, which is when paused work resumes.</param>
/// <param name="ConsumedUnitCount">What the step has already spent inside this period.</param>
/// <param name="CeilingUnitCount">What the period admits, or <see langword="null" /> where the deployment declared no ceiling.</param>
/// <param name="RemainingUnitCount">What the period still admits, or <see langword="null" /> where nothing is counted against.</param>
/// <remarks>
/// The unit is the step's own and is not comparable across two of these: extraction counts the octets it opens and
/// description counts the calls it makes. Reporting them as one figure would add a megabyte to a phone call.
/// </remarks>
internal sealed record AttachmentDerivationPeriodResponse(
    string Step,
    DateTimeOffset PeriodStartsAt,
    DateTimeOffset PeriodEndsAt,
    long ConsumedUnitCount,
    long? CeilingUnitCount,
    long? RemainingUnitCount)
{
    /// <summary>Describes one period on the wire.</summary>
    /// <param name="period">The period.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="period" /> is <see langword="null" />.</exception>
    internal static AttachmentDerivationPeriodResponse For(AttachmentDerivationPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);

        return new AttachmentDerivationPeriodResponse(
            period.Step.ToString(),
            period.StartsAt,
            period.EndsAt,
            period.ConsumedUnitCount,
            period.CeilingUnitCount,
            period.RemainingUnitCount);
    }
}

/// <summary>What reading this deployment's attachments has covered and what its own ceilings have spent.</summary>
/// <param name="Coverage">How far reading has come.</param>
/// <param name="Extraction">Where the octets-read period stands.</param>
/// <param name="Description">Where the description-calls period stands.</param>
internal sealed record AttachmentDerivationStatusResponse(
    AttachmentDerivationCoverageResponse Coverage,
    AttachmentDerivationPeriodResponse Extraction,
    AttachmentDerivationPeriodResponse Description)
{
    /// <summary>Describes one deployment's attachment state on the wire.</summary>
    /// <param name="status">The state.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="status" /> is <see langword="null" />.</exception>
    internal static AttachmentDerivationStatusResponse For(AttachmentDerivationStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new AttachmentDerivationStatusResponse(
            AttachmentDerivationCoverageResponse.For(status.Coverage),
            AttachmentDerivationPeriodResponse.For(status.Extraction),
            AttachmentDerivationPeriodResponse.For(status.Description));
    }
}
