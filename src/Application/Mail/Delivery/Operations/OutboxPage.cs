// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Operations;

/// <summary>One bounded page of what a deployment has been asked to send, newest first.</summary>
/// <param name="Sends">The sends, ordered by when each one was written down, newest first.</param>
/// <param name="NextCursor">The boundary the following page is asked with, and <see langword="null" /> at the end of the reading.</param>
public sealed record OutboxPage(IReadOnlyList<OutboxEntry> Sends, OutboxCursor? NextCursor);
