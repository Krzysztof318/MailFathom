// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Answering;
using MailFathom.Host.Configuration.Embeddings;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Proves a reloaded chat declaration before it becomes the one new questions are answered through.</summary>
/// <remarks>
/// <para>
/// The sections it is judged against arrive as the values composition actually used rather than as settings to be read
/// again, for the reason the composed database command timeout does: what a candidate has to agree with is what the
/// running process was built from, and reading both sides afresh would compare a candidate against itself.
/// </para>
/// <para>
/// A candidate that fails leaves the previous declaration serving. That is what keeps a mistyped model, a bound outside
/// its range, or a repointed key that resolves to nothing from taking the answering capability offline — the operator's
/// next edit is the correction, and the process has to still be there to read it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this validator.")]
internal sealed class ChatSettingsReloadValidator(
    SecretConfigurationValidator secretValidator,
    ChatModelOptions composedSettings,
    EmbeddingOptions? declaredEmbeddings,
    MailAnsweringOptions declaredAnswering)
{
    /// <summary>Finds everything an operator must fix before a reloaded chat declaration can be published.</summary>
    /// <param name="candidate">The reloaded declaration.</param>
    /// <param name="cancellationToken">Cancels the secret resolution.</param>
    /// <returns>One message per unusable setting, empty when the candidate is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The credential is proven here and resolved again per request, on the same terms every other reloadable section's
    /// secrets are: a reference an operator repoints to nothing would otherwise publish cleanly and then refuse every
    /// question.
    /// </remarks>
    internal async Task<IReadOnlyList<string>> FindConfigurationErrorsAsync(
        ChatModelOptions candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var errors = new List<string>(
            await secretValidator.FindSecretReferenceErrorsAsync(
                ChatModelOptions.SectionName,
                candidate,
                null,
                cancellationToken));

        errors.AddRange(ChatDeclarationRules.FindDeclarationErrors(candidate, declaredEmbeddings, declaredAnswering));
        errors.AddRange(ChatDeclarationRules.FindChangesNeedingRestart(candidate, composedSettings));

        return errors;
    }
}
