// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.SensitiveContent;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>What one account asks for over the mail in it, within what the deployment provides.</summary>
/// <remarks>
/// <para>
/// It is content of that account's record rather than an overlay on the deployment's <c>SensitiveContent</c> section.
/// No value here shadows a deployment setting: the effective posture is the stricter of the two, so what this block can
/// do is add — switch on a scanner the deployment left off, and refuse more of the mail leaving this account.
/// </para>
/// <para>
/// It is written on the account rather than on each assigned user because the mail is one copy: a mailbox two people
/// are assigned is scanned once, under one posture, so that neither of them reads mail the other's rules judged.
/// </para>
/// <para>
/// What it cannot carry is the engine. The analyzer's address, the languages it is asked in, the confidence floor, the
/// analyzed ceiling, the per-scan budget, the process-wide concurrency, and the extraction backfill are one machine's
/// and one operator's, and are budgeted per host exactly as mail-server connections are. Nor can it carry the
/// categories either scanner looks for or the rules suppressed inside them: what a scanner detects is one answer for
/// the deployment, and this block decides only whether that answer is applied to this account's mail and what a finding
/// in a message leaving it does.
/// </para>
/// <para>
/// A record that says nothing at all is the ordinary case and costs nothing: the account reads the deployment's
/// posture, which is what every mailbox read before this block existed.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when a user's record is read.")]
internal sealed class MailAccountSensitiveContentOptions
{
    /// <summary>The property an account's record carries this block under, which is the deployment section's own name.</summary>
    /// <remarks>
    /// Named after the deployment's section deliberately, because somebody reading an account record beside the
    /// deployment's file should meet one vocabulary rather than two names for the switch on the same scanner. It is a
    /// block inside a document rather than a bound configuration section, which is why the constant is not named
    /// <c>SectionName</c>: that name is what the public-surface record discovers a configuration section by, and this
    /// block is reached at <c>MailAccounts:&lt;index&gt;:SensitiveContent</c> rather than at a root of its own.
    /// </remarks>
    public const string BlockName = "SensitiveContent";

    /// <summary>Gets what this account says about the scanner that looks for credentials, tokens, and keys.</summary>
    public MailAccountSensitiveContentScannerOptions Secrets { get; } = new();

    /// <summary>Gets what this account says about the scanner that looks for personal data.</summary>
    public MailAccountSensitiveContentScannerOptions Pii { get; } = new();

    /// <summary>Gets or sets which scanners stop a message leaving this account rather than being read for a placeholder.</summary>
    /// <remarks>
    /// <para>
    /// Read as this account's whole answer rather than as an addition to the deployment's, which is why a list that
    /// names fewer scanners than the deployment screens for is refused where it is written: a record somebody reads has
    /// to say what actually stops the mail. What is in force is still the union of the two, so an operator who widens
    /// the deployment's list afterwards widens this account's without waiting for its record to be written again.
    /// </para>
    /// <para>
    /// It is an array rather than a list for the reason the deployment's key is: <c>[]</c> has to be distinguishable
    /// from an absent key, and the binder leaves a list at its default instead of emptying it. Here <c>[]</c> is
    /// legitimate on a deployment that screens nothing and refused on one that screens anything, which is the same rule
    /// every other entry is judged by.
    /// </para>
    /// </remarks>
    public string[]? ScreenOutgoingMailFor { get; set; }

    /// <summary>Finds what this account says about one scanner.</summary>
    /// <param name="scanner">The switch to read.</param>
    /// <returns>What the record said, which may be nothing.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value names no switch.</exception>
    public MailAccountSensitiveContentScannerOptions For(SensitiveContentScannerKind scanner) => scanner switch
    {
        SensitiveContentScannerKind.Secrets => this.Secrets,
        SensitiveContentScannerKind.Pii => this.Pii,
        _ => throw new ArgumentOutOfRangeException(nameof(scanner), scanner, "The value names no configured scanner."),
    };
}
