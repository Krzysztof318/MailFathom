// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>The AI provider file exactly as it is written, before anything about it has been checked.</summary>
/// <remarks>
/// Every member is optional, because a half-written file is the case this type exists to represent: what turns it into
/// an <see cref="AiProviderConfiguration" /> is <see cref="SyntheticAiProviderFile" />, which is where a missing
/// value becomes a message naming the key to set rather than a null reference somewhere later.
/// </remarks>
internal sealed record AiProviderConfigurationDocument
{
    /// <summary>The credential the run presents to the endpoint.</summary>
    public string? ApiKey { get; init; }

    /// <summary>The routed model name the generation is sent to.</summary>
    public string? Model { get; init; }

    /// <summary>The OpenAI-compatible base address, when the provider's own default is not what the run reaches.</summary>
    public string? Endpoint { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Redacted for the reason <see cref="AiProviderConfiguration.ToString" /> is, one step earlier in the same
    /// pipeline: this is what holds the credential between parsing and validation, so it is what a message about a
    /// file that failed validation would be written from. The synthesized printer prints every member, which would put
    /// a real key into the one kind of output this type exists to produce.
    /// </remarks>
    public override string ToString() =>
        $"{nameof(AiProviderConfigurationDocument)} {{ {nameof(this.ApiKey)} = ***, {nameof(this.Model)} = {this.Model}, {nameof(this.Endpoint)} = {this.Endpoint} }}";
}
