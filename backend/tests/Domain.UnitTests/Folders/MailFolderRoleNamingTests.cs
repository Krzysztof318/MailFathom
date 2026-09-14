// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Domain.UnitTests.Folders;

/// <summary>Covers which roles a folder may be created for and what such a folder is called.</summary>
public sealed class MailFolderRoleNamingTests
{
    /// <summary>The set is narrower than the roles themselves, and each absence is a rule rather than an oversight.</summary>
    [Fact]
    public void Creatable_TheRolesAFolderMayBeMadeFor_LeavesOutTheInboxTheViewsAndTheOutbox()
    {
        // Assert
        Assert.Equal(
            [
                MailFolderSpecialUse.Archive,
                MailFolderSpecialUse.Drafts,
                MailFolderSpecialUse.Sent,
                MailFolderSpecialUse.Junk,
                MailFolderSpecialUse.Trash,
            ],
            MailFolderRoleNaming.Creatable);
    }

    /// <summary>The name is the service's rather than the person's, so it is the same on every account of every deployment.</summary>
    [Theory]
    [InlineData(MailFolderSpecialUse.Archive, "Archive")]
    [InlineData(MailFolderSpecialUse.Drafts, "Drafts")]
    [InlineData(MailFolderSpecialUse.Sent, "Sent")]
    [InlineData(MailFolderSpecialUse.Junk, "Junk")]
    [InlineData(MailFolderSpecialUse.Trash, "Trash")]
    public void StandardNameOf_ARoleAFolderMayBeMadeFor_IsTheRolesOwnEnglishWord(MailFolderSpecialUse role, string expected)
    {
        // Act, Assert
        Assert.Equal(expected, MailFolderRoleNaming.StandardNameOf(role));
    }

    /// <summary>The one folder name RFC 3501 fixes is the one this never invents.</summary>
    [Fact]
    public void StandardNameOf_TheInbox_IsTheNameEveryServerAlreadyHolds()
    {
        // Act, Assert
        Assert.Equal("INBOX", MailFolderRoleNaming.StandardNameOf(MailFolderSpecialUse.Inbox));
    }

    /// <summary>A folder made for a role is found by the name it was given, so no two roles may answer to one name.</summary>
    [Fact]
    public void StandardNameOf_EveryCreatableRole_AnswersANameNoOtherRoleDoes()
    {
        // Act
        var names = MailFolderRoleNaming.Creatable.Select(MailFolderRoleNaming.StandardNameOf).ToArray();

        // Assert
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
