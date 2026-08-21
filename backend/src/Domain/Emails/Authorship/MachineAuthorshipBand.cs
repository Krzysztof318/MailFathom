// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authorship;

/// <summary>How strongly a message's own text reads as machine written.</summary>
/// <remarks>
/// <para>
/// A coarse reading of the likelihood beside it, and the value a reader is expected to branch on. The number is what
/// makes two messages comparable; the band is what makes one message legible without knowing where the thresholds sit,
/// which is the whole of what a listing row has room to say.
/// </para>
/// <para>
/// It is an observation about the text and never a finding against the message or its sender. A great deal of ordinary
/// correspondence is drafted with a text generator by people who mean every word of it, so nothing in this system
/// treats any of these values as a warning, and nothing acts on one.
/// </para>
/// </remarks>
public enum MachineAuthorshipBand
{
    /// <summary>Nothing read the message's text, so nothing is claimed either way.</summary>
    /// <remarks>
    /// It covers three ordinary states and is deliberately not separated into three: the deployment does not assess
    /// authorship, the message's body yielded no words to read, and the message was stored before this deployment
    /// assessed anything. All three are the absence of a reading rather than a reading that found nothing, which is
    /// what <see cref="Unlikely" /> is.
    /// </remarks>
    NotAssessed = 0,

    /// <summary>The text was read and carries little or nothing of what machine-written text carries.</summary>
    Unlikely = 1,

    /// <summary>The text carries some of it, in a combination a person writing by hand also reaches.</summary>
    Possible = 2,

    /// <summary>The text carries enough of it, or one thing strong enough on its own, that a person typing it is the less likely reading.</summary>
    Likely = 3,
}
