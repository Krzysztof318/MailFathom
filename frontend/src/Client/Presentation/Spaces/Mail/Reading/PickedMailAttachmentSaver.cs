// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Mail;
using Microsoft.Extensions.Localization;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;

namespace MailFathom.Client.Presentation.Spaces.Mail.Reading;

/// <summary>Saves an attachment through the platform picker, staging it before replacing the chosen file.</summary>
internal sealed class PickedMailAttachmentSaver : IMailAttachmentSaver
{
    private readonly IStringLocalizer words;

    public PickedMailAttachmentSaver(IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(words);

        this.words = words;
    }

    /// <inheritdoc />
    public async ValueTask<bool> SaveAsync(
        DeploymentMailAttachment attachment,
        Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(write);

        var name = attachment.FileName
            ?? this.words[MailMessageWords.AttachmentFallbackKey, attachment.Position + 1].Value;
        var extension = Path.GetExtension(name);
        extension = string.IsNullOrWhiteSpace(extension) ? ".bin" : extension;

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            SuggestedFileName = name,
        };

        picker.FileTypeChoices.Add(
            this.words[MailMessageWords.AttachmentFileTypeKey],
            [extension]);

        var chosen = await picker.PickSaveFileAsync();

        if (chosen is null)
        {
            return false;
        }

        var temporary = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(
            $"mailfathom-{Guid.NewGuid():N}",
            CreationCollisionOption.GenerateUniqueName);

        try
        {
            await using (var destination = await temporary.OpenStreamForWriteAsync().ConfigureAwait(false))
            {
                await write(destination, cancellationToken).ConfigureAwait(false);
            }

            CachedFileManager.DeferUpdates(chosen);
            await temporary.CopyAndReplaceAsync(chosen);

            if (await CachedFileManager.CompleteUpdatesAsync(chosen) is not FileUpdateStatus.Complete)
            {
                throw new IOException("The platform did not complete the attachment save.");
            }

            return true;
        }
        finally
        {
            await temporary.DeleteAsync();
        }
    }
}
