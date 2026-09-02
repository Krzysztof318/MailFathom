// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.OAuth;

/// <summary>What a person has to be shown to complete a device-code authorization on another device.</summary>
/// <param name="UserCode">The short code the person types at the verification address.</param>
/// <param name="VerificationUri">The address the person opens in a browser.</param>
/// <param name="VerificationUriComplete">The same address with the code already embedded, or <see langword="null" /> when the provider issued none.</param>
/// <param name="ExpiresAt">When the code stops being redeemable.</param>
/// <remarks>
/// <para>
/// The prompt is reported rather than printed, so the flow stays free of a console it does not own and the command
/// decides how an operator sees it. Nothing here is a credential: the user code is single-use, bound to one pending
/// authorization, and useless without the person signing in.
/// </para>
/// <para>
/// It is reported through an <see cref="Action{T}" /> rather than an <see cref="IProgress{T}" />, because what the
/// report has to guarantee is an ordering: the person cannot act on a code they have not been shown, so it must reach
/// them before the flow starts waiting on their action. <see cref="IProgress{T}" /> promises the opposite — a report
/// may be delivered asynchronously — and <see cref="Progress{T}" />, the only implementation the platform ships,
/// exists to marshal onto a captured <see cref="SynchronizationContext" />. A console process has none, so the report
/// degrades to a thread-pool work item that races the rest of the sign-in and the writes it makes to the terminal.
/// </para>
/// </remarks>
public sealed record DeviceCodePrompt(
    string UserCode,
    Uri VerificationUri,
    Uri? VerificationUriComplete,
    DateTimeOffset ExpiresAt);
