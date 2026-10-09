// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Grants.Configurations;

/// <summary>Declares which users belong to which groups.</summary>
internal sealed class UserGroupMemberConfiguration : IEntityTypeConfiguration<UserGroupMemberEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserGroupMemberEntity> entity)
    {
        entity.ToTable(UserGroupMemberEntity.TableName);
        entity.HasKey(member => new { member.GroupId, member.UserId });

        // Cascading from both sides: removing a user ends every membership they held, and removing a group — refused
        // while it is still assigned — ends its memberships with it.
        entity.HasOne<UserGroupEntity>()
            .WithMany()
            .HasForeignKey(member => member.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
