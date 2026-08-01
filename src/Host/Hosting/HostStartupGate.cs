// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Host.Hosting;

/// <summary>A step the host completes before it is finished coming up.</summary>
/// <remarks>
/// Both steps reach a remote dependency and both take as long as that dependency does, which is the interval a startup
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
}
