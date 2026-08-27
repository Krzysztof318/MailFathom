// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Access;

/// <summary>Indicates that this deployment cannot settle which owners it serves, or who each of them is.</summary>
/// <remarks>
/// <para>
/// Raised rather than returned, because no caller above it can decide what it means. Who an owner is decides what every
/// synchronization run writes and what every admitted caller may read, so a deployment that could not settle the
/// question has no state a request can be answered in: it refuses to finish starting rather than serving a reader whose
/// bound was guessed.
/// </para>
/// <para>
/// The messages name the count, the label an operator wrote, and the remedy, and nothing else. An owner identity is a
/// generated identifier naming a person inside this deployment, so no message here carries one; a label is the
/// operator's own text for a row of their own file, in the same class as an account alias, and naming it is what makes
/// a refusal actionable.
/// </para>
/// </remarks>
public sealed class DeploymentMailOwnerUnresolvedException : MailFathomException
{
    private DeploymentMailOwnerUnresolvedException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.DeploymentMailOwnerUnresolved;

    /// <summary>Reports a deployment holding several owners while its mail accounts are declared in the section that names none.</summary>
    /// <returns>The failure to raise.</returns>
    public static DeploymentMailOwnerUnresolvedException SeveralOwners() => new(
        "This deployment holds more than one owner record while its mail accounts are declared in "
        + "MailSynchronization:Accounts, which names no owner, so a configured account cannot be attributed to one of "
        + "them. Declare each owner in the top-level Accounts collection, with the mail accounts they own, so every "
        + "mailbox says whose it is.");

    /// <summary>Reports a declaration that would give an owner the deployment already holds a different identifier.</summary>
    /// <param name="displayName">The label both the declaration and the stored row carry.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="displayName" /> is <see langword="null" />, empty, or white space.</exception>
    public static DeploymentMailOwnerUnresolvedException OwnerIdentifierChanged(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new(
            $"The owner labelled '{displayName}' is declared under an identifier this deployment does not hold them "
            + "under. That identifier is what every mail account, every stored message, and every job of theirs hangs "
            + "on, so starting under a new one would leave all of it belonging to nobody. Restore the identifier the "
            + "deployment holds, or declare the new one under a label of its own if it is meant to be a second person.");
    }

    /// <summary>Reports a label a declaration moves onto one owner while another owner the deployment holds still carries it.</summary>
    /// <param name="displayName">The label both owners would carry.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="displayName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// Separate from <see cref="OwnerIdentifierChanged" /> because the two are different mistakes: there an owner the
    /// deployment holds is declared under an identifier that is not theirs, here two owners it holds are both meant to
    /// carry one label at the moment the relabel runs. The second is reachable by a file whose end state is perfectly
    /// legal — two owners exchanging labels — which is why the message says how to reach that state rather than only
    /// what is wrong with the file.
    /// </remarks>
    public static DeploymentMailOwnerUnresolvedException OwnerLabelHeldByAnother(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new(
            $"The label '{displayName}' is declared for one owner while another owner this deployment holds still "
            + "carries it, and a label names one owner. Free the label first — relabel or remove the owner holding it "
            + "and start once — and declare it for its new owner afterwards; two owners exchanging labels is two "
            + "starts rather than one.");
    }

    /// <summary>Reports a deployment holding more owner records than it may serve.</summary>
    /// <param name="maximumOwners">The greatest number of owners one deployment serves.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumOwners" /> is not positive.</exception>
    public static DeploymentMailOwnerUnresolvedException TooManyOwners(int maximumOwners)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOwners);

        return new(
            $"This deployment holds more than the {maximumOwners} owner records one deployment serves. A roster that "
            + "long was generated rather than provisioned: check what wrote the settings_accounts table.");
    }

    /// <summary>Reports several owners on a deployment whose owner-facing surfaces cannot say which of them a caller acts for.</summary>
    /// <param name="authenticationDisabled">Whether an owner-facing surface admits callers without authenticating them at all.</param>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// Both halves are the same fact one step apart: an owner-facing surface acts for exactly one owner, and nothing
    /// this release admits a caller with names which. Authentication-free operation says so outright, and a configured
    /// credential says it too — it authenticates a caller and carries no owner. Which of the two it is decides what an
    /// operator does next, so the message says which.
    /// </remarks>
    public static DeploymentMailOwnerUnresolvedException SeveralOwnersOnAnOwnerFacingSurface(bool authenticationDisabled) => new(
        "This deployment serves more than one owner while an owner-facing surface — McpEndpoint or ClientEndpoint — is "
        + "enabled. Such a surface answers one person about their own mail, and "
        + (authenticationDisabled
            ? "it admits callers without authenticating them, so there is nothing for it to resolve an owner from. "
            : "a configured credential authenticates a caller without naming the owner they act for, so there is "
            + "nothing for it to resolve an owner from. ")
        + "Serve one owner while those surfaces are enabled, or disable them on a deployment that serves several.");

    /// <summary>Reports an owner whose own mail accounts carry a secret or a trust anchor this deployment cannot use.</summary>
    /// <param name="displayName">The label the owner is declared under.</param>
    /// <param name="refusals">The sentences naming each setting that must change, each already carrying its configuration path.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="displayName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusals" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Separate from the deployment section's own secret refusal because an owner's mailboxes are declared somewhere
    /// that section cannot reach, and an operator reading a path alone would not know whose mailbox it names. The
    /// refusals carry no material and no length, exactly as the ones raised over the deployment's own section do.
    /// </remarks>
    public static DeploymentMailOwnerUnresolvedException OwnerMailAccountsUnusable(
        string displayName,
        IReadOnlyList<string> refusals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(refusals);

        return new(
            $"The mail accounts of the owner labelled '{displayName}' carry a setting this deployment cannot use, so "
            + "they would have failed one connection at a time rather than the start: "
            + string.Join(" ", refusals));
    }

    /// <summary>Reports an owner whose own record could not be read as the settings it is meant to hold.</summary>
    /// <param name="displayName">The label the owner's row carries.</param>
    /// <param name="refusals">The sentences naming what must change in the record.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="displayName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusals" /> is <see langword="null" />.</exception>
    public static DeploymentMailOwnerUnresolvedException OwnerRecordUnusable(
        string displayName,
        IReadOnlyList<string> refusals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(refusals);

        return new(
            $"The record of the owner labelled '{displayName}' is not the settings an owner's document holds, and that "
            + "owner is served from it rather than from configuration: "
            + string.Join(" ", refusals));
    }
}
