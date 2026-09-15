// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Accounts;

/// <summary>What a deployment is asked when one account's custody is to change.</summary>
/// <param name="Account">The account, as the deployment's configuration names it.</param>
/// <param name="Custody">The custody asked for.</param>
internal sealed record MailAccountCustodySwitchRequest(
    [property: JsonPropertyName("account")] string Account,
    [property: JsonPropertyName("custody")] string Custody);

/// <summary>Which copy of one account's mailbox is the truth, and how far a switch under way has got.</summary>
/// <param name="Account">The account.</param>
/// <param name="Requested">The custody an administrator last asked for.</param>
/// <param name="Phase">Which copy is the truth at this moment.</param>
/// <param name="IsSwitchPending">Whether the account is still moving towards what was asked for.</param>
/// <param name="Drain">What the source still holds, counted.</param>
internal sealed record MailAccountCustodyState(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("requested")] string? Requested,
    [property: JsonPropertyName("phase")] string? Phase,
    [property: JsonPropertyName("isSwitchPending")] bool IsSwitchPending,
    [property: JsonPropertyName("drain")] MailAccountDrainStanding? Drain);

/// <summary>What one held account's source still holds, counted rather than listed.</summary>
/// <param name="AwaitingDrain">Messages whose occurrence still stands on the source.</param>
/// <param name="HeldBackAboveSizeLimit">Messages the source keeps because their content is above the size MailFathom stores.</param>
/// <param name="HeldBackAwaitingHeadroom">Messages the source keeps until the storage ceiling has headroom for them.</param>
/// <param name="AwaitingSourceRemoval">Messages erased locally whose source copy has still to be removed.</param>
internal sealed record MailAccountDrainStanding(
    [property: JsonPropertyName("awaitingDrain")] int AwaitingDrain,
    [property: JsonPropertyName("heldBackAboveSizeLimit")] int HeldBackAboveSizeLimit,
    [property: JsonPropertyName("heldBackAwaitingHeadroom")] int HeldBackAwaitingHeadroom,
    [property: JsonPropertyName("awaitingSourceRemoval")] int AwaitingSourceRemoval);

/// <summary>What asking for a custody did.</summary>
/// <param name="Account">The account.</param>
/// <param name="WasAccepted">Whether the request was recorded, which is false exactly when refusals are named.</param>
/// <param name="Requested">The custody the account is now asked to have.</param>
/// <param name="Phase">Which copy of the mailbox is the truth at this moment.</param>
/// <param name="Refusals">One sentence per reason the switch was refused.</param>
internal sealed record MailAccountCustodySwitchOutcome(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("wasAccepted")] bool WasAccepted,
    [property: JsonPropertyName("requested")] string? Requested,
    [property: JsonPropertyName("phase")] string? Phase,
    [property: JsonPropertyName("refusals")] IReadOnlyList<string>? Refusals);
