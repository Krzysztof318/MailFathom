// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using NSubstitute;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the reader of the account records a deployment serves, answering from records a test states.</summary>
/// <remarks>
/// The deployed reader is a query against PostgreSQL, so every composition a unit test builds is handed one of these
/// instead. Each answer is stated rather than left to the substitute's defaults, because a list it made up would read as
/// a deployment serving something.
/// </remarks>
internal static class ServedMailAccountReaders
{
    /// <summary>Builds a reader for a deployment whose records serve no account.</summary>
    /// <returns>The reader, answering every question with nothing.</returns>
    internal static IServedMailAccountReader HoldingNothing() => Holding();

    /// <summary>Builds a reader serving exactly the given records, whole.</summary>
    /// <param name="records">The served records, in the order the reader answers them.</param>
    /// <returns>The reader, which answers no other question with anything.</returns>
    internal static IServedMailAccountReader Holding(params MailAccountRecord[] records)
    {
        var reader = Substitute.For<IServedMailAccountReader>();
        reader.ReadServedRecordsAsync(Arg.Any<CancellationToken>()).Returns(records);
        reader.ReadServedAsync(Arg.Any<CancellationToken>()).Returns([]);
        reader.ReadScanningRequestsAsync(Arg.Any<UserId?>(), Arg.Any<CancellationToken>()).Returns([]);
        reader.ReadTrailingSettingsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        return reader;
    }
}
