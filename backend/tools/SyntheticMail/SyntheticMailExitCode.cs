// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail;

/// <summary>What the command reports to whatever ran it.</summary>
/// <remarks>
/// Two codes. A batch in which any message failed reports the failure code even though the rest were delivered,
/// because a script that fills a mailbox and then asserts against it has to know the mailbox is not the one the seed
/// describes.
/// </remarks>
internal static class SyntheticMailExitCode
{
    /// <summary>Every message the run generated was delivered.</summary>
    internal const int Success = 0;

    /// <summary>The run failed, or delivered less than it generated, and has already said so.</summary>
    internal const int Failure = 1;
}
