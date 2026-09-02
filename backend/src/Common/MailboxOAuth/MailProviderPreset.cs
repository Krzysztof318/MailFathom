// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.MailboxOAuth;

/// <summary>A mail provider's published OAuth endpoints and the scope its IMAP access needs.</summary>
/// <remarks>
/// <para>
/// The value is a closed enumeration rather than a C# <see langword="enum" /> because each member carries the four
/// addresses and the scope string that make it usable, and because its name is what an operator types on the command
/// line. A numeric member would name none of that.
/// </para>
/// <para>
/// A preset saves an operator from transcribing four URLs correctly, and it is not a provider integration: nothing
/// here reaches the running service, which authenticates over IMAP against whatever host its account configures.
/// Anything not covered by a preset is supplied endpoint by endpoint instead.
/// </para>
/// </remarks>
public readonly record struct MailProviderPreset
{
    private readonly string? presetName;

    private MailProviderPreset(
        string presetName,
        Uri authorizationEndpoint,
        Uri tokenEndpoint,
        Uri? deviceAuthorizationEndpoint,
        string scope,
        bool requiresClientSecret)
    {
        this.presetName = presetName;
        this.AuthorizationEndpoint = authorizationEndpoint;
        this.TokenEndpoint = tokenEndpoint;
        this.DeviceAuthorizationEndpoint = deviceAuthorizationEndpoint;
        this.Scope = scope;
        this.RequiresClientSecret = requiresClientSecret;
    }

    /// <summary>Gets the preset for Google accounts, which reach IMAP through the restricted <c>https://mail.google.com/</c> scope.</summary>
    /// <remarks>
    /// The device authorization endpoint is deliberately absent. Google operates one, but its documented allowed-scope
    /// list for that flow covers only OpenID Connect, Drive, and YouTube scopes, so a mail scope cannot be obtained
    /// through it and offering the option would produce a rejection the operator could not act on.
    /// </remarks>
    public static MailProviderPreset Google { get; } = new(
        "google",
        new Uri("https://accounts.google.com/o/oauth2/v2/auth"),
        new Uri("https://oauth2.googleapis.com/token"),
        deviceAuthorizationEndpoint: null,
        "https://mail.google.com/",
        requiresClientSecret: true);

    /// <summary>Gets the preset for Microsoft work, school, and personal accounts against Exchange Online.</summary>
    /// <remarks>
    /// <para>
    /// The endpoints target the <c>common</c> tenant, which admits both organizational and personal accounts. A
    /// deployment restricted to one tenant substitutes its identifier and supplies the three addresses explicitly.
    /// </para>
    /// <para>
    /// <c>offline_access</c> is part of the scope because Entra issues no refresh token without it, and a grant that
    /// authenticates once would strand the deployment at the first access-token expiry.
    /// </para>
    /// </remarks>
    public static MailProviderPreset Microsoft { get; } = new(
        "microsoft",
        new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/authorize"),
        new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/token"),
        new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/devicecode"),
        "https://outlook.office.com/IMAP.AccessAsUser.All https://outlook.office.com/SMTP.Send offline_access",
        requiresClientSecret: false);

    /// <summary>Gets every supported preset.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailProviderPreset> All { get; } = [Google, Microsoft];

    /// <summary>Gets whether this value names a preset rather than the unusable struct default.</summary>
    public bool IsSpecified => this.presetName is not null;

    /// <summary>Gets the authorization endpoint a person signs in at.</summary>
    public Uri AuthorizationEndpoint { get; }

    /// <summary>Gets the token endpoint codes and refresh tokens are exchanged at.</summary>
    public Uri TokenEndpoint { get; }

    /// <summary>Gets the device authorization endpoint, or <see langword="null" /> when the provider's device flow cannot issue mail scopes.</summary>
    public Uri? DeviceAuthorizationEndpoint { get; }

    /// <summary>Gets the space-delimited scope an IMAP mailbox needs at this provider.</summary>
    public string Scope { get; }

    /// <summary>Gets whether the provider rejects an authorization-code exchange that carries no client secret.</summary>
    public bool RequiresClientSecret { get; }

    /// <summary>Gets the name an operator types to select this preset.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a preset.</exception>
    public string PresetName => this.presetName
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a provider preset.");

    /// <summary>Parses an operator-supplied preset name, ignoring case and surrounding whitespace.</summary>
    /// <param name="presetName">The configured preset name.</param>
    /// <param name="preset">The parsed preset when the name is supported; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is a supported preset; otherwise <see langword="false" />.</returns>
    public static bool TryParsePresetName(string? presetName, out MailProviderPreset preset)
    {
        preset = default;
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return false;
        }

        var normalizedName = presetName.Trim();

        preset = All.FirstOrDefault(candidate => StringComparer.OrdinalIgnoreCase.Equals(candidate.PresetName, normalizedName));

        return preset.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.presetName ?? "(unspecified)";
}
