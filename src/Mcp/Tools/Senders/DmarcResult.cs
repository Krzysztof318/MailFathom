// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Senders;

/// <summary>Reports what the trusted receiving server said about DMARC for an email, as the protocol spells it.</summary>
/// <remarks>
/// Read back from one header rather than evaluated here. MailFathom resolves no DNS and reads no published policy, so
/// every value below is the receiving server's statement and not this deployment's.
/// </remarks>
internal enum DmarcResult
{
    /// <summary>The trusted header reported no DMARC result at all.</summary>
    NotReported = 0,

    /// <summary>An authenticated domain lined up with the displayed one under the sender's published policy.</summary>
    Pass = 1,

    /// <summary>The displayed domain published a policy and the email did not satisfy it.</summary>
    Fail = 2,

    /// <summary>The evaluation ran and the displayed domain publishes no DMARC record, so its policy decided nothing.</summary>
    NoPolicyPublished = 3,

    /// <summary>The evaluation could not complete and may succeed if repeated.</summary>
    TemporaryError = 4,

    /// <summary>The evaluation could not complete and will not succeed if repeated.</summary>
    PermanentError = 5,
}
