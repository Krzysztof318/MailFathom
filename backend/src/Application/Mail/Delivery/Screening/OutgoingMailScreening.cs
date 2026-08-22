// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;

namespace MailFathom.Application.Mail.Delivery.Screening;

/// <summary>Asks whether one composed message may be put on a mail server at all, before anything is written down.</summary>
/// <remarks>
/// <para>
/// It is asked by the outbox and by the draft book rather than by an entrypoint, which is what makes it unbypassable
/// and is the same reasoning the send governor is placed by: those two are the one way a message becomes durable, so a
/// caller, a rule, a promotion, a recurring occasion, and whatever asks next all meet the screen without any of them
/// carrying a copy of it. A message reaching the outbox by a route added later is screened because it reaches the
/// outbox, not because whoever wrote that route remembered to.
/// </para>
/// <para>
/// The bytes are what is read rather than the fields an author supplied, and that is deliberate: half the routes into
/// the outbox have no authored fields at all — a promoted draft and a recurring occasion are a stored message and a
/// recomposition of one — and screening the composed bytes is the one reading that covers every route identically.
/// It also screens what will actually be transmitted rather than what somebody typed.
/// </para>
/// <para>
/// Nothing is parsed on a deployment that screens nothing. The screen answers whether it is active before the message
/// is read back, so an opt-in nobody took costs a send no parse, no allocation, and no scan.
/// </para>
/// </remarks>
/// <param name="textReader">Reads back what the composed message says.</param>
/// <param name="screen">Judges those values against what this deployment refuses to let leave.</param>
public sealed class OutgoingMailScreening(
    IOutgoingMailTextReader textReader,
    SensitiveContentEgressScreen screen)
{
    /// <summary>Screens one composed message and reports what stops it, if anything does.</summary>
    /// <param name="rawMime">The RFC 822 bytes about to be stored and transmitted or filed.</param>
    /// <param name="cancellationToken">Cancels the parse and the scan.</param>
    /// <returns>What stopped the act, or <see langword="null" /> where nothing did.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the message carries, which stops the act.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>
    /// <para>
    /// The answer is returned rather than raised, because the two callers refuse in different words: one did not queue
    /// a message and the other did not write a draft, and a shared exception would tell one author about the other's
    /// act. What is not returned is anything about the finding beyond the category — see
    /// <see cref="SensitiveContentEgressRefusal" /> for what that costs and why.
    /// </para>
    /// <para>
    /// The emptiness guard is above the active test rather than left to the reader, so what may be handed to this
    /// method does not depend on what an operator switched on. The outbox refuses empty bytes of its own accord and
    /// the draft book does not, and a deployment screening nothing would otherwise file a draft of no message at all
    /// while a screening one refused it.
    /// </para>
    /// </remarks>
    public async Task<SensitiveContentEgressRefusal?> FindRefusalAsync(
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        if (rawMime.IsEmpty)
        {
            throw new ArgumentException(
                "An outgoing message is screened from the MIME it will be transmitted as.",
                nameof(rawMime));
        }

        if (!screen.IsActive)
        {
            return null;
        }

        var composed = await textReader.ReadAsync(rawMime, cancellationToken);

        return await screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            composed.ScreenedValues,
            cancellationToken);
    }
}
