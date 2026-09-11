// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;

namespace MailFathom.TestSupport;

/// <summary>Names the replica a test arranges when which replica it is is not what the test is about.</summary>
/// <remarks>
/// Most tests need a replica identity only because a status answer carries one, so one name here keeps them from
/// restating a spelling each. A test that is about two replicas answering differently names the second itself, which is
/// what makes the distinction visible in the test rather than hidden in a helper.
/// </remarks>
public static class SyntheticReplica
{
    /// <summary>Gets the replica a test arranges by default, shaped as the composition root shapes a real one.</summary>
    public static ReplicaIdentity Answering { get; } = ReplicaIdentity.Create("mailfathom-0:1");
}
