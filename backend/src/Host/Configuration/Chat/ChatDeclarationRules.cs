// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Answering;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Configuration.Providers;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Holds every rule a chat declaration must satisfy, so composition and a reload judge one by the same reading.</summary>
/// <remarks>
/// <para>
/// The section is read twice in the life of a process: once while the host composes itself, and again for every
/// candidate a configuration reload produces. A second copy of the rules would drift into a value startup accepted and
/// a reload refused, or the reverse — and the reverse is the worse of the two, because it publishes a declaration
/// nothing proved.
/// </para>
/// <para>
/// Two of the rules span sections and neither options type can see both sides, which is why they are reached from here
/// rather than from <see cref="ChatModelOptions.Validate" />: the alias must not repeat one an embedding endpoint
/// declares, and the relevance filter must not name more candidates than a retrieval hands over.
/// </para>
/// </remarks>
internal static class ChatDeclarationRules
{
    /// <summary>Reports everything an operator must fix before a chat declaration can be used.</summary>
    /// <param name="candidate">The bound declaration, or <see langword="null" /> when the deployment wrote no section.</param>
    /// <param name="embeddings">The bound embedding declaration, or <see langword="null" /> when the deployment wrote no section.</param>
    /// <param name="answering">The bound answering ceilings, which supply the retrieval count the filter is judged against.</param>
    /// <returns>One message per rule the declaration breaks, empty when it is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="answering" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An absent section is a supported deployment rather than a failure, so it reports nothing at all — the capability
    /// is simply not offered, exactly as an absent embedding section serves lexical search.
    /// </remarks>
    public static IReadOnlyList<string> FindDeclarationErrors(
        ChatModelOptions? candidate,
        EmbeddingOptions? embeddings,
        MailAnsweringOptions answering)
    {
        ArgumentNullException.ThrowIfNull(answering);

        if (candidate is null)
        {
            return [];
        }

        var errors = new List<string>(FindSectionErrors(candidate));

        if (PassageRelevanceCandidateAgreement.FindUnreachableCandidateCount(candidate, answering) is { } unreachableCandidateCount)
        {
            errors.Add(PassageRelevanceCandidateAgreement.DescribeUnreachableCandidateCount(
                unreachableCandidateCount,
                answering.MaxPassagesPerRetrieval));
        }

        if (ProviderEndpointAliases.FindReusedAlias(embeddings, candidate) is { } reusedAlias)
        {
            errors.Add(ProviderEndpointAliases.DescribeReusedAlias(reusedAlias));
        }

        // Zero is a supported declaration on its own — it is what a text-only model carries — and it is only a mistake
        // beside the one feature that has nothing else to send. Refused here rather than left to the describer, because
        // what an operator would otherwise meet is ImageTooLarge stamped on every picture in the mailbox, which reads as
        // a property of the pictures rather than as the endpoint declaration it is.
        if (candidate.MaxRequestImageOctets == 0 && embeddings?.ImageDescription.Enabled is true)
        {
            errors.Add(
                $"{ChatModelOptions.SectionName}:{nameof(ChatModelOptions.MaxRequestImageOctets)} — a chat endpoint declared to carry no image cannot be the one describing image attachments. Either raise this above zero, or turn {EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.ImageDescription)}:{nameof(EmbeddingImageDescriptionOptions.Enabled)} off.");
        }

        return errors;
    }

    /// <summary>Reports what a reloaded declaration changed that composition already acted on.</summary>
    /// <param name="candidate">The reloaded declaration.</param>
    /// <param name="composed">The declaration the host was built from.</param>
    /// <returns>One message per change that cannot take effect, empty when the candidate moves nothing composition read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Both settings decide which services exist rather than what a request carries, and the container is built once.
    /// Publishing such a change would report it as adopted while the process went on offering exactly the capabilities
    /// it started with, which is worse than refusing it: an operator who turned answering on would watch the setting
    /// take and the tool go on reporting itself inactive.
    /// </para>
    /// <para>
    /// Refused rather than merely ignored, for the reason the composed database command timeout is refused: a rejected
    /// candidate is logged with the path an operator edits, and a silently dropped one is not.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> FindChangesNeedingRestart(ChatModelOptions candidate, ChatModelOptions composed)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(composed);

        var errors = new List<string>();

        if (candidate.IsConfigured != composed.IsConfigured)
        {
            errors.Add(
                $"{ChatModelOptions.SectionName}:{nameof(ChatModelOptions.Alias)} — whether this deployment declares a chat endpoint decides which services it registers, so declaring or removing one needs a restart rather than a configuration reload. Everything the endpoint itself says reloads.");
        }

        if (candidate.RelevanceFilter.Enabled != composed.RelevanceFilter.Enabled)
        {
            errors.Add(
                $"{ChatModelOptions.SectionName}:{nameof(ChatModelOptions.RelevanceFilter)}:{nameof(PassageRelevanceFilterOptions.Enabled)} — whether retrieval judges its candidates decides which retrieval is registered, so turning the pass on or off needs a restart rather than a configuration reload. The two numbers beside it reload.");
        }

        return errors;
    }

    /// <summary>Runs the section's own rules: the attribute bounds and everything <see cref="ChatModelOptions.Validate" /> reports.</summary>
    /// <remarks>
    /// Invoked rather than left to <c>ValidateDataAnnotations</c>, because the options framework raises a rejected
    /// reload on the thread that reported the configuration change, where the failure has nowhere to be reported and
    /// the candidate is dropped without a word. Running the same validator here is what turns a mistyped bound into a
    /// logged refusal beside every other reason a candidate was not adopted.
    /// <para>
    /// Each message is prefixed with the key it is about, because an attribute's own text names the property and not
    /// the setting an operator edits, and because that is the shape every other rejected reload is reported in.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> FindSectionErrors(ChatModelOptions candidate)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            candidate,
            new ValidationContext(candidate),
            results,
            validateAllProperties: true);

        return results
            .Where(result => !string.IsNullOrWhiteSpace(result.ErrorMessage))
            .Select(result => $"{DescribeConfigurationPath(result)} — {result.ErrorMessage}");
    }

    /// <summary>Names the configuration key a validation result is about, falling back to the section when it names no member.</summary>
    private static string DescribeConfigurationPath(ValidationResult result) =>
        result.MemberNames.FirstOrDefault() is { Length: > 0 } memberName
            ? $"{ChatModelOptions.SectionName}:{memberName}"
            : ChatModelOptions.SectionName;
}
