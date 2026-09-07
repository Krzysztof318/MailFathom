// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.AttachmentText;

/// <summary>How many attachments one skip reason accounts for, as a deployment reports it.</summary>
/// <param name="Outcome">Why those attachments yielded no text.</param>
/// <param name="AttachmentCount">How many attachments ended that way.</param>
internal sealed record AttachmentSkip(
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("attachmentCount")] long AttachmentCount)
{
    /// <summary>Describes the reason in the words an operator reads it by.</summary>
    /// <returns>The sentence naming what the deployment could not read, or the deployment's own word where this build does not know it.</returns>
    /// <remarks>
    /// A reason this build has never heard of is repeated verbatim rather than folded into a catch-all: a newer
    /// deployment naming a new refusal is exactly the case an operator needs to see the deployment's own word for.
    /// </remarks>
    internal string DescribeOutcome() => this.Outcome switch
    {
        "Encrypted" => "encrypted, so nothing could be opened",
        "Malformed" or "ImageUnreadable" => "corrupt, so the format could not be parsed",
        "FormatNotRecognized" or "FormatNotExtracted" or "FormatNotSupported" or "FormatExcluded" =>
            "in a format this deployment does not read",
        "NoTextExtracted" => "carrying no text to read, which is what a scan looks like",
        "InputTooLarge" or "ImageTooLarge" or "ExtractedTextTooLarge" or "PixelGridTooLarge"
            or "ContainerBoundExceeded" => "past a configured size bound",
        "ProviderRefused" or "ProviderUnavailable" or "ProviderTimedOut" =>
            "refused by the description provider, or unanswered by it",
        "TimedOut" => "past the extraction timeout",
        "MessageBudgetExhausted" => "past what one message may be read from, so they were never opened",
        "NotActivated" => "pictures this deployment does not describe",
        _ => this.Outcome ?? "not reported",
    };
}

/// <summary>How far a deployment has come reading a mailbox's attachments and images.</summary>
/// <remarks>
/// Reported apart from the embedding figures because it is counted apart from them: a mailbox may be entirely embedded
/// on its message text with every document in it still unread, and the two workloads are bounded in different units.
/// Every figure is grouped invariantly rather than for the terminal's culture, because the published binary sets
/// <c>InvariantGlobalization</c> and these are numbers an operator quotes back.
/// </remarks>
/// <param name="EmailsWithAttachmentCount">The stored messages carrying at least one attachment.</param>
/// <param name="ReadEmailCount">How many of those the deployment has already read.</param>
/// <param name="OutstandingEmailCount">How many of those it has not.</param>
/// <param name="OutstandingInputOctetCount">The octets those unread attachments would be opened over.</param>
/// <param name="OutstandingAttachmentCount">The attachments they carry.</param>
/// <param name="DocumentTextAttachmentCount">The attachments that yielded document text.</param>
/// <param name="DescribedImageCount">The images a provider described.</param>
/// <param name="IndexedCharacterCount">The characters that document text added to the lexical index.</param>
/// <param name="SkippedAttachmentCount">How many attachments yielded nothing, across every reason.</param>
/// <param name="Skips">One entry per reason.</param>
internal sealed record AttachmentDerivationCoverage(
    [property: JsonPropertyName("emailsWithAttachmentCount")] int EmailsWithAttachmentCount,
    [property: JsonPropertyName("readEmailCount")] int ReadEmailCount,
    [property: JsonPropertyName("outstandingEmailCount")] int OutstandingEmailCount,
    [property: JsonPropertyName("outstandingInputOctetCount")] long OutstandingInputOctetCount,
    [property: JsonPropertyName("outstandingAttachmentCount")] long OutstandingAttachmentCount,
    [property: JsonPropertyName("documentTextAttachmentCount")] long DocumentTextAttachmentCount,
    [property: JsonPropertyName("describedImageCount")] long DescribedImageCount,
    [property: JsonPropertyName("indexedCharacterCount")] long IndexedCharacterCount,
    [property: JsonPropertyName("skippedAttachmentCount")] long SkippedAttachmentCount,
    [property: JsonPropertyName("skips")] IReadOnlyList<AttachmentSkip>? Skips)
{
    /// <summary>Describes how much of the mailbox's attachments the deployment has read.</summary>
    /// <returns>The two message counts, and what reading the remainder would open where anything is left.</returns>
    internal string DescribeProgress()
    {
        if (this.EmailsWithAttachmentCount == 0)
        {
            return "no stored message carries an attachment";
        }

        var covered = string.Create(
            CultureInfo.InvariantCulture,
            $"{this.ReadEmailCount:N0} of {this.EmailsWithAttachmentCount:N0} messages with attachments read");

        return this.OutstandingEmailCount == 0
            ? $"{covered}; nothing outstanding"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{covered}; {this.OutstandingAttachmentCount:N0} attachments left over {this.OutstandingInputOctetCount:N0} octets");
    }

    /// <summary>Describes what reading has yielded, apart from what it could not read.</summary>
    /// <returns>The document extracts, the described images, and the characters the lexical index grew by.</returns>
    /// <remarks>
    /// The characters are reported as storage rather than as spend, which is what they are: chunking and the lexical
    /// index reach no provider, so the figure an operator weighs them against is disk rather than a bill.
    /// </remarks>
    internal string DescribeYield() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.DocumentTextAttachmentCount:N0} document extracts and {this.DescribedImageCount:N0} described images; {this.IndexedCharacterCount:N0} characters of lexical index");

    /// <summary>Describes what could not be read, reason by reason.</summary>
    /// <returns>One line per reason in the order the deployment reported them, or a single line where nothing was skipped.</returns>
    internal IReadOnlyList<string> DescribeSkips()
    {
        if (this.Skips is not { Count: > 0 } skips)
        {
            return ["none"];
        }

        return
        [
            .. skips.Select(skip => string.Create(
                CultureInfo.InvariantCulture,
                $"{skip.AttachmentCount:N0} {skip.DescribeOutcome()}")),
        ];
    }
}

