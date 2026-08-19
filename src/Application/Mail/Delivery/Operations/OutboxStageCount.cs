// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Operations;

/// <summary>States how many recorded sends stand at one stage.</summary>
/// <remarks>
/// The pair is what both renderings of the outbox are built from — what <c>mfctl</c> prints and what a dashboard graphs
/// — so a stage added later appears in both or in neither. It carries a stage and a count and nothing else, which is
/// what keeps it publishable as a metric dimension: neither value is anybody's correspondence.
/// </remarks>
/// <param name="Stage">The stage the sends counted here stand at.</param>
/// <param name="Count">How many of them there are.</param>
public sealed record OutboxStageCount(OutgoingEmailStage Stage, int Count);
