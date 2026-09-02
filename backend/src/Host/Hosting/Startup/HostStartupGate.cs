// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Hosting.Startup;

/// <summary>A step the host completes before it is finished coming up.</summary>
/// <remarks>
/// <para>
/// Every step reaches a remote dependency and takes as long as that dependency does, which is the interval a startup
/// probe exists to cover. Under the builder MailFathom composes with, they run before the web host opens its listener,
/// so an orchestrator ordinarily sees a refused connection rather than an unhealthy probe during that interval and
/// counts it the same way. The probe reports the gates regardless, because that ordering is an implementation detail of
/// the framework's composition and not something the answer should depend on.
/// </para>
/// <para>
/// <c>2</c> is absent because the gate that held it — the personal-data analyzer — became a readiness check instead: a
/// sidecar that may come up after this process, and may stop answering long after it, is not a question a gate that
/// runs once can answer. The number is not reissued, for the reason no enum value here is reused, so a member added
/// later takes the next one after the highest rather than the gap.
/// </para>
/// </remarks>
internal enum HostStartupGate
{
    /// <summary>Every configured secret reference has been resolved and the material behind it proven usable.</summary>
    SecretConfiguration = 0,

    /// <summary>The database carries every migration this build defines, and its lexical index matches the configured text search configuration.</summary>
    DatabaseSchema = 1,

    /// <summary>The spam scanner answers and has named the corpus every classification will record.</summary>
    /// <remarks>Expected only where the scanner switch is on, which is the only state in which anything asks a scanner for a score.</remarks>
    SpamScanner = 3,

    /// <summary>Every owner this deployment serves has a record, and each of them is resolved to the source their mail accounts are read from.</summary>
    /// <remarks>Expected on every deployment, because every mail account belongs to one of those owners and every caller a mail-reading surface admits is composed for one of them.</remarks>
    ServedMailOwners = 4,
}
