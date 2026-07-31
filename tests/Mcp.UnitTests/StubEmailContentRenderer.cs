// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.EmailContent;

namespace MailMcp.Mcp.UnitTests;

/// <summary>Answers a render with one fixed outcome and records whether HTML was asked for.</summary>
/// <remarks>
/// The requested representation is what a test asserts against: the tool owns the flag a caller sets, and the value the
/// renderer received is the observable result of passing it through the use case.
/// </remarks>
internal sealed class StubEmailContentRenderer(EmailContentRenderingResult result) : IEmailContentRenderer
{
    /// <summary>Gets whether the last render was asked for the sanitized HTML representation.</summary>
    public bool? LastIncludeSanitizedHtml { get; private set; }

    /// <inheritdoc />
    public Task<EmailContentRenderingResult> RenderAsync(
        StoredEmailContent content,
        bool includeSanitizedHtml,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.LastIncludeSanitizedHtml = includeSanitizedHtml;

        return Task.FromResult(result);
    }
}
