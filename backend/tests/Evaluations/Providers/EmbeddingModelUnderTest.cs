// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.Evaluations.Providers;

/// <summary>One embedding model a run measures, where it is reached, and the width its vectors are cut to.</summary>
/// <param name="RoutedModelName">The name the endpoint routes the request to.</param>
/// <param name="Address">Where requests go, or <see langword="null" /> for the provider's own default.</param>
/// <param name="Dimension">The width asked for and cut to, or <see langword="null" /> for the model's own.</param>
internal sealed record EmbeddingModelUnderTest(string RoutedModelName, Uri? Address, int? Dimension)
{
    /// <summary>Gets how every passage and question is prepared before it is sent: the preparation a deployment declares when it states none.</summary>
    /// <remarks>
    /// No instruction, because a deployment's semantic search sends a question through the same preparation as a passage,
    /// so an instruction written for passages would be measured on questions it was never written for.
    /// </remarks>
    public static EmbeddingInputPreparation Preparation { get; } =
        EmbeddingInputPreparation.Create(inputCharacterLimit: 8000, passageInstruction: null, normalizesVector: true);

    /// <summary>Gets the name the model's results are filed and reported under.</summary>
    /// <remarks>The width is part of it, so the same model measured at two widths is two columns of the report rather than one overwritten.</remarks>
    public string ReportedName =>
        this.Dimension is { } dimension
            ? string.Create(CultureInfo.InvariantCulture, $"{this.RoutedModelName}@{dimension}")
            : this.RoutedModelName;
}
