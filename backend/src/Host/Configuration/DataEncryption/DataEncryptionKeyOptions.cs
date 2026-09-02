// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.DataEncryption;

/// <summary>One key of the deployment's data-encryption key ring.</summary>
/// <remarks>
/// <para>
/// A key entry is identified twice, and the two identities do different jobs. <see cref="KeyId" /> is written beside
/// every value this key seals, so the database holds it and it can never be changed once a single row references it.
/// The operator's own label is the required <c>Name</c> of <see cref="Material" />, which every secret block carries and
/// which every diagnostic, rotation instruction, and audit record names the key by. No third name is added here: an
/// entry holds exactly one material, so a second label would be a second name for the same object.
/// </para>
/// <para>
/// See <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md">ADR 0005</see> for the
/// key ring, the provisioning model, and why the material is base64 rather than raw bytes.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class DataEncryptionKeyOptions
{
    /// <summary>Gets or sets the identifier stored beside every value this key seals.</summary>
    /// <remarks>
    /// It is persisted, so it is chosen once and never edited: renaming it orphans every value that already carries the
    /// previous spelling. A date such as <c>2026-08</c> reads well because it says when the key entered service, which
    /// is the question an operator asks when deciding what to retire.
    /// </remarks>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Gets or sets the reference to the key material: base64 that decodes to exactly 32 bytes.</summary>
    /// <remarks>
    /// Generate it with <c>openssl rand -base64 32</c>. The two database passwords beside it in a Compose deployment are
    /// generated with <c>-base64 33</c>, which is right for them and wrong here, so the commands must never be copied
    /// from one another — startup refuses material of any other length rather than silently accepting a weaker key.
    /// </remarks>
    public ConfiguredSecret? Material { get; set; }
}
