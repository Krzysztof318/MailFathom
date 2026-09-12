// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Names which declared model a capability runs on, and which one answers where that model could not.</summary>
/// <remarks>
/// <para>
/// Two aliases and nothing else, which is what separates a reference from a declaration: everything about how the model
/// is reached — its address, its credential, its parameters, its bounds — is written once in the block that declares it,
/// and a capability names it. Two capabilities routed to one model therefore share one declaration rather than two
/// copies of it that can drift.
/// </para>
/// <para>
/// The fallback lives here rather than on the model for the same reason. Which model stands behind another is a routing
/// decision belonging to whoever chose the first one, so a model declaration carries no fallback of its own — which is
/// also what bounds a chain at two and makes a cycle unwritable.
/// </para>
/// </remarks>
internal sealed class ChatModelReferenceOptions
{
    /// <summary>Gets or sets the alias of the model this runs on, empty to take whatever the deployment declared as its main model.</summary>
    /// <remarks>
    /// Empty is a supported declaration rather than an omission everywhere but <c>Chat:MainModel</c> itself: a
    /// capability that named no model of its own runs on the one the deployment answers questions with, which is what
    /// keeps a single-model deployment to one alias written once.
    /// </remarks>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Gets or sets the alias of the model a call this one could not answer is attempted against, empty to attempt none.</summary>
    public string Fallback { get; set; } = string.Empty;

    /// <summary>Gets whether this reference names a model at all.</summary>
    public bool NamesModel => this.Alias.Trim().Length > 0;

    /// <summary>Reports everything an operator must fix before this reference could be resolved.</summary>
    /// <param name="referenceDescription">How a message names this reference, already carrying the key an operator edits.</param>
    /// <param name="declaredAliases">Every alias the section declares a model under.</param>
    /// <returns>One result per rule the reference breaks, empty when it is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaredAliases" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A reference naming nothing reports nothing, because the caller decides whether an unwritten one is a default or a
    /// refusal — it is the first for a capability and the second for the main model, and only the section knows which
    /// one it is holding.
    /// </remarks>
    public IEnumerable<ValidationResult> FindConfigurationErrors(
        string referenceDescription,
        IReadOnlyCollection<string> declaredAliases)
    {
        ArgumentNullException.ThrowIfNull(declaredAliases);

        var alias = this.Alias.Trim();
        var fallback = this.Fallback.Trim();

        if (alias.Length > 0 && !Names(declaredAliases, alias))
        {
            yield return new ValidationResult(
                $"{referenceDescription} names the model '{alias}', which no block under {ChatModelOptions.SectionName}:{nameof(ChatModelOptions.Models)} declares.",
                [nameof(this.Alias)]);
        }

        if (fallback.Length == 0)
        {
            yield break;
        }

        if (alias.Length == 0)
        {
            yield return new ValidationResult(
                $"{referenceDescription} names a Fallback without naming the model it stands behind, so nothing says what would have to fail first.",
                [nameof(this.Fallback)]);

            yield break;
        }

        if (!Names(declaredAliases, fallback))
        {
            yield return new ValidationResult(
                $"{referenceDescription} names the fallback model '{fallback}', which no block under {ChatModelOptions.SectionName}:{nameof(ChatModelOptions.Models)} declares.",
                [nameof(this.Fallback)]);

            yield break;
        }

        if (string.Equals(alias, fallback, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                $"{referenceDescription} names '{fallback}' as its own fallback, so a second attempt would reach the endpoint that had just failed.",
                [nameof(this.Fallback)]);
        }
    }

    private static bool Names(IReadOnlyCollection<string> declaredAliases, string alias) =>
        declaredAliases.Contains(alias, StringComparer.OrdinalIgnoreCase);
}
