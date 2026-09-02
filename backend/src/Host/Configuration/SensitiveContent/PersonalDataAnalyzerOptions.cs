// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    /// <summary>Gets the two-letter codes of the languages the analyzer is asked in.</summary>
    /// <remarks>
    /// <para>
    /// A set for the whole deployment, with no per-account, per-folder, or per-message language and no detection. A mailbox
    /// is mixed by nature, and the analyzer selects a model by language and registers a recognizer against a language, so
    /// what a category can find is decided here before it is decided by anything else in this section. One scan asks once
    /// per entry and merges what came back, which makes every entry another request inside the one budget
    /// <c>SensitiveContent:ScanTimeout</c> allows.
    /// </para>
    /// <para>
    /// The order is not read: the set is deduplicated and ordered before it reaches anything, so two deployments that
    /// named the same languages carry the same derivation stamp whichever way round they wrote them. The default is what
    /// the shipped analyzer image carries; naming another code means an analyzer image built for that language in the same
    /// edit, because a model is installed while such an image is built rather than while it starts.
    /// </para>
    /// <para>
    /// Empty rather than defaulted here, as every list in this section is: the binder adds to a bound collection instead
    /// of replacing it, so a default written into this property would be a language an operator could name others beside
    /// but never remove. <see cref="SensitiveContentPlanMapper" /> is where a list nobody wrote becomes
    /// <see cref="DefaultLanguage" />.
    /// </para>
    /// </remarks>
    public IList<string> Languages { get; } = [];

    /// <summary>Gets or sets how sure the analyzer must be before a finding is redacted, from 0 to 1.</summary>
    /// <remarks>
    /// <para>
    /// Redaction acts on every finding it is given whatever its confidence, so this floor is the only thing between a
    /// deployment and the analyzer's weakest guesses — and those are weak by design rather than by accident. Measured
    /// against the shipped image, an eight-digit build number is reported as a bank account number at 0.05 and as a
    /// driving licence at 0.01, and a contract reference of one letter and seven digits as a driving licence at 0.3, so a
    /// deployment with no floor redacts a great deal of text nothing was actually found in.
    /// </para>
    /// <para>
    /// The default is the one value that drops every measured false positive while leaving every category detectable, and
    /// both halves of that are decided by the analyzer rather than by taste. Below it sit the 0.3 pattern above and a
    /// nine-digit passport number that a second recognizer also reads as a national identifier at 0.3; at it sit that
    /// passport number's own 0.4 and a bank routing number's 0.4, so raising the floor at all stops two of the five
    /// default categories from being found. It is compared inclusively, as the analyzer compares it.
    /// </para>
    /// </remarks>
    public double MinimumConfidence { get; set; } = DefaultMinimumConfidence;

    /// <summary>The floor a deployment that states none receives.</summary>
    internal const double DefaultMinimumConfidence = 0.4;

    /// <summary>The language a deployment that names none is asked in, which is the one the shipped analyzer image serves.</summary>
    internal const string DefaultLanguage = "en";
}
