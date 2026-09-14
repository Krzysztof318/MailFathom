// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>What one account says about one of the two scanners over the mail in it.</summary>
/// <remarks>
/// <para>
/// The switch is deliberately nullable, and the three states are three different statements. Absent is the record
/// saying nothing, which leaves the deployment's answer standing. <see langword="true" /> switches the scanner on for
/// this account's mail whether or not the deployment switched it on for every mailbox. <see langword="false" /> is the
/// account declining it, which stands only where the deployment declined it too — a deployment that switched a scanner
/// on carries the obligation for every mailbox it holds, so that write is refused rather than composed away.
/// </para>
/// <para>
/// A bound of <see langword="false" /> is worth writing even though it changes nothing today: it is what an account's
/// record says about a scanner, and a record that could only ever say "on" would leave nobody able to state that a
/// scanner was considered and not wanted.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when a user's record is read.")]
internal sealed class MailAccountSensitiveContentScannerOptions
{
    /// <summary>Gets or sets whether this scanner runs over this account's mail, or nothing where the record said nothing.</summary>
    public bool? Enabled { get; set; }
}
