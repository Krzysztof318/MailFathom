// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Names where a deployment writes the raw MIME of the messages it stores next.</summary>
/// <remarks>
/// <para>
/// One value for the whole deployment rather than one per mail account or per owner, which
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 1
/// decided: the process-wide ceilings on stored content bound one thing, the readiness probe reports one bucket, and a
/// second axis of storage tenancy is what ADR 0014 already closed.
/// </para>
/// <para>
/// <b>It decides only where the next write goes.</b> It never describes what is already stored, because each stored
/// payload's own row says which backend holds it, so changing this setting moves nothing, re-encodes nothing, and leaves
/// every existing message readable from wherever it was written.
/// </para>
/// </remarks>
internal enum ContentStorageBackend
{
    /// <summary>The PostgreSQL table beside the message metadata, which is where a deployment that configures nothing writes.</summary>
    Database = 0,

    /// <summary>The S3-compatible endpoint the deployment configured.</summary>
    ObjectStorage = 1,
}
