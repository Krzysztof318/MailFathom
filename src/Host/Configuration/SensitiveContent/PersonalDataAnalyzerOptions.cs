// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Where the personal-data analyzer is, what language it is asked in, and how sure it must be.</summary>
/// <remarks>
/// <para>
/// A block of its own rather than two more keys under the <c>Pii</c> switch, because the switch's shape is deliberately the
/// same for both scanners: which of them runs in this process and which reaches a container beside it is not a distinction
/// an operator configures. What they do configure is the address of the container, and only this scanner has one.
/// </para>
/// <para>
/// It is read only when that switch is on. A deployment that never enabled personal-data scanning may leave an address here
/// or leave it out; neither is a failure, because nothing constructs a client for it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class PersonalDataAnalyzerOptions
{
    /// <summary>Gets or sets the analyzer's base address, as an absolute HTTP address.</summary>
    /// <remarks>
    /// <para>
    /// Required once the <c>Pii</c> switch is on, and refused at startup when it is absent, because a scanner that fails
    /// closed with nowhere to ask would refuse every read, derived write, and egress it guards.
    /// </para>
    /// <para>
    /// Expected to name the deployment's own analyzer. Scanning exists so that content is inspected before it leaves the
    /// trust boundary, so an address outside the deployment hands the mail to a third party in order to establish whether
    /// it may be handed to one. Nothing refuses it — an analyzer shared across a private network is a legitimate
    /// arrangement, and no rule about addresses distinguishes the two — and the documentation states what is given up.
    /// </para>
    /// <para>
    /// Bound as a string rather than as a <see cref="Uri" /> so that a malformed value fails as this section's own startup
    /// error naming the key, rather than as a binder format exception naming a type.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Bound from configuration, where a malformed value must surface as this section's startup failure naming the key rather than as a binder format exception.")]
    public string? Endpoint { get; set; }

    /// <summary>Gets or sets the two-letter code of the language every request states.</summary>
    /// <remarks>
    /// The analyzer selects a model by it and refuses a language it loaded none for, which is what the startup probe finds
    /// out. The default is the language the shipped analyzer image carries a model for; changing it means changing the
    /// analyzer's own configuration in the same edit.
    /// </remarks>
    public string Language { get; set; } = "en";

    /// <summary>Gets or sets how sure the analyzer must be before a finding is redacted, from 0 to 1.</summary>
    /// <remarks>
    /// <para>
    /// Redaction acts on every finding it is given whatever its confidence, so this floor is the only thing between a
    /// deployment and the analyzer's weakest guesses — and those are weak by design rather than by accident. Measured
    /// against the shipped image, a payment card number is also reported as a bank account number at 0.05 and an
    /// arbitrary run of characters as a driving licence at 0.01, so a deployment with no floor redacts a great deal of
    /// text nothing was actually found in.
    /// </para>
    /// <para>
    /// The default keeps every pattern the analyzer scores as a real match and drops that sub-0.1 layer. Raising it
    /// trades recall for readable text — above 0.4 an American passport number stops being found at all, because the
    /// analyzer scores a bare nine-digit run at 0.4 until surrounding words raise it — and lowering it to zero is the
    /// state described above. It is compared inclusively, as the analyzer compares it.
    /// </para>
    /// </remarks>
    public double MinimumConfidence { get; set; } = DefaultMinimumConfidence;

    /// <summary>The floor a deployment that states none receives.</summary>
    internal const double DefaultMinimumConfidence = 0.3;
}
