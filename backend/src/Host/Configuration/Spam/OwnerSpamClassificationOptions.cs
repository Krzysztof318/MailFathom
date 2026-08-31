// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Rules;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>How one owner asked their own mail to be classified, and what is to happen to their junk.</summary>
/// <remarks>
/// <para>
/// The owner's half of <see cref="SpamClassificationOptions" />, and deliberately only that half. Junk is a judgement
/// about somebody's own mailbox and the actions it triggers write to that person's mail server, so whether their mail is
/// classified at all, over which folders, at what score, and what becomes of the result are theirs — including the
/// decision not to classify at all, which no deployment setting requires of them.
/// </para>
/// <para>
/// What is not here is what costs the deployment a resource: where the scanner daemon is, what one scan may spend, how
/// many run at once, how fast a run may commit, and how long the index may be held back waiting on a verdict. Those stay
/// in the deployment's own section, and there is no key here that shadows one — an owner record layers over nothing.
/// </para>
/// <para>
/// Every switch is off when the block is absent, so an owner whose record states none of it is classified as nothing,
/// which is the same answer a deployment that configured nothing gives. That is what the marker on their row decides
/// rather than this type: an owner still read from configuration takes the deployment's section, and one whose document
/// has been written takes this, whichever of its settings the document happens to carry.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when an owner's document is read.")]
internal sealed class OwnerSpamClassificationOptions
{
    /// <summary>The property an owner's record holds this block under.</summary>
    internal const string RecordProperty = "SpamClassification";

    /// <summary>The path an owner's record holds the junk actions under, as a refusal names it.</summary>
    internal const string ActionsPath = $"{RecordProperty}:{nameof(Actions)}";

    /// <summary>Gets or sets whether this owner's mail is classified at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets whether the deployment's scanner is consulted after the deterministic stage.</summary>
    /// <remarks>
    /// Which scanner that is, and what one scan of a message may cost, stay the deployment's: this asks for the engine
    /// rather than choosing it. An owner who switches it on where the deployment registered none is answered by the
    /// deterministic stage alone, exactly as the deployment's own section is.
    /// </remarks>
    public bool UseScanner { get; set; }

    /// <summary>Gets or sets the folder aliases this owner's mail is classified over, or <see langword="null" /> to take each of their accounts' inbox.</summary>
    /// <remarks>
    /// Nullable so that leaving the key out and writing an empty list stay distinguishable, which an <c>IList</c>-typed
    /// property could not express. Each alias is resolved within this owner's own mail accounts, so one only somebody
    /// else's account carries reaches no mail at all — the same answer a folder this deployment does not serve gets.
    /// </remarks>
    public string[]? ScannedFolders { get; set; }

    /// <summary>Gets or sets the score at or above which a scanner's verdict is spam for this owner, or <see langword="null" /> to take the scanner's own.</summary>
    public double? ScannerThreshold { get; set; }

    /// <summary>Gets or sets what happens to mail a verdict calls this owner's junk.</summary>
    /// <remarks>
    /// Always present so that a record stating none of its keys still binds. Both of its switches are off, which is what
    /// keeps a classification derived data by default: a verdict is recorded and the mailbox is left exactly as it was.
    /// </remarks>
    public OwnerSpamActionOptions Actions { get; set; } = new();

    /// <summary>Finds everything about this block that would otherwise be discovered on the owner's own mail.</summary>
    /// <param name="accounts">The owner's own mail accounts, which are the only ones a folder may resolve within.</param>
    /// <returns>One result per refusal, each naming the setting that carries it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    internal IEnumerable<ValidationResult> FindRefusals(IReadOnlyCollection<DeclaredMailAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        return this.FindPostureRefusals().Concat(this.Actions.FindRefusals(this.Enabled, accounts));
    }

    private IEnumerable<ValidationResult> FindPostureRefusals()
    {
        if (this.UseScanner && !this.Enabled)
        {
            yield return new ValidationResult(
                $"The owner record asks for a scanner while {nameof(this.Enabled)} is false, and a scanner is only consulted where classification runs. Set it to true, or remove {nameof(this.UseScanner)}.",
                [nameof(this.UseScanner)]);
        }

        foreach (var alias in (this.ScannedFolders ?? [])
            .Where(static alias => !MailFolderAlias.TryCreate(alias, out _)))
        {
            yield return new ValidationResult(
                $"The owner record names scanned folder '{alias}', which is not a usable folder alias.",
                [nameof(this.ScannedFolders)]);
        }

        if (this.ScannerThreshold is { } threshold
            && (!double.IsFinite(threshold)
                || threshold < SpamClassificationOptions.SmallestThreshold
                || threshold > SpamClassificationOptions.LargestThreshold))
        {
            yield return new ValidationResult(
                $"The owner record declares a {nameof(this.ScannerThreshold)} of {threshold.ToString(CultureInfo.InvariantCulture)}, and this deployment permits a threshold between {SpamClassificationOptions.SmallestThreshold.ToString(CultureInfo.InvariantCulture)} and {SpamClassificationOptions.LargestThreshold.ToString(CultureInfo.InvariantCulture)}. A value outside that range either files every message or can never be reached.",
                [nameof(this.ScannerThreshold)]);
        }
    }
}
