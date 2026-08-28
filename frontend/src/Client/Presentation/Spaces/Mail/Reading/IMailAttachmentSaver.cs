// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Mail;

namespace MailFathom.Client.Presentation.Spaces.Mail.Reading;

/// <summary>Lets a person choose where one attachment is written, without putting a storage type in a model.</summary>
public interface IMailAttachmentSaver
{
    /// <summary>Chooses a destination and writes the attachment there.</summary>
    /// <param name="attachment">The safe name and type suggested to the platform picker.</param>
    /// <param name="write">Streams the file into the destination.</param>
    /// <param name="cancellationToken">Cancels the choice or the write.</param>
    /// <returns><see langword="true" /> where the file was saved, or <see langword="false" /> where the picker was cancelled.</returns>
    ValueTask<bool> SaveAsync(
        DeploymentMailAttachment attachment,
        Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken);
}
