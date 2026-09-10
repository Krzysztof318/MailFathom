// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Access;

/// <summary>Indicates that this deployment cannot settle which users it serves, or who each of them is.</summary>
/// <remarks>
/// <para>
/// Raised rather than returned, because no caller above it can decide what it means. Who a user is decides what every
/// synchronization run writes and what every admitted caller may read, so a deployment that could not settle the
/// question has no state a request can be answered in: it refuses to finish starting rather than serving a reader whose
/// bound was guessed.
/// </para>
/// <para>
/// The messages name the count, the label an operator wrote, and the remedy, and nothing else. A user identity is a
/// generated identifier naming a person inside this deployment, so no message here carries one; a label is the
/// operator's own text for a person this deployment holds, in the same class as an account alias, and naming it is
/// what makes a refusal actionable.
/// </para>
/// </remarks>
public sealed class DeploymentMailUserUnresolvedException : MailFathomException
{
    private DeploymentMailUserUnresolvedException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.DeploymentMailUserUnresolved;

    /// <summary>Reports a deployment holding more user records than it may serve.</summary>
    /// <param name="maximumUsers">The greatest number of users one deployment serves.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumUsers" /> is not positive.</exception>
    public static DeploymentMailUserUnresolvedException TooManyUsers(int maximumUsers)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUsers);

        return new(
            $"This deployment holds more than the {maximumUsers} user records one deployment serves. A roster that "
            + "long was generated rather than provisioned: check what wrote the settings_accounts table.");
    }

    /// <summary>Reports several users on a deployment whose surfaces cannot say which of them an act is for.</summary>
    /// <param name="refusal">The sentence naming which surface cannot name a user and what an operator changes about it.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusal" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// The sentence is composed by the reading that decides the question rather than here, because the same fact
    /// refuses two acts a start apart — a roster this start would serve, and a user an administrator is provisioning
    /// into a deployment that is already running — and an operator correcting one is correcting the other.
    /// </remarks>
    public static DeploymentMailUserUnresolvedException SeveralUsersOnAUserFacingSurface(string refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refusal);

        return new(refusal);
    }

    /// <summary>Reports a request that names no user reaching a deployment that serves several.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// The only member here raised while the process serves requests rather than while it starts. Every other one
    /// refuses a start; this one refuses one act, because a deployment serving several users is a state a start now
    /// admits — the several-user bound holds over the surfaces that serve a person their own mail, and the
    /// administrative surface is deliberately outside it. What is left is an administrative act reached by a
    /// credential naming nobody and asking about one person's contacts, mail accounts, or mailbox, which has no answer
    /// rather than a first one. It is classified so that a caller reads which failure it is instead of an unclassified
    /// fault, and so that the sentence names the credential that would have been answered.
    /// </remarks>
    public static DeploymentMailUserUnresolvedException NoSoleUserToActFor() => new(
        "This deployment serves more than one user, so a request that names none has nobody to act for. The acts "
        + "that read or write one person's contacts, mail accounts, or mailbox are reached by a credential that names "
        + "the user it acts for; grant one such credential per user, and use the deployment-wide administrative "
        + "routes — which name the user they act on — for everything an administrator does across the roster.");

    /// <summary>Reports a user whose own mail accounts carry a secret or a trust anchor this deployment cannot use.</summary>
    /// <param name="displayName">The label the user is recorded under.</param>
    /// <param name="refusals">The sentences naming each setting that must change, each already carrying its path within the record.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="displayName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusals" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Separate from the deployment section's own secret refusal because a user's mailboxes are in a record that
    /// section cannot reach, and an operator reading a path alone would not know whose mailbox it names. The
    /// refusals carry no material and no length, exactly as the ones raised over the deployment's own section do.
    /// </remarks>
    public static DeploymentMailUserUnresolvedException UserMailAccountsUnusable(
        string displayName,
        IReadOnlyList<string> refusals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(refusals);

        return new(
            $"The mail accounts of the user labelled '{displayName}' carry a setting this deployment cannot use, so "
            + "they would have failed one connection at a time rather than the start: "
            + string.Join(" ", refusals));
    }

    /// <summary>Reports a user whose own record could not be read as the settings it is meant to hold.</summary>
    /// <param name="displayName">The label the user's row carries.</param>
    /// <param name="refusals">The sentences naming what must change in the record.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="displayName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusals" /> is <see langword="null" />.</exception>
    public static DeploymentMailUserUnresolvedException UserRecordUnusable(
        string displayName,
        IReadOnlyList<string> refusals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(refusals);

        return new(
            $"The record of the user labelled '{displayName}' is not the settings a user's document holds, and that "
            + "user is served from it rather than from configuration: "
            + string.Join(" ", refusals));
    }
}
