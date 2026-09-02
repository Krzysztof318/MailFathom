// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using NSubstitute;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Builds the substituted content store the write paths are exercised against.</summary>
/// <remarks>
/// A content write is two steps, and only the second one takes a session. The first hands the payload to the port
/// before the unit of work opens, and what it answers is staged by the second — so a substitute that answered nothing
/// for it would hand every caller a null placement and fail inside the staging body rather than where the arrangement
/// was wrong. This answers what the database backend answers: the payload itself, with the length and digest it
/// computes over it, which is what every caller here is written against.
/// </remarks>
internal static class ContentStores
{
    /// <summary>Creates a content store that places a payload in the database and records every other call.</summary>
    /// <returns>The substitute.</returns>
    internal static IEmailContentStore Substituted()
    {
        var contentStore = Substitute.For<IEmailContentStore>();

        contentStore
            .PlaceContentAsync(
                Arg.Any<EmailContentKind>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(PlacedEmailContent.InDatabase(call.ArgAt<ReadOnlyMemory<byte>>(1))));

        return contentStore;
    }
}
