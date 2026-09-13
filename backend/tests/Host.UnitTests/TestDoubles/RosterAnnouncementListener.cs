// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Signals;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Another replica listening on a backplane, noting for each announcement whether the announcing replica had already released its roster.</summary>
/// <remarks>
/// The note is taken on delivery, which <see cref="InMemoryBackplane" /> makes synchronous with the publication, so it
/// reads the roster publication exactly as the announcing write left it at the moment it announced.
/// </remarks>
internal static class RosterAnnouncementListener
{
    /// <summary>Starts listening.</summary>
    /// <param name="backplane">The endpoint the announcing replica publishes over.</param>
    /// <param name="roster">The announcing replica's roster.</param>
    /// <returns>One entry per announcement heard, <see langword="true" /> where the roster publication was free when it arrived.</returns>
    internal static async Task<IReadOnlyList<bool>> ListenAsync(InMemoryBackplane backplane, ServedMailUsers roster)
    {
        var heard = new List<bool>();

        await new ConfigurationChangeAnnouncements(
                () => Task.FromResult(backplane.Connect()),
                NullLogger<ConfigurationChangeAnnouncements>.Instance)
            .ListenAsync(() => heard.Add(IsFree(roster)));

        return heard;
    }

    private static bool IsFree(ServedMailUsers roster)
    {
        // A publication still held leaves this wait pending, and it takes the publication once the write releases it;
        // the test has failed by then, so nothing waits on it afterwards.
        if (!roster.WaitForRosterPublicationAsync(CancellationToken.None).IsCompletedSuccessfully)
        {
            return false;
        }

        roster.ReleaseRosterPublication();

        return true;
    }
}
