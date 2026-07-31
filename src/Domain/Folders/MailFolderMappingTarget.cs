// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Domain.Folders;

/// <summary>States which of the two ways an alias names its remote folder a mapping uses.</summary>
public enum MailFolderMappingTarget
{
    /// <summary>The mapping names the server-advertised path directly.</summary>
    RemotePath = 0,

    /// <summary>The mapping names a special-use role and lets discovery find whichever folder carries it.</summary>
    SpecialUse = 1,
}
