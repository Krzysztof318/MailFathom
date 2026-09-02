// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>The OpenAI endpoint a run generates through, once every value has been checked.</summary>
/// <param name="ApiKey">The credential the run presents to the endpoint, which never reaches an argument, a log line, or the process list.</param>
/// <param name="Model">The routed model name the generation is sent to.</param>
/// <param name="Endpoint">The OpenAI-compatible base address, or <see langword="null" /> for the provider's own default address.</param>
/// <remarks>
/// Only this type crosses into the generation layer, so nothing there has to remember which values were validated.
/// The key is carried as an ordinary string because it is read from a local file and handed straight to one
/// authentication construction; nothing here logs, serializes, or persists it, and <see cref="ToString" /> is what
/// keeps printing out of that list rather than the habits of the call sites.
/// </remarks>
internal sealed record AiProviderConfiguration(
    string ApiKey,
    string Model,
    Uri? Endpoint)
{
    /// <inheritdoc />
    /// <remarks>
    /// Written by hand because the synthesized one prints every member, <see cref="ApiKey" /> included — so an
    /// interpolation of the whole record, a future log line, or a debugger inspection would put a real credential
    /// somewhere nobody meant to. This is the same decision <see cref="SendingAccount.ToString" /> makes, for the
    /// same credential.
    /// </remarks>
    public override string ToString() =>
        $"{nameof(AiProviderConfiguration)} {{ {nameof(this.ApiKey)} = ***, {nameof(this.Model)} = {this.Model}, {nameof(this.Endpoint)} = {this.Endpoint} }}";
}
