// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.CodeCoverage;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>
/// Stands in for production code whose verification lives in the integration suite, so the marker can be asserted
/// against elements that actually carry it instead of against a boundary that happens to apply it today.
/// </summary>
/// <remarks>
/// The marker sits on the type and on a member of each kind the coverage collector can be pointed at, because the
/// collector drops elements individually rather than whole types. Nothing here runs; the type exists to be read
/// through reflection, which is why it is static.
/// </remarks>
[RequiresIntegrationCoverage]
internal static class IntegrationVerifiedSample
{
    /// <summary>Gets a value whose real behavior only a composed host would exercise.</summary>
    [RequiresIntegrationCoverage]
    public static bool IsConnected => false;

    /// <summary>Performs work that only a real dependency could prove.</summary>
    [RequiresIntegrationCoverage]
    public static void Connect()
    {
    }
}
