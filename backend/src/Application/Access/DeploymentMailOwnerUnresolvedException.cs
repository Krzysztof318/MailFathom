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
            + "carries it, and a label names one owner. Free the label first — relabel the owner holding it and start "
            + "once — and declare it for its new owner afterwards; two owners exchanging labels is two starts rather "
            + "than one. Removing them from the file frees nothing: an owner this deployment holds keeps their record "
            + "and their label whether or not a file declares them.");
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

    /// <summary>Reports a start that would leave the deployment holding more owner records than it may serve.</summary>
    /// <param name="maximumOwners">The greatest number of owners one deployment serves.</param>
    /// <param name="heldOwners">The owner records the deployment already holds.</param>
    /// <param name="newOwners">The declared owners it holds no record for, each of which this start would provision.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumOwners" /> is not positive.</exception>
    /// <remarks>
    /// Separate from <see cref="TooManyOwners" />, which reports a table that was already past the bound before this
    /// start read it. Here the file and the table are each within it and only their sum is not, which an operator acts
    /// on by removing owner records rather than by finding what wrote them — and refusing before the writes is what
    /// keeps this start from producing the roster the next start would refuse permanently.
    /// </remarks>
    public static DeploymentMailOwnerUnresolvedException RosterWouldExceedTheBound(
        int maximumOwners,
        int heldOwners,
        int newOwners)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOwners);

        return new(
            $"This deployment holds {heldOwners} owner records and declares {newOwners} owners it holds none for, "
            + $"which would leave it past the {maximumOwners} owner records one deployment serves. Nothing was "
            + "written: an owner the file no longer declares keeps their record, so remove the records this "
            + "deployment no longer serves before declaring more owners.");
    }

    /// <summary>Reports several owners on a deployment whose surfaces cannot say which of them an act is for.</summary>
    /// <param name="refusal">The sentence naming which surface cannot name an owner and what an operator changes about it.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusal" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// The sentence is composed by the reading that decides the question rather than here, because the same fact
    /// refuses two acts a start apart — a roster this start would serve, and an owner an administrator is provisioning
    /// into a deployment that is already running — and an operator correcting one is correcting the other.
    /// </remarks>
    public static DeploymentMailOwnerUnresolvedException SeveralOwnersOnAnOwnerFacingSurface(string refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refusal);

        return new(refusal);
    }

    /// <summary>Reports a request that names no owner reaching a deployment that serves several.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// The only member here raised while the process serves requests rather than while it starts. Every other one
    /// refuses a start; this one refuses one act, because a deployment serving several owners is a state a start now
    /// admits — the several-owner bound holds over the surfaces that serve a person their own mail, and the
    /// administrative surface is deliberately outside it. What is left is an administrative act reached by a
    /// credential naming nobody and asking about one person's contacts, mail accounts, or mailbox, which has no answer
    /// rather than a first one. It is classified so that a caller reads which failure it is instead of an unclassified
    /// fault, and so that the sentence names the credential that would have been answered.
    /// </remarks>
    public static DeploymentMailOwnerUnresolvedException NoSoleOwnerToActFor() => new(
        "This deployment serves more than one owner, so a request that names none has nobody to act for. The acts "
        + "that read or write one person's contacts, mail accounts, or mailbox are reached by a credential that names "
        + "the owner it acts for; grant one such credential per owner, and use the deployment-wide administrative "
        + "routes — which name the owner they act on — for everything an administrator does across the roster.");

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

    /// <summary>Reports a mail-account name more than one served owner would answer to.</summary>
    /// <param name="sharedNames">The names two owners of this roster both carry.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sharedNames" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sharedNames" /> names nothing.</exception>
    /// <remarks>
    /// The deployment-wide naming rule the declarations are already held to, asked again of the roster a start would
    /// actually serve — which is the only place a collision written into two owners' records while a process ran can
    /// be seen. Neither owner is named: the answer is about a name two people share, and which two they are is read
    /// from the roster rather than from a line that would outlive the collision.
    /// </remarks>
    public static DeploymentMailOwnerUnresolvedException MailAccountNameSharedByOwners(
        IReadOnlyList<string> sharedNames)
    {
        ArgumentNullException.ThrowIfNull(sharedNames);

        if (sharedNames.Count == 0)
        {
            throw new ArgumentException("A shared mail-account name is reported for at least one name.", nameof(sharedNames));
        }

        return new(
            $"More than one owner this deployment would serve names a mail account {string.Join(", ", sharedNames)}. "
            + "A mail account belongs to its owner, but this release resolves an account's settings by its identifier "
            + "alone, so a name two owners share would reach whichever of the two the lookup met first. Give each of "
            + "them a name no other owner uses, with 'mfctl owner account remove' and 'mfctl owner account add' for an "
            + "owner whose record is their own, and in the declaration for one a file supplies.");
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
