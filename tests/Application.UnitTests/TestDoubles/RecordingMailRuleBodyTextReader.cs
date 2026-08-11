// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Facts;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers with a fixed extracted body text and counts how often it was asked for it.</summary>
internal sealed class RecordingMailRuleBodyTextReader(string? bodyText = null) : IMailRuleBodyTextReader
{
    /// <summary>Gets how often the extracted body text has been read.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc />
    public Task<string?> ReadBodyTextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.ReadCount++;

        return Task.FromResult(bodyText);
    }
}
