// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.EmailContent;

namespace MailFathom.Application.UnitTests;

/// <summary>Keeps the repair requests a read recorded, in the order it recorded them.</summary>
/// <remarks>
/// Hand-written rather than substituted because every test that uses it asserts on what was recorded and several assert
/// that nothing was, and a recorded list reports both without a matcher.
/// </remarks>
internal sealed class RecordingEmailContentRepairRequestStore : IEmailContentRepairRequestStore
{
    private readonly List<EmailContentRepairRequest> recorded = [];

    /// <summary>Gets the requests recorded so far.</summary>
    public IReadOnlyList<EmailContentRepairRequest> Recorded => this.recorded;

    /// <inheritdoc />
    public Task RecordAsync(EmailContentRepairRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.recorded.Add(request);

        return Task.CompletedTask;
    }
}
