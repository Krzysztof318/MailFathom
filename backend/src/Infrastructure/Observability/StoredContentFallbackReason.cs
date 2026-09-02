// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Observability;

/// <summary>Names why a read of a moved payload was answered by the copy the database still holds.</summary>
/// <remarks>
/// The two are apart because they ask an operator for different things. An endpoint that holds no object under a key a
/// row names is an endpoint or a bucket to look at; an object that comes back and is not what the row records is one
/// object to look at, and the row's own length and digest say exactly what it should have been. Both are why the
/// retained copy exists, and both stop being survivable the moment it is released.
/// </remarks>
internal enum StoredContentFallbackReason
{
    /// <summary>The endpoint answered, and held no object under the key the row names.</summary>
    ObjectAbsent = 0,

    /// <summary>The object came back with a length or a digest that is not the one the row records.</summary>
    ObjectMismatch = 1,
}
