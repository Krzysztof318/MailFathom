// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.AiProviders;

/// <summary>Records what one call to an AI provider established about it.</summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IAiProviderHealthReader" /> because the two have no consumer in common. A provider adapter
/// only ever writes and must not be able to consult the state it is producing; a capability gate and a health check only
/// ever read and have no business declaring a provider healthy.
/// </para>
/// <para>
/// Every method is safe to call from any number of concurrent requests and none of them blocks: recording is on the
/// path of a real provider call, so it costs what a field assignment costs and nothing more.
/// </para>
/// </remarks>
public interface IAiProviderHealthRecorder
{
    /// <summary>Records that a call to the provider produced an answer.</summary>
    /// <param name="role">Which provider answered.</param>
    void RecordServed(AiProviderRole role);

    /// <summary>Records that a call failed for a reason a later attempt may not meet.</summary>
    /// <param name="role">Which provider failed.</param>
    void RecordUnavailable(AiProviderRole role);

    /// <summary>Records that a call failed for a reason no later attempt changes until somebody acts.</summary>
    /// <param name="role">Which provider failed.</param>
    void RecordMisconfigured(AiProviderRole role);
}
