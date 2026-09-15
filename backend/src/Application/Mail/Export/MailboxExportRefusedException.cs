// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Export;

/// <summary>Indicates that an export was refused before anything was enqueued or written, and names which refusal it was.</summary>
/// <remarks>
/// <para>
/// One type carrying the code rather than a class per refusal, because every one of them is the same act with the same
/// consequence — the caller asked for an export and got none — and what differs is only the sentence and the code a
/// boundary publishes. Each is raised through a factory below, so the pairing of a code with its message is written
/// once.
/// </para>
/// <para>
/// Nothing in a message here is derived from a message, a folder name, or a caller's credential: the figures are this
/// deployment's own storage, and the setting names are the operator's own configuration.
/// </para>
/// </remarks>
public sealed class MailboxExportRefusedException : MailFathomException
{
    private MailboxExportRefusedException(MailFathomErrorCode errorCode, string operatorSafeMessage)
        : base(operatorSafeMessage) => this.ErrorCode = errorCode;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode { get; }

    /// <summary>Refuses an export on a deployment that keeps stored content in its database.</summary>
    /// <param name="settingName">The configuration key that would give the deployment somewhere to keep an archive.</param>
    /// <returns>The refusal.</returns>
    public static MailboxExportRefusedException NoObjectStorage(string settingName) => new(
        MailFathomErrorCode.MailboxExportUnavailable,
        $"This deployment stores mail content in its database, which cannot hold an export archive. Configure an object-storage endpoint under '{settingName}' and ask again.");

    /// <summary>Refuses an export whose measurement is past what this deployment will produce.</summary>
    /// <param name="measuredByteCount">What the export would carry.</param>
    /// <param name="limitByteCount">The limit it exceeded.</param>
    /// <returns>The refusal.</returns>
    public static MailboxExportRefusedException TooLarge(long measuredByteCount, long limitByteCount) => new(
        MailFathomErrorCode.MailboxExportTooLarge,
        string.Create(
            CultureInfo.InvariantCulture,
            $"The export would carry {measuredByteCount:N0} bytes of stored mail, past the {limitByteCount:N0} bytes this deployment exports at most. Export one folder at a time, or raise the limit."));

    /// <summary>Refuses an export a configured storage ceiling leaves no headroom for.</summary>
    /// <param name="measuredByteCount">What the export would carry.</param>
    /// <param name="reachedBound">Which ceiling had no room for it.</param>
    /// <returns>The refusal.</returns>
    /// <remarks>The bound is named rather than the free figure, because a ceiling is read together with what every other write is reserving against it and a number quoted here would be true only for the instant it was read.</remarks>
    public static MailboxExportRefusedException NoStorageHeadroom(
        long measuredByteCount,
        StoredContentBound reachedBound) => new(
        MailFathomErrorCode.MailboxExportTooLarge,
        string.Create(
            CultureInfo.InvariantCulture,
            $"The export would carry {measuredByteCount:N0} bytes and the {DescribeBound(reachedBound)} stored-content ceiling has no room for that. An archive is a second copy of the mailbox for as long as it is kept, so free storage or raise the ceiling and ask again."));

    /// <summary>Refuses a second export of an account that is already writing one.</summary>
    /// <returns>The refusal.</returns>
    public static MailboxExportRefusedException AlreadyRunning() => new(
        MailFathomErrorCode.MailboxExportAlreadyRunning,
        "This account is already exporting a different scope. Follow that export, or cancel it, and ask again.");

    /// <summary>Refuses to serve an archive that failed, was cancelled, expired, or was deleted.</summary>
    /// <returns>The refusal.</returns>
    public static MailboxExportRefusedException NoLongerDownloadable() => new(
        MailFathomErrorCode.MailboxExportNoLongerDownloadable,
        "This export has no archive to download. Its state says whether it failed, was cancelled, expired, or was deleted.");

    /// <summary>Refuses an act on an export this account does not hold.</summary>
    /// <returns>The refusal.</returns>
    public static MailboxExportRefusedException NotFound() => new(
        MailFathomErrorCode.MailboxExportNotFound,
        "This account holds no export under that identity.");

    private static string DescribeBound(StoredContentBound reachedBound) => reachedBound switch
    {
        StoredContentBound.User => "user's",
        StoredContentBound.Deployment => "deployment's",
        _ => "configured",
    };
}
