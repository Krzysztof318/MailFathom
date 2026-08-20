// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Reads back the code a stored attempt ended in, from the column that records it.</summary>
/// <remarks>
/// A number this build does not recognize is a row written by one that allocated a code since. It is diagnostic detail
/// rather than something acted on, so it is reported as absent instead of failing the read of a record that is
/// otherwise perfectly readable. Every table that records a failure code stores it as a nullable number, which is why
/// the reading is one method rather than one per table.
/// </remarks>
internal static class StoredFailureCode
{
    /// <summary>Reads the error code the stored number names.</summary>
    /// <param name="storedCode">The number the column holds, where the row holds one.</param>
    /// <returns>The code, or <see langword="null" /> when the column is empty or names a code this build has no member for.</returns>
    internal static MailFathomErrorCode? ToErrorCode(int? storedCode)
    {
        if (storedCode is not { } failureCode)
        {
            return null;
        }

        return MailFathomErrorCode.TryParse(failureCode, out var failure) ? failure : null;
    }
}
