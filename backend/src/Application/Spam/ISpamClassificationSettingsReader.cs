// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam;

/// <summary>Answers what an operator decided about spam classification.</summary>
/// <remarks>
/// Read through a port for the reason every other operator decision is: the paths that obey it must not reach for a
/// settings type of the host's, and the value has to be re-read rather than captured so that a configuration reload
/// takes effect without a restart. Reading it does not set anything off — a reload changes what the next classification
/// runs with and never reclassifies what is already recorded.
/// </remarks>
public interface ISpamClassificationSettingsReader
{
    /// <summary>Gets the settings in force now.</summary>
    SpamClassificationSettings Settings { get; }
}
