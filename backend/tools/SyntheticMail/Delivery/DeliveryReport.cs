// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Delivery;

/// <summary>What a batch actually did, as against what it was asked to do.</summary>
/// <param name="Attempted">How many messages the batch was given.</param>
/// <param name="Delivered">How many the server accepted.</param>
/// <param name="Failures">The ones it refused, each naming the message and the reason.</param>
/// <remarks>
/// A batch that stopped at the first refusal would leave a mailbox holding an unknown prefix of the corpus, which is
/// worse than one that finished and said which messages are missing from it. The counts are what the command reports
/// and what its exit code follows.
/// </remarks>
internal sealed record DeliveryReport(int Attempted, int Delivered, IReadOnlyList<DeliveryFailure> Failures);
