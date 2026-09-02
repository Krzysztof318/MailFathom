// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>A next step somebody may take, with what it would change and why it is being suggested.</summary>
/// <remarks>
/// <para>
/// The block that offers rather than reports. It carries no authority of its own: it names a step from a closed set,
/// says what taking it would change, and says whether it must be confirmed — the client is what offers it and whatever
/// governs that step is what permits it.
/// </para>
/// <para>
/// Confirmation is stated by the producer and is not derived from the impact, because the two answer different
/// questions. An action that sends mail always needs confirming; an action that changes nothing may still need it where
/// the run is unsure the suggestion fits, and a contract that computed the flag would have no way to say so.
/// </para>
/// </remarks>
public sealed record SuggestedActionBlock : PresentationBlock
{
    /// <summary>Initializes a next step somebody may take.</summary>
    /// <param name="evidence">What the correspondence does for the suggestion.</param>
    /// <param name="action">Which step is suggested.</param>
    /// <param name="reason">Why it is being suggested.</param>
    /// <param name="impact">What taking it would change.</param>
    /// <param name="requiresConfirmation">Whether it must be confirmed before it is taken.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason" /> is the unspecified default, or when an action that sends mail is offered without confirmation.</exception>
    public SuggestedActionBlock(
        PresentationEvidence evidence,
        SuggestedActionKind action,
        PresentationText reason,
        SuggestedActionImpact impact,
        bool requiresConfirmation)
        : base(PresentationBlockType.SuggestedAction, evidence)
    {
        PresentationRequirement.Specified(reason, nameof(reason));

        if (impact is SuggestedActionImpact.SendsMail && !requiresConfirmation)
        {
            throw new ArgumentException(
                "An action that sends mail is confirmed before it is taken, because nothing here can recall it afterwards.",
                nameof(requiresConfirmation));
        }

        this.Action = action;
        this.Reason = reason;
        this.Impact = impact;
        this.RequiresConfirmation = requiresConfirmation;
    }

    /// <summary>Gets which step is suggested.</summary>
    public SuggestedActionKind Action { get; }

    /// <summary>Gets why it is being suggested.</summary>
    public PresentationText Reason { get; }

    /// <summary>Gets what taking it would change.</summary>
    public SuggestedActionImpact Impact { get; }

    /// <summary>Gets whether it must be confirmed before it is taken.</summary>
    public bool RequiresConfirmation { get; }
}
