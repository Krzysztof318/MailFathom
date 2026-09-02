// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What the receiving server reported for DMARC, which is where an authenticated domain meets the displayed one.</summary>
/// <remarks>
/// <para>
/// The values are RFC 8601's registered <c>dmarc</c> results and nothing else. MailFathom evaluates no policy and
/// resolves no record: this is what the trusted header said, read back.
/// </para>
/// <para>
/// It is worth recording beside the authenticated identity because the two answer different questions. An identity says
/// somebody authenticated as a domain; a DMARC result says whether that domain is the one the message claims to be from,
/// which is the difference the whole verdict exists to make visible.
/// </para>
/// </remarks>
public enum DmarcOutcome
{
    /// <summary>The trusted header reported no DMARC result, which most servers that publish results still do not.</summary>
    NotReported = 0,

    /// <summary>An authenticated domain lined up with the <c>From</c> domain under the sender's published policy.</summary>
    Pass = 1,

    /// <summary>The <c>From</c> domain published a policy and the message did not satisfy it.</summary>
    Fail = 2,

    /// <summary>The <c>From</c> domain publishes no DMARC record, so its policy decided nothing.</summary>
    /// <remarks>
    /// RFC 8601 spells this result <c>none</c>. It is kept apart from <see cref="NotReported" /> because the two say
    /// opposite things about the receiving server: this one is a DMARC evaluation that ran and found no policy, and that
    /// one is an evaluation that never ran.
    /// </remarks>
    NoPolicyPublished = 3,

    /// <summary>The evaluation could not complete and may succeed if repeated, which is RFC 8601's <c>temperror</c>.</summary>
    TemporaryError = 4,

    /// <summary>The evaluation could not complete and will not succeed if repeated, which is RFC 8601's <c>permerror</c>.</summary>
    PermanentError = 5,
}
