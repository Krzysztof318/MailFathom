// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.OAuth;

/// <summary>What a person has to be shown to complete a device-code authorization on another device.</summary>
/// <param name="UserCode">The short code the person types at the verification address.</param>
/// <param name="VerificationUri">The address the person opens in a browser.</param>
/// <param name="VerificationUriComplete">The same address with the code already embedded, or <see langword="null" /> when the provider issued none.</param>
/// <param name="ExpiresAt">When the code stops being redeemable.</param>
/// <remarks>
/// The prompt is reported rather than printed, so the flow stays free of a console it does not own and the command
/// decides how an operator sees it. Nothing here is a credential: the user code is single-use, bound to one pending
/// authorization, and useless without the person signing in.
/// </remarks>
public sealed record DeviceCodePrompt(
    string UserCode,
    Uri VerificationUri,
    Uri? VerificationUriComplete,
    DateTimeOffset ExpiresAt);
