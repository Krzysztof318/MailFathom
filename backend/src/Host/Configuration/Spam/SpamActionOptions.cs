// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Configures what happens to mail a classification calls junk.</summary>
/// <remarks>
/// <para>
/// Both switches are off when the block is absent, so a deployment that switches classification on and states nothing
/// here records verdicts and writes to no mailbox. They are independent keys because they are independent decisions: an
/// operator may want spam out of the way without it being marked read, or marked read without being moved.
/// </para>
/// <para>
/// Everything either switch can do is a change
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
/// already permits and which a mail client reverses in one drag. Nothing here deletes, creates a folder, sends anything,
/// or writes a flag other than <c>\Seen</c>.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class SpamActionOptions
{
    /// <summary>Gets or sets whether junk is moved into the junk folder on the mail server.</summary>
    /// <remarks>
    /// The move is the ordinary relocation MailFathom already performs for a rule, written down as a durable record and
    /// carried out by the account's convergence pass. MailFathom never creates the folder, so an account with none is
    /// refused at startup rather than at the first spam message.
    /// </remarks>
    public bool MoveToJunkFolder { get; set; }

    /// <summary>Gets or sets whether junk has its remote <c>\Seen</c> flag set.</summary>
    /// <remarks>
    /// This is the one authored act that sets that flag. Synchronization and content retrieval still never do, which is
    /// the invariant this does not touch: reading a message on the owner's behalf must not mark it read, and deciding
    /// that a message is junk the operator asked to have marked read is a different act entirely.
    /// </remarks>
    public bool MarkAsRead { get; set; }

    /// <summary>Gets or sets the folder junk is filed into, or <see langword="null" /> to take whichever folder each account maps to the junk role.</summary>
    /// <remarks>
    /// Written the way every folder is named: an alias, or <c>role:Junk</c> for the folder an account labelled with that
    /// role. It does not have to be a folder MailFathom mirrors, and for most deployments it should not be — the point of
    /// filing spam is to be rid of it, and an unmirrored destination takes the local copy with it under the account's own
    /// answer about mail it deletes.
    /// </remarks>
    public string? JunkFolder { get; set; }

    /// <summary>Gets or sets the score a scanner has to reach before mail is touched, or <see langword="null" /> to act on every spam verdict.</summary>
    /// <remarks>
    /// It is a second reading of a scanner's score rather than a replacement for the one the verdict was reached under,
    /// so an operator can label at five and move at eight. Raising it is deliberately not the same edit as switching
    /// classification off: the verdicts go on being recorded and only the acting stops.
    /// </remarks>
    public double? Threshold { get; set; }

    /// <summary>Gets whether either switch asks for anything.</summary>
    internal bool IsAnyActionEnabled => this.MoveToJunkFolder || this.MarkAsRead;

    /// <summary>Gets the folder junk is filed into, with the junk role standing in for a destination nobody named.</summary>
    /// <remarks>
    /// Text that names neither an alias nor a role reads as unnamed here rather than raising, because validation refuses
    /// it against the key that wrote it and a lookup must not throw over a candidate a reload has already rejected.
    /// </remarks>
    internal MailFolderReference Destination =>
        MailFolderReference.TryCreate(this.JunkFolder, out var destination)
            ? destination
            : SpamActionSettings.DefaultJunkFolder;

    /// <summary>Builds the settings these keys describe.</summary>
    /// <returns>The settings the recorder reads.</returns>
    internal SpamActionSettings ToSettings() => SpamActionSettings.Create(
        this.MoveToJunkFolder,
        this.MarkAsRead,
        this.Destination,
        this.Threshold);

    /// <summary>Finds everything about this block that would otherwise be discovered on somebody's mail.</summary>
    /// <param name="classificationEnabled">Whether the section switches classification on at all.</param>
    /// <returns>One result per defect, each naming the key that carries it.</returns>
    internal IEnumerable<ValidationResult> FindErrors(bool classificationEnabled)
    {
        if (this.IsAnyActionEnabled && !classificationEnabled)
        {
            yield return new ValidationResult(
                $"{SpamClassificationOptions.SectionName} asks for junk to be acted on while classification is disabled, and there is no verdict to act on. Set Enabled to true, or remove the switches under Actions.",
                [nameof(this.MoveToJunkFolder), nameof(this.MarkAsRead)]);
        }

        if (this.JunkFolder is not null && !MailFolderReference.TryCreate(this.JunkFolder, out _))
        {
            yield return new ValidationResult(
                $"{SpamClassificationOptions.SectionName} names junk folder '{this.JunkFolder}', which is neither a usable folder alias nor one of the roles {string.Join(", ", Enum.GetNames<MailFolderSpecialUse>())} written as '{MailFolderReference.RoleScheme}<name>'.",
                [nameof(this.JunkFolder)]);
        }

        foreach (var result in this.FindThresholdErrors())
        {
            yield return result;
        }
    }

    private IEnumerable<ValidationResult> FindThresholdErrors()
    {
        if (this.Threshold is not { } threshold)
        {
            yield break;
        }

        if (!double.IsFinite(threshold)
            || threshold < SpamClassificationOptions.SmallestThreshold
            || threshold > SpamClassificationOptions.LargestThreshold)
        {
            yield return new ValidationResult(
                $"{SpamClassificationOptions.SectionName} declares an action {nameof(this.Threshold)} of {threshold.ToString(CultureInfo.InvariantCulture)}, and a threshold is between {SpamClassificationOptions.SmallestThreshold.ToString(CultureInfo.InvariantCulture)} and {SpamClassificationOptions.LargestThreshold.ToString(CultureInfo.InvariantCulture)}. A value outside that range either acts on every scored message or can never be reached.",
                [nameof(this.Threshold)]);
        }
    }
}
