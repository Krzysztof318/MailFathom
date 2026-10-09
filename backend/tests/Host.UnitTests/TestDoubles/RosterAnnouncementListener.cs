// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Signals;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Another replica listening on a backplane, noting for each announcement what the announcing replica held when it arrived.</summary>
/// <remarks>
/// The note is taken on delivery, which <see cref="InMemoryBackplane" /> makes synchronous with the publication, so it
/// reads the announcing replica exactly as the write left it at the moment it announced — which is what tells a write
/// that settled its own replica before announcing from one that announced first.
/// </remarks>
internal static class RosterAnnouncementListener
{
    /// <summary>Starts listening.</summary>
    /// <typeparam name="TObserved">What is noted about the announcing replica.</typeparam>
    /// <param name="backplane">The endpoint the announcing replica publishes over.</param>
    /// <param name="observe">Reads what the announcing replica holds at the moment an announcement arrives.</param>
    /// <returns>One note per announcement heard.</returns>
    internal static async Task<IReadOnlyList<TObserved>> ListenAsync<TObserved>(
        InMemoryBackplane backplane,
        Func<TObserved> observe)
    {
        var heard = new List<TObserved>();

        await new ConfigurationChangeAnnouncements(
                () => Task.FromResult(backplane.Connect()),
                NullLogger<ConfigurationChangeAnnouncements>.Instance)
            .ListenAsync(() => heard.Add(observe()));

        return heard;
    }
}
