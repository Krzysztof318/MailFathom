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
/// <param name="Restore">What the mailbox still owes the source, counted, with the appends an operator has to settle.</param>
internal sealed record MailAccountCustodyState(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("requested")] string? Requested,
    [property: JsonPropertyName("phase")] string? Phase,
    [property: JsonPropertyName("isSwitchPending")] bool IsSwitchPending,
    [property: JsonPropertyName("drain")] MailAccountDrainStanding? Drain,
    [property: JsonPropertyName("restore")] MailAccountRestoreStanding? Restore);

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

/// <summary>What one restoring account's mailbox still owes its source, counted rather than listed.</summary>
/// <param name="AwaitingAppend">Messages the source no longer holds that have still to be put back.</param>
/// <param name="AwaitingStateWrite">Messages whose stored state has still to be written onto the occurrence they keep.</param>
/// <param name="UnansweredAppends">Appends whose answer never came back, each of which holds the account in Restoring.</param>
/// <param name="AwaitingConfirmation">Appends the source answered in full whose occurrence the next run has still to write.</param>
/// <param name="Unanswered">The unanswered appends, named so an operator can settle them one at a time.</param>
internal sealed record MailAccountRestoreStanding(
    [property: JsonPropertyName("awaitingAppend")] int AwaitingAppend,
    [property: JsonPropertyName("awaitingStateWrite")] int AwaitingStateWrite,
    [property: JsonPropertyName("unansweredAppends")] int UnansweredAppends,
    [property: JsonPropertyName("awaitingConfirmation")] int AwaitingConfirmation,
    [property: JsonPropertyName("unanswered")] IReadOnlyList<MailAccountUnansweredAppend>? Unanswered);

/// <summary>One append the restore issued whose answer never came back.</summary>
/// <param name="Record">What the settling command names the record by.</param>
/// <param name="Folder">MailFathom's own name for the folder the copy was appended into.</param>
/// <param name="IssuedAt">When the command went out.</param>
internal sealed record MailAccountUnansweredAppend(
    [property: JsonPropertyName("record")] Guid Record,
    [property: JsonPropertyName("folder")] string? Folder,
    [property: JsonPropertyName("issuedAt")] DateTimeOffset IssuedAt);

/// <summary>What an operator found in the folder one unanswered restore append was issued against.</summary>
/// <param name="Account">The account, as the deployment's configuration names it.</param>
/// <param name="Record">The record being settled.</param>
/// <param name="SourceHoldsTheCopy">Whether the folder holds the copy the append may have put there.</param>
internal sealed record MailAccountRestoreSettlementRequest(
    [property: JsonPropertyName("account")] string Account,
    [property: JsonPropertyName("record")] Guid Record,
    [property: JsonPropertyName("sourceHoldsTheCopy")] bool SourceHoldsTheCopy);

/// <summary>What settling one unanswered restore append did.</summary>
/// <param name="Account">The account.</param>
/// <param name="Record">The record named.</param>
/// <param name="WasSettled">Whether the record was still standing, which is false where somebody had already settled it.</param>
internal sealed record MailAccountRestoreSettlementOutcome(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("record")] Guid Record,
    [property: JsonPropertyName("wasSettled")] bool WasSettled);

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
