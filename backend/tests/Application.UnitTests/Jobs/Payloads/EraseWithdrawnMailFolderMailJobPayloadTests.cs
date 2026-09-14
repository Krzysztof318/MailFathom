// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Payloads;

/// <summary>Covers the identity one pass over a withdrawn folder's mail is enqueued under.</summary>
/// <remarks>
/// The key is composed of an account identifier and a folder alias, neither of which carries a maximum length, and it
/// is composed after the folder has already gone from the mail server. A key the store refuses there would leave the
/// deletion committed and its mail unswept, so the composition has to fit whatever those two names are — while still
/// telling two folders apart, since a key two of them shared would answer the second pass as work already enqueued.
/// </remarks>
public sealed class EraseWithdrawnMailFolderMailJobPayloadTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("primary"));

    [Fact]
    public void ToIdempotencyKey_AnOrdinaryAlias_NamesTheAccountTheFolderAndThePass()
    {
        // Arrange
        var payload = EraseWithdrawnMailFolderMailJobPayload.For(Account, MailFolderAlias.Create("projects"));

        // Act
        var key = payload.ToIdempotencyKey();

        // Assert
        Assert.Contains("PROJECTS", key.Value, StringComparison.Ordinal);
        Assert.EndsWith(":0", key.Value, StringComparison.Ordinal);
        Assert.NotEqual(key, payload.Next().ToIdempotencyKey());
    }

    [Fact]
    public void ToIdempotencyKey_AnAliasLongerThanAKeyMayBe_FitsTheKeyAndStillTellsTwoFoldersApart()
    {
        // Arrange
        var shared = new string('a', JobIdempotencyKey.MaximumLength);
        var first = EraseWithdrawnMailFolderMailJobPayload.For(Account, MailFolderAlias.Create(shared + "-one"));
        var second = EraseWithdrawnMailFolderMailJobPayload.For(Account, MailFolderAlias.Create(shared + "-two"));

        // Act
        var firstKey = first.ToIdempotencyKey();
        var secondKey = second.ToIdempotencyKey();

        // Assert
        Assert.Equal(JobIdempotencyKey.MaximumLength, firstKey.Value.Length);
        Assert.NotEqual(firstKey, secondKey);
        Assert.NotEqual(firstKey, first.Next().ToIdempotencyKey());
    }
}
