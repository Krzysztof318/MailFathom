// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration;

/// <summary>What the administrative endpoint reports about the caller of a request.</summary>
/// <param name="Service">The product answering, which is what tells a successful sign-in from having reached something else that returns JSON.</param>
/// <param name="Version">The running version.</param>
/// <param name="Credential">The name the deployment knows the presented credential by.</param>
internal sealed record AdminSession(
    [property: JsonPropertyName("service")] string? Service,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("credential")] string? Credential);
