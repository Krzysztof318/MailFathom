// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Generation.AiContent;

namespace MailFathom.SyntheticMail.UnitTests.TestDoubles;

/// <summary>Answers a generation from a script, and records every question it was asked.</summary>
/// <remarks>
/// The seam the AI-mode tests are written against: a script that returns a fixed answer is what "the model is
/// deterministic for this test" means, and the recorded requests are what the distribution, the envelope, and the
/// reply-context assertions are read from. A script that throws is what a provider failure is read from.
/// </remarks>
internal sealed class ScriptedAiEmailContentSource : IAiEmailContentSource
{
    private readonly AiEmailContent? content;
    private readonly Exception? failure;

    /// <summary>The questions asked, in the order asked.</summary>
    public List<AiEmailContentRequest> Requests { get; } = [];

    /// <summary>Initializes a source that answers every request with one fixed content.</summary>
    /// <param name="content">The answer.</param>
    public ScriptedAiEmailContentSource(AiEmailContent content)
    {
        this.content = content;
    }

    /// <summary>Initializes a source that fails every request with one failure.</summary>
    /// <param name="failure">The failure the provider would have raised.</param>
    public ScriptedAiEmailContentSource(Exception failure)
    {
        this.failure = failure;
    }

    /// <inheritdoc />
    public Task<AiEmailContent> GenerateAsync(AiEmailContentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.Requests.Add(request);

        if (this.failure is { } raise)
        {
            return Task.FromException<AiEmailContent>(raise);
        }

        return Task.FromResult(this.content!);
    }
}
