// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>What <see cref="MailAnsweringCapability" /> decided, and the answerer a run needs when it let one through.</summary>
/// <param name="Availability">What this deployment can do with a question right now.</param>
/// <param name="Answerer">The answerer to conduct the run with, non-null exactly when <paramref name="Availability" /> is <see cref="MailAnsweringAvailability.Available" />.</param>
/// <remarks>
/// Internal because it exists to carry one decision between two types of this assembly. Every boundary above reads the
/// availability alone, which is why <see cref="MailAnsweringCapability.ReadAsync" /> publishes that and not this.
/// </remarks>
internal sealed record MailAnsweringGate(MailAnsweringAvailability Availability, IMailQuestionAnswerer? Answerer);
