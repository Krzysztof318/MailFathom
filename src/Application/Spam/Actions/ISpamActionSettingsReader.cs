// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Actions;

/// <summary>Answers what an operator asked to happen to mail a classification calls junk.</summary>
/// <remarks>
/// It is a port of its own rather than a second property on <see cref="ISpamClassificationSettingsReader" /> because the
/// two answer for different halves of the feature: that one decides whether a verdict is reached at all, and this one
/// decides whether anything is done about it. Keeping them apart is what lets the classifier stay a use case that writes
/// nothing but its own record — it never resolves this reader and cannot reach a mailbox through it.
/// </remarks>
public interface ISpamActionSettingsReader
{
    /// <summary>Gets what the operator decided, as it stands now.</summary>
    /// <remarks>
    /// Read per request rather than captured, so an operator switching filing on reaches the next verdict without a
    /// restart — and so one switching it off stops the next one.
    /// </remarks>
    SpamActionSettings Actions { get; }
}
