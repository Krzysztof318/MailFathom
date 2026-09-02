// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Mutations.Destinations;

/// <summary>States what happened when what an author named as a destination was turned into a folder on the server.</summary>
/// <remarks>
/// The four refusals are separate because their remedies are: one is a configuration to write, one is a run to wait
/// for, one is a folder to create by hand or a path to correct, and one is a role to give to a single folder. Reporting
/// them as one reason would leave an operator reading <em>the destination did not resolve</em> for four different facts.
/// </remarks>
public enum MailboxDestinationOutcome
{
    /// <summary>The destination names a folder the account's server currently holds.</summary>
    Resolved = 0,

    /// <summary>No mapping of the account answers to that name, so the folder is one MailFathom knows nothing about.</summary>
    Unmapped = 1,

    /// <summary>The account mirrors the folder and no run has bound its alias to a remote folder yet.</summary>
    Unbound = 2,

    /// <summary>The server advertises no folder the mapping names, and none was created for it.</summary>
    NotAdvertised = 3,

    /// <summary>Several advertised folders carry the role the mapping names, so which one is meant is the operator's to state.</summary>
    Ambiguous = 4,
}

/// <summary>Carries the folder a destination resolved to, or the reason it resolved to none.</summary>
/// <remarks>
/// A destination that resolved to nothing is a result rather than an exception because it fails one action and no more.
/// The rest of the batch is unaffected, and the author reports the refusal where the operator reads what a rule did.
/// </remarks>
public sealed record MailboxDestinationResolution
{
    private MailboxDestinationResolution(MailboxDestinationOutcome outcome, MailboxDestination? destination)
    {
        this.Outcome = outcome;
        this.Destination = destination;
    }

    /// <summary>Gets what happened.</summary>
    public MailboxDestinationOutcome Outcome { get; }

    /// <summary>Gets the folder, which is present exactly when <see cref="Outcome" /> is <see cref="MailboxDestinationOutcome.Resolved" />.</summary>
    public MailboxDestination? Destination { get; }

    /// <summary>Reports a destination that names a folder the server holds.</summary>
    /// <param name="destination">The folder it names.</param>
    /// <returns>A resolved result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination" /> is <see langword="null" />.</exception>
    public static MailboxDestinationResolution Resolved(MailboxDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return new MailboxDestinationResolution(MailboxDestinationOutcome.Resolved, destination);
    }

    /// <summary>Reports a destination no mapping of the account answers to.</summary>
    /// <returns>An unmapped result.</returns>
    public static MailboxDestinationResolution Unmapped() =>
        new(MailboxDestinationOutcome.Unmapped, destination: null);

    /// <summary>Reports a mirrored folder nothing has bound to a remote folder yet.</summary>
    /// <returns>An unbound result.</returns>
    public static MailboxDestinationResolution Unbound() =>
        new(MailboxDestinationOutcome.Unbound, destination: null);

    /// <summary>Reports a mapping the server advertises no folder for.</summary>
    /// <returns>An unadvertised result.</returns>
    public static MailboxDestinationResolution NotAdvertised() =>
        new(MailboxDestinationOutcome.NotAdvertised, destination: null);

    /// <summary>Reports a role several advertised folders carry.</summary>
    /// <returns>An ambiguous result.</returns>
    public static MailboxDestinationResolution Ambiguous() =>
        new(MailboxDestinationOutcome.Ambiguous, destination: null);
}
