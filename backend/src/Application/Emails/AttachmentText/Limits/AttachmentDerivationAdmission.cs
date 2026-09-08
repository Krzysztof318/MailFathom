// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText.Limits;

/// <summary>Where one user stands on one step of attachment reading, against both ceilings that bound them.</summary>
/// <param name="User">What the named user has consumed in this period and what their own ceiling admits.</param>
/// <param name="Deployment">What every user together has consumed and what the deployment's ceiling admits.</param>
/// <remarks>
/// The two halves are the same shape because they are the same question asked of two populations, and both are needed
/// at once: work is admitted only where both admit it, and a refusal is only actionable if it says which one refused.
/// Both share the period's instants, so paused work wakes at one roll-over whichever bound stopped it.
/// </remarks>
public sealed record AttachmentDerivationAdmission(
    AttachmentDerivationPeriod User,
    AttachmentDerivationPeriod Deployment)
{
    /// <summary>Gets which step both halves report on.</summary>
    public AttachmentDerivationStep Step => this.Deployment.Step;

    /// <summary>Gets which ceiling this period has reached, if either.</summary>
    public AttachmentDerivationBound ReachedBound => this.Deployment.IsExhausted
        ? AttachmentDerivationBound.Deployment
        : this.User.IsExhausted
            ? AttachmentDerivationBound.User
            : AttachmentDerivationBound.None;

    /// <summary>Gets whether this user's mail may be read under this step right now.</summary>
    public bool AdmitsWork => this.ReachedBound is AttachmentDerivationBound.None;

    /// <summary>Gets when the period rolls over, which is the instant paused work resumes at whichever bound stopped it.</summary>
    public DateTimeOffset EndsAt => this.Deployment.EndsAt;
}
