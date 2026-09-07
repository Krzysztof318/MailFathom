// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Rules;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>What one user asked to happen to mail a classification calls their junk.</summary>
/// <remarks>
/// <para>
/// The same four decisions <see cref="SpamActionOptions" /> holds for a deployment, written in a user's own record
/// because they are acts on that user's mail server: a message moved into a junk folder and marked read is a change to
/// somebody's mailbox, and nobody else's settings may ask for it. The two types are not one, because the deployment's
/// section is a decision an operator states for whichever users still read it, and this one is a decision its user
/// states for themselves — so each refusal names the document it was written in rather than the other one.
/// </para>
/// <para>
/// The bounds a threshold is judged against are shared rather than restated: they are the deployment's, and a user
/// writing a value outside them is refused at the write naming the range.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when a user's document is read.")]
internal sealed class UserSpamActionOptions
{
    /// <summary>Gets or sets whether junk is moved into the junk folder on the mail server.</summary>
    public bool MoveToJunkFolder { get; set; }

    /// <summary>Gets or sets whether junk has its remote <c>\Seen</c> flag set.</summary>
    public bool MarkAsRead { get; set; }

    /// <summary>Gets or sets the folder junk is filed into, or <see langword="null" /> to take whichever folder each of this user's accounts maps to the junk role.</summary>
    /// <remarks>
    /// Written the way every folder is named: an alias, or <c>role:Junk</c> for the folder an account labelled with that
    /// role. It is resolved within this user's own mail accounts and nowhere else, so a name that only another user's
    /// account carries is refused exactly as one this deployment does not serve.
    /// </remarks>
    public string? JunkFolder { get; set; }

    /// <summary>Gets or sets the score a scanner has to reach before this user's mail is touched, or <see langword="null" /> to act on every spam verdict.</summary>
    public double? Threshold { get; set; }

    /// <summary>Gets whether either switch asks for anything.</summary>
    internal bool IsAnyActionEnabled => this.MoveToJunkFolder || this.MarkAsRead;

    /// <summary>Gets the folder junk is filed into, with the junk role standing in for a destination nobody named.</summary>
    internal MailFolderReference Destination =>
        MailFolderReference.TryCreate(this.JunkFolder, out var destination)
            ? destination
            : SpamActionSettings.DefaultJunkFolder;

    /// <summary>Builds the settings the recorder reads for this user.</summary>
    /// <returns>The settings.</returns>
    internal SpamActionSettings ToSettings() => SpamActionSettings.Create(
        this.MoveToJunkFolder,
        this.MarkAsRead,
        this.Destination,
        this.Threshold);

    /// <summary>Finds everything about this block that would otherwise be discovered on the user's own mail.</summary>
    /// <param name="classificationEnabled">Whether the user switched classification on at all.</param>
    /// <param name="accounts">The user's own mail accounts, which are the only ones a destination may resolve within.</param>
    /// <returns>One result per refusal, each naming the setting that carries it.</returns>
    internal IEnumerable<ValidationResult> FindRefusals(
        bool classificationEnabled,
        IReadOnlyCollection<DeclaredMailAccount> accounts)
    {
        if (this.IsAnyActionEnabled && !classificationEnabled)
        {
            yield return new ValidationResult(
                $"The user record asks for junk to be acted on while {nameof(UserSpamClassificationOptions.Enabled)} is false, and there is no verdict to act on. Set it to true, or remove the switches under {UserSpamClassificationOptions.ActionsPath}.",
                [nameof(this.MoveToJunkFolder), nameof(this.MarkAsRead)]);
        }

        if (this.JunkFolder is not null && !MailFolderReference.TryCreate(this.JunkFolder, out _))
        {
            yield return new ValidationResult(
                $"The user record files junk into '{this.JunkFolder}', which is neither a usable folder alias nor one of the roles {string.Join(", ", Enum.GetNames<MailFolderSpecialUse>())} written as '{MailFolderReference.RoleScheme}<name>'.",
                [nameof(this.JunkFolder)]);
        }

        foreach (var result in this.FindThresholdRefusals())
        {
            yield return result;
        }

        foreach (var result in this.FindDestinationRefusals(classificationEnabled, accounts))
        {
            yield return result;
        }
    }

    /// <summary>Refuses a destination one of this user's own accounts could never file into.</summary>
    /// <remarks>
    /// The one claim this block makes about another part of the record, and the reason it is asked here rather than at
    /// startup: a user's accounts are their own, so a junk folder is judged against them and against nothing else.
    /// A user naming a folder only somebody else's account maps is refused exactly as one naming a folder nobody maps,
    /// because from inside this record the two are the same thing — MailFathom never creates the folder either way.
    /// </remarks>
    private IEnumerable<ValidationResult> FindDestinationRefusals(
        bool classificationEnabled,
        IReadOnlyCollection<DeclaredMailAccount> accounts)
    {
        // Nothing is judged unless filing is actually switched on. A destination written beside switches that are off is
        // a user preparing their record, and refusing it would make staging the change impossible.
        if (!classificationEnabled || !this.MoveToJunkFolder)
        {
            yield break;
        }

        var destination = this.Destination;

        foreach (var account in accounts.Where(account => !account.Maps(destination)))
        {
            yield return new ValidationResult(
                $"The user record files junk into '{destination}', and their account '{account.AccountId}' maps no such folder. Map it in that account's Folders, or name a folder the account has; MailFathom does not create one.",
                [nameof(this.JunkFolder)]);
        }
    }

    private IEnumerable<ValidationResult> FindThresholdRefusals()
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
                $"The user record declares an action {nameof(this.Threshold)} of {threshold.ToString(CultureInfo.InvariantCulture)}, and this deployment permits a threshold between {SpamClassificationOptions.SmallestThreshold.ToString(CultureInfo.InvariantCulture)} and {SpamClassificationOptions.LargestThreshold.ToString(CultureInfo.InvariantCulture)}. A value outside that range either acts on every scored message or can never be reached.",
                [nameof(this.Threshold)]);
        }
    }
}
