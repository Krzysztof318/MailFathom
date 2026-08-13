// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.DeadLetters;

/// <summary>One bounded page of the jobs nothing will attempt again, newest first.</summary>
/// <param name="Jobs">The jobs, ordered by when each one stopped, newest first.</param>
/// <param name="NextCursor">The boundary the following page is asked with, and <see langword="null" /> at the end of the reading.</param>
public sealed record DeadLetteredJobPage(
    IReadOnlyList<DeadLetteredJob> Jobs,
    DeadLetteredJobCursor? NextCursor);
