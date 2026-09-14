// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Builds the per-mailbox resolution of the one derived-reading setting that is still a user's.</summary>
/// <remarks>
/// Composed from the real <see cref="AccountLanguages" /> rather than substituted, because what a consumer needs from
/// it is the answer it derives — which assigned user's language a shared mailbox is read in — and a substitute would
/// let a test state that answer rather than exercise it. The default is a deployment writing English, so a suite
/// arranging nothing gets the quiet answer instead of a roster.
/// </remarks>
internal static class SyntheticAccountLanguages
{
    /// <summary>Builds the resolution over whichever of the two collaborators a test states.</summary>
    /// <param name="assignments">Who reaches which mailbox, or <see langword="null" /> for a deployment assigning none.</param>
    /// <param name="languages">What language each user reads, or <see langword="null" /> for English.</param>
    /// <returns>The resolution the derivation passes read a mailbox's language through.</returns>
    public static AccountLanguages Of(
        IMailAccountAssignments? assignments = null,
        IMailUserLanguages? languages = null) =>
        new(assignments ?? new StubMailAccountAssignments(), languages ?? new EnglishForEveryUser());

    /// <summary>The language of a deployment that has not been told a user reads anything else.</summary>
    private sealed class EnglishForEveryUser : IMailUserLanguages
    {
        public MailUserLanguage ForUser(MailUserId user) => MailUserLanguage.English;
    }
}
