// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Answers a render with one fixed outcome and records the bounds it was given.</summary>
/// <remarks>
/// The bounds are what a test asserts against: the tool owns the flags a caller sets, and the values the renderer
/// received are the observable result of passing them through the use case.
/// </remarks>
internal sealed class StubEmailContentRenderer(EmailContentRenderingResult result) : IEmailContentRenderer
{
    /// <summary>Gets whether the last render was asked for the sanitized HTML representation.</summary>
    public bool? LastIncludeSanitizedHtml { get; private set; }

    /// <summary>Gets how many renders were asked for.</summary>
    public int RenderCount { get; private set; }

    /// <inheritdoc />
    public Task<EmailContentRenderingResult> RenderAsync(
        StoredEmailContent content,
        EmailContentRenderingBounds bounds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        cancellationToken.ThrowIfCancellationRequested();

        this.LastIncludeSanitizedHtml = bounds.IncludeSanitizedHtml;
        this.RenderCount++;

        return Task.FromResult(result);
    }
}
