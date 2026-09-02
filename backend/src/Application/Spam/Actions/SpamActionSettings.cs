// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam.Actions;

/// <summary>What an operator asked to happen to mail a classification calls junk.</summary>
/// <remarks>
/// <para>
/// Both switches are off in <see cref="None" />, which is what a deployment that configured nothing runs with: a verdict
/// is recorded and no mailbox is written to. They are independent of each other, so an operator can file junk without
/// marking it read and mark it read without moving it.
/// </para>
/// <para>
/// Neither switch is a decision about classification itself. Turning both off leaves classification recording exactly
/// what it recorded before, which is the property that lets an operator watch what a scanner concludes for a while
/// before letting it touch anything.
/// </para>
/// </remarks>
public sealed record SpamActionSettings
{
    private SpamActionSettings(
        bool filesJunk,
        bool marksJunkRead,
        MailFolderReference junkFolder,
        double? threshold)
    {
        this.FilesJunk = filesJunk;
        this.MarksJunkRead = marksJunkRead;
        this.JunkFolder = junkFolder;
        this.Threshold = threshold;
    }

    /// <summary>Gets the settings a deployment that asked for no action runs with.</summary>
    public static SpamActionSettings None { get; } = new(
        filesJunk: false,
        marksJunkRead: false,
        DefaultJunkFolder,
        threshold: null);

    /// <summary>Gets the folder a filing goes to when the operator named none: whichever folder the account maps to the junk role.</summary>
    /// <remarks>
    /// A role rather than an alias, because the folder junk belongs in is a different name on every server and an account
    /// already states which of its folders plays that part. An operator whose junk folder is not the one the account
    /// labelled names it explicitly instead.
    /// </remarks>
    public static MailFolderReference DefaultJunkFolder { get; } = MailFolderReference.ToRole(MailFolderSpecialUse.Junk);

    /// <summary>Gets whether junk is moved into the junk folder on the mail server.</summary>
    public bool FilesJunk { get; }

    /// <summary>Gets whether junk has its remote <c>\Seen</c> flag set.</summary>
    public bool MarksJunkRead { get; }

    /// <summary>Gets the folder junk is filed into, named by alias or by the role a folder plays.</summary>
    public MailFolderReference JunkFolder { get; }

    /// <summary>Gets the score a scanner has to reach before mail is touched, or <see langword="null" /> to act on every spam verdict.</summary>
    /// <remarks>
    /// It judges what a scanner scored, in the scanner's own scale, and it is a second reading of that score rather than
    /// a replacement for the one the verdict was reached under: labelling a message spam and moving it are different
    /// costs to get wrong, so an operator may be stricter about the second. It reaches no other stage, for the reason the
    /// classification threshold does not — a provider header carries a threshold in a scale this one knows nothing about,
    /// and a verdict resting on where the receiving server filed the message carries no score at all.
    /// </remarks>
    public double? Threshold { get; }

    /// <summary>Gets whether anything at all is acted on.</summary>
    public bool IsAnyActionEnabled => this.FilesJunk || this.MarksJunkRead;

    /// <summary>Builds the settings an operator's answers describe.</summary>
    /// <param name="filesJunk">Whether junk is moved into the junk folder.</param>
    /// <param name="marksJunkRead">Whether junk is marked read.</param>
    /// <param name="junkFolder">The folder junk is filed into, or <see langword="null" /> to take <see cref="DefaultJunkFolder" />.</param>
    /// <param name="threshold">The score a scanner has to reach before mail is touched, or <see langword="null" /> to act on every spam verdict.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="junkFolder" /> is the unspecified struct default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="threshold" /> is not a finite number.</exception>
    public static SpamActionSettings Create(
        bool filesJunk,
        bool marksJunkRead,
        MailFolderReference? junkFolder = null,
        double? threshold = null)
    {
        if (junkFolder is { } named && !named.IsSpecified)
        {
            throw new ArgumentException("The unspecified default of the struct names no junk folder.", nameof(junkFolder));
        }

        if (threshold is { } configured && !double.IsFinite(configured))
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                configured,
                "A configured action threshold is a finite number.");
        }

        return new SpamActionSettings(filesJunk, marksJunkRead, junkFolder ?? DefaultJunkFolder, threshold);
    }
}
