// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Egress;

namespace MailFathom.TestSupport;

/// <summary>Builds the guard a consumer of an egress point is exercised against.</summary>
/// <remarks>
/// The inactive guard is here rather than on <see cref="ScanningSensitiveContentEgress" /> because it owns nothing and
/// is therefore written as an argument rather than as something a test holds: a deployment scanning nothing has no
/// redactor to release. A guard that does scan is built by that type instead, which holds the redactor it runs through.
/// </remarks>
internal static class SensitiveContentEgressGuards
{
    /// <summary>Builds the guard of a deployment nobody's mail is scanned for.</summary>
    /// <returns>A guard that returns every text it is handed and constructs no detector.</returns>
    internal static SensitiveContentEgressGuard Inactive() =>
        new(
            FixedSensitiveContentPostures.ScanningNothing(),
            new RecordingSensitiveContentEgressTelemetry(),
            TimeProvider.System);
}