/// <summary>Where one of a deployment's two attachment ceilings stands inside its current period.</summary>
/// <param name="Step">The step this period bounds.</param>
/// <param name="PeriodStartsAt">When the period began.</param>
/// <param name="PeriodEndsAt">When it rolls over, which is when paused work resumes.</param>
/// <param name="ConsumedUnitCount">What the step has already spent inside this period.</param>
/// <param name="CeilingUnitCount">What the period admits, or <see langword="null" /> where the deployment declared no ceiling.</param>
/// <param name="RemainingUnitCount">What the period still admits, or <see langword="null" /> where nothing is counted against.</param>
internal sealed record AttachmentDerivationPeriod(
    [property: JsonPropertyName("step")] string? Step,
    [property: JsonPropertyName("periodStartsAt")] DateTimeOffset PeriodStartsAt,
    [property: JsonPropertyName("periodEndsAt")] DateTimeOffset PeriodEndsAt,
    [property: JsonPropertyName("consumedUnitCount")] long ConsumedUnitCount,
    [property: JsonPropertyName("ceilingUnitCount")] long? CeilingUnitCount,
    [property: JsonPropertyName("remainingUnitCount")] long? RemainingUnitCount)
{
    /// <summary>Describes the period in one line an operator reads.</summary>
    /// <returns>What has been spent in the step's own unit, against the ceiling where one was declared, and when the period rolls over.</returns>
    /// <remarks>The unit is named in every line, because octets read and calls made are not the same quantity and a bare number would invite adding them.</remarks>
    internal string Describe()
    {
        var unit = this.Step switch
        {
            "Extraction" => "octets read",
            "Description" => "description calls",
            _ => "units",
        };

        var spent = this.CeilingUnitCount is { } ceiling
            ? string.Create(CultureInfo.InvariantCulture, $"{this.ConsumedUnitCount:N0} of {ceiling:N0} {unit}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{this.ConsumedUnitCount:N0} {unit}, against no declared ceiling");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{spent}; the period rolls over at {this.PeriodEndsAt:u}");
    }
}

/// <summary>What reading a deployment's attachments has covered, and what its own ceilings have spent.</summary>
/// <param name="Coverage">How far reading has come.</param>
/// <param name="Extraction">Where the octets-read period stands.</param>
/// <param name="Description">Where the description-calls period stands.</param>
internal sealed record AttachmentDerivationStatus(
    [property: JsonPropertyName("coverage")] AttachmentDerivationCoverage? Coverage,
    [property: JsonPropertyName("extraction")] AttachmentDerivationPeriod? Extraction,
    [property: JsonPropertyName("description")] AttachmentDerivationPeriod? Description);
