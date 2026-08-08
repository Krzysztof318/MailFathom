// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail.Audit;

/// <summary>Writes the durable record one finished answering run leaves behind.</summary>
/// <remarks>
/// <para>
/// It is asked of every run that ends, however it ended, and it decides for itself which entries are owed: one per
/// account in the run's scope whose operator turned the record on, and none at all on a deployment where nobody did. The
/// caller therefore reads no setting and holds no branch about whether a record exists.
/// </para>
/// <para>
/// <strong>It fails no answer for a reason of its own.</strong> The run is over by the time this is called and the
/// answer has already been produced, so a record that could fail the question it describes would be worse than a record
/// with a hole in it. Every entry that does not get written is warned about naming the run and counted where an operator
/// can see it. The one thing that travels on is the caller's own cancellation, which is reported the same way before it
/// is re-raised.
/// </para>
/// <para>
/// Enablement is read as the run ends rather than carried from when it began, which is the deliberate difference from
/// the mutation trail. A mutation is authored, persisted, and converged over minutes or hours, so a toggle flipped
/// mid-flight would leave gaps that look like changes nobody made; a run is one request, and there is no window worth
/// carrying an answer across.
/// </para>
/// </remarks>
public interface IMailAnsweringAuditTrail
{
    /// <summary>Appends the entries one finished run owes, for the accounts whose record is on.</summary>
    /// <param name="observation">What the run did, as it was observed from beginning to end.</param>
    /// <param name="cancellationToken">Cancels the durable write.</param>
    /// <returns>A task that completes once the entries are durable, were refused, or were not owed at all.</returns>
    Task RecordAsync(MailAnsweringRunObservation observation, CancellationToken cancellationToken);
}
