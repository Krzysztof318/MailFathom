// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Hosting.Startup;

/// <summary>A step the host completes before it is finished coming up.</summary>
/// <remarks>
/// Every step reaches a remote dependency and takes as long as that dependency does, which is the interval a startup
/// probe exists to cover. Under the builder MailFathom composes with, they run before the web host opens its listener,
/// so an orchestrator ordinarily sees a refused connection rather than an unhealthy probe during that interval and
/// counts it the same way. The probe reports the gates regardless, because that ordering is an implementation detail of
/// the framework's composition and not something the answer should depend on.
/// </remarks>
internal enum HostStartupGate
{
    /// <summary>Every configured secret reference has been resolved and the material behind it proven usable.</summary>
    SecretConfiguration = 0,

    /// <summary>The database carries every migration this build defines, and its lexical index matches the configured text search configuration.</summary>
    DatabaseSchema = 1,

    /// <summary>The personal-data analyzer answers, in the configured language, for every category the scanner was switched on for.</summary>
    /// <remarks>The one gate a deployment may not run at all: it is expected only where the <c>Pii</c> switch is on, which is the only state in which anything asks the analyzer a question.</remarks>
    PersonalDataAnalyzer = 2,

    /// <summary>The spam scanner answers and has named the corpus every classification will record.</summary>
    /// <remarks>Expected only where the scanner switch is on, which is the only state in which anything asks a scanner for a score.</remarks>
    SpamScanner = 3,
}
