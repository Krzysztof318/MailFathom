// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Facts;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Answers with a fixed extracted body text and counts how often it was asked for it.</summary>
/// <remarks>
/// The count is the whole point. The one fact that costs a read is the body text, so a test proving that an unnamed
/// fact costs nothing and that a fact named twice costs one read has to observe the reads rather than the answers.
/// </remarks>
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
