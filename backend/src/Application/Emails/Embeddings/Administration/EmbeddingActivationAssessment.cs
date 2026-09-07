// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Administration;
using MailFathom.Application.Emails.AttachmentText.Limits;
using MailFathom.Application.Emails.Embeddings.Limits;

namespace MailFathom.Application.Emails.Embeddings.Administration;

/// <summary>What an activation would do, what it would cost, and whether the deployment's budget admits it.</summary>
/// <remarks>
/// The whole of what an operator is shown before they agree to spend, in one value, because the three parts are only
/// meaningful together: a number of passages says nothing without the ceiling it is weighed against, and a ceiling says
/// nothing without what the run would send.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// makes seeing this the condition of activating at all.
/// </remarks>
/// <param name="Declared">The geometry configuration declares, which is what would be activated.</param>
/// <param name="Forecast">What activating it would do.</param>
/// <param name="Estimate">What that would cost, counted over the passages the run would send.</param>
/// <param name="Period">Where the deployment's budget period stands, which is what the estimate is weighed against.</param>
/// <param name="AttachmentDerivation">
/// What reading the attachments of the mail already stored would cost, and where their own two periods stand. Present
/// on every assessment and reporting nothing outstanding on a deployment that reads no attachment, so an operator sees
/// one page rather than learning about a second bill after agreeing to the first.
/// </param>
public sealed record EmbeddingActivationAssessment(
    EmbeddingProfileIdentity Declared,
    EmbeddingActivationForecast Forecast,
    EmbeddingWorkload Estimate,
    EmbeddingSpendPeriod Period,
    AttachmentDerivationStatus AttachmentDerivation)
{
    /// <summary>Gets whether the declared ceiling refuses this activation outright.</summary>
    /// <remarks>
    /// <para>
    /// Weighed against the ceiling one period admits rather than against what the current period has left, because the
    /// question is whether the deployment ever agreed to a spend of this size. Comparing against the remainder would
    /// make the same activation succeed in the morning and be refused in the afternoon, which is a schedule rather than
    /// a budget — and ADR 0006 chose the refusing ceiling over the pacing one for exactly that reason.
    /// </para>
    /// <para>
    /// Only a run that would start applies. Resuming a reindex, re-activating what already serves, and colliding with a
    /// different reindex each spend nothing this command decided on, so refusing them on a ceiling would refuse a
    /// deployment the right to read its own state.
    /// </para>
    /// </remarks>
    public bool ExceedsSpendCeiling =>
        this.Forecast == EmbeddingActivationForecast.WouldStartReindex
        && this.Period.CeilingInputCharacterCount is { } ceiling
        && this.Estimate.OutstandingCharacterCount > ceiling;

    /// <summary>Gets whether reading the attachments this deployment holds is a spend it never agreed to.</summary>
    /// <remarks>
    /// <para>
    /// The same question as <see cref="ExceedsSpendCeiling" />, asked of the two ceilings that bound reading rather than
    /// sending, and weighed the same way and for the same reason: against what one period admits rather than against
    /// what the current one has left, because what is being decided is whether the deployment ever agreed to a spend of
    /// this size. Each step is weighed in its own unit, since octets read and calls made do not convert.
    /// </para>
    /// <para>
    /// It joins the activation refusal because activating a profile is the moment a deployment that has also switched
    /// attachment reading on begins deriving at scale, and an operator agreeing to one bill should not discover the
    /// other afterwards. The remedy is theirs and is stated by the figures: raise the ceiling the estimate passed, or
    /// leave attachment reading off. A deployment that reads no attachment reports nothing outstanding and is never
    /// refused by this.
    /// </para>
    /// </remarks>
    public bool ExceedsAttachmentCeiling =>
        this.Forecast == EmbeddingActivationForecast.WouldStartReindex
        && (Exceeds(this.AttachmentDerivation.Extraction, this.AttachmentDerivation.Coverage.Outstanding.OutstandingInputOctetCount)
            || Exceeds(this.AttachmentDerivation.Description, this.AttachmentDerivation.Coverage.Outstanding.OutstandingAttachmentCount));

    /// <summary>Gets whether any declared ceiling refuses this activation outright.</summary>
    public bool IsRefused => this.ExceedsSpendCeiling || this.ExceedsAttachmentCeiling;

    private static bool Exceeds(AttachmentDerivationPeriod period, long estimate) =>
        period.CeilingUnitCount is { } ceiling && estimate > ceiling;
}
