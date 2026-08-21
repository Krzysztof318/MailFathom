// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Facts;

namespace MailFathom.TestSupport;

/// <summary>Answers with a fixed extracted body text and counts how often it was asked for it.</summary>
/// <remarks>
/// <para>
/// The count is the whole point. The one fact whose resolution costs a read is the body text, so a test proving that
/// an unnamed fact costs nothing and that a fact named twice costs one read has to observe the reads rather than the
/// answers.
/// </para>
/// <para>
/// It is compiled into each test project that needs it from <c>backend/tests/shared/</c>, because the same claim is made on
/// both sides of the condition boundary: the fact resolver in <c>Application</c> is what caches a read, and the
/// compiled condition in <c>Infrastructure</c> is what decides whether a read is asked for at all.
/// </para>
/// </remarks>
/// <param name="bodyText">The extracted text every read answers with, or <see langword="null" /> for a message that has none.</param>
internal sealed class RecordingMailRuleBodyTextReader(string? bodyText = null) : IMailRuleBodyTextReader
{
    /// <summary>Gets how often the extracted body text has been read.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// Observes the token before answering, which is what lets a test prove that the cancellation an evaluation was
    /// given actually reaches the resolution of a fact rather than stopping at the expression around it.
    /// </remarks>
    public Task<string?> ReadBodyTextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.ReadCount++;

        return Task.FromResult(bodyText);
    }
}
