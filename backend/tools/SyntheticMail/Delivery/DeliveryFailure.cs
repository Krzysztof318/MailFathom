// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Delivery;

/// <summary>One message the server refused.</summary>
/// <param name="MessageId">The identity the corpus gave it, which is how it is found in a dry run of the same seed.</param>
/// <param name="Subject">Its subject, so a refusal is recognizable without cross-referencing anything.</param>
/// <param name="Reason">What the server or the transport said.</param>
internal sealed record DeliveryFailure(string MessageId, string Subject, string Reason);
