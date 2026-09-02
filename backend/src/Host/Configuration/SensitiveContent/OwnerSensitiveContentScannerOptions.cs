// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>What one owner says about one of the two scanners over their own mail.</summary>
/// <remarks>
/// <para>
/// The switch is deliberately nullable, and the three states are three different statements. Absent is the owner saying
/// nothing, which leaves the deployment's answer standing. <see langword="true" /> switches the scanner on for their
/// mail whether or not the deployment switched it on for everybody. <see langword="false" /> is the owner declining it,
/// which stands only where the deployment declined it too — a deployment that switched a scanner on carries the
/// obligation for every owner it holds mail for, so that write is refused rather than composed away.
/// </para>
/// <para>
/// A bound of <see langword="false" /> is worth writing even though it changes nothing today: it is what an owner's
/// record says about a scanner, and a record that could only ever say "on" would leave an owner unable to state that
/// they considered one and did not want it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when an owner's record is read.")]
internal sealed class OwnerSensitiveContentScannerOptions
{
    /// <summary>Gets or sets whether this scanner runs over this owner's mail, or nothing where they said nothing.</summary>
    public bool? Enabled { get; set; }
}
