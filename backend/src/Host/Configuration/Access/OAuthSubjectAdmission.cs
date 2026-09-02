// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Access;

/// <summary>What a validated token's subject decides on the endpoint an OAuth block was configured for.</summary>
/// <remarks>
/// <para>
/// One block validates two different arrangements, and the difference is whose decision a subject is. The
/// administrative endpoint serves the deployment administrator, who is nobody's owner and holds no record, so the
/// profile's own list is the whole of who may sign in and a profile naming none would serve whoever the authorization
/// server's tenant holds. A mail-serving endpoint serves owners, and a subject there resolves one owner's credential
/// record — so the list is not merely unnecessary but the wrong place for the answer, and a token naming a subject no
/// record maps is refused exactly as an unknown credential is.
/// </para>
/// <para>
/// The value is passed to validation rather than read from the block, because the block cannot see which endpoint it
/// sits in and the same type is bound under both.
/// </para>
/// </remarks>
internal enum OAuthSubjectAdmission
{
    /// <summary>The profile's own configured subjects are what admit a person, so it names at least one.</summary>
    ConfiguredSubjects = 0,

    /// <summary>A subject resolves an owner's credential record, so the profile names none and one written there is refused as a retired setting.</summary>
    ResolvedOwnerCredentials = 1,
}
