// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>What erasing one owner removed.</summary>
/// <param name="OwnerErased">Whether an owner record was there to remove, so a repeat is reported as the no-op it is.</param>
/// <param name="RowsErasedBesideTheCascade">
/// How many rows the seam took itself, from the tables that name a mail account without a foreign key onto one. It is
/// stated because those are exactly the rows no constraint would have removed, so a number that falls to zero while
/// such a table still exists is the failure this record is written to make visible.
/// </param>
internal readonly record struct OwnerErasure(bool OwnerErased, int RowsErasedBesideTheCascade);
