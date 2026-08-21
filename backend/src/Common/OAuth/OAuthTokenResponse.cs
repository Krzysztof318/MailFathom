// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MailFathom.Common.OAuth;

/// <summary>The subset of an RFC 6749 token endpoint response MailFathom reads.</summary>
/// <param name="AccessToken">The issued bearer token.</param>
/// <param name="ExpiresInSeconds">The lifetime the authorization server stated, or <see langword="null" /> when it stated none.</param>
/// <param name="Error">The error code from a rejected request, absent from a successful one.</param>
/// <param name="RefreshToken">The long-lived token an interactive authorization returns, absent from an ordinary refresh at run time.</param>
/// <remarks>
/// <para>
/// A success and a failure are one type because RFC 6749 returns them from the same endpoint in the same media type,
/// and because which one arrived is decided by the presence of a member rather than by the status code alone —
/// authorization servers exist that answer a rejected grant with <c>200</c>.
/// </para>
/// <para>
/// <c>error_description</c> and <c>error_uri</c> are deliberately unmapped. Both are free text an authorization server
/// may populate with the request it rejected, so reading them would risk copying a client secret into a log through a
/// field nobody controls.
/// </para>
/// </remarks>
public sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("expires_in")] int? ExpiresInSeconds,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken = null)
{
    /// <inheritdoc />
    /// <remarks>Redacted by construction, because the successful shape carries a bearer token.</remarks>
    public override string ToString() => "***";
}

/// <summary>The subset of an RFC 8628 device authorization response MailFathom reads.</summary>
/// <param name="DeviceCode">The code the token request polls with.</param>
/// <param name="UserCode">The short code a person types at the verification address.</param>
/// <param name="VerificationUri">The address a person opens on a device that has a browser.</param>
/// <param name="VerificationUriComplete">The same address with the user code embedded, which providers may omit.</param>
/// <param name="ExpiresInSeconds">How long the device code stays pollable.</param>
/// <param name="IntervalSeconds">The minimum seconds between polls, defaulting to the RFC's 5 when absent.</param>
/// <param name="Error">The error code from a rejected request, absent from a successful one.</param>
[SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "The value is a JSON field read verbatim from an authorization server this process does not own. Binding it as a Uri would move a malformed value's failure inside deserialization, where it arrives as a format exception rather than as the sanitized authorization failure the caller reports.")]
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The value is a JSON field read verbatim from an authorization server this process does not own. Binding it as a Uri would move a malformed value's failure inside deserialization, where it arrives as a format exception rather than as the sanitized authorization failure the caller reports.")]
public sealed record OAuthDeviceAuthorizationResponse(
    [property: JsonPropertyName("device_code")] string? DeviceCode,
    [property: JsonPropertyName("user_code")] string? UserCode,
    [property: JsonPropertyName("verification_uri")] string? VerificationUri,
    [property: JsonPropertyName("verification_uri_complete")] string? VerificationUriComplete,
    [property: JsonPropertyName("expires_in")] int? ExpiresInSeconds,
    [property: JsonPropertyName("interval")] int? IntervalSeconds,
    [property: JsonPropertyName("error")] string? Error)
{
    /// <inheritdoc />
    /// <remarks>Redacted by construction, because the device code authorizes a token request.</remarks>
    public override string ToString() => "***";
}

/// <summary>Serializes the OAuth endpoint responses without reflection.</summary>
[JsonSerializable(typeof(OAuthTokenResponse))]
[JsonSerializable(typeof(OAuthDeviceAuthorizationResponse))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
public sealed partial class OAuthJsonContext : JsonSerializerContext;
