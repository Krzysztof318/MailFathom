// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Names where the raw MIME of one message is held.</summary>
/// <remarks>
/// <para>
/// One value for the whole deployment rather than one per mail account or per owner, which
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 1
/// decided: the process-wide ceilings on stored content bound one thing, the readiness probe reports one bucket, and a
/// second axis of storage tenancy is what ADR 0014 already closed.
/// </para>
/// <para>
/// <b>The configured value decides only where the next write goes.</b> It never describes what is already stored,
/// because each stored payload's own row says which backend holds it, so changing that setting moves nothing,
/// re-encodes nothing, and leaves every existing message readable from wherever it was written.
/// </para>
/// <para>
/// One type serves both readings deliberately. The deployment's setting and the discriminator on a content row are the
/// same fact asked at two moments — where a payload is about to go, and where one already is — and a second type for
/// the second reading would be two names for one concept, drifting apart the first time a member was added. It lives in
/// this boundary rather than beside the configuration for that reason: the port's result carries it, so it has to be
/// somewhere both the composition root and the adapters can see.
/// </para>
/// </remarks>
public enum ContentStorageBackend
{
    /// <summary>The PostgreSQL table beside the message metadata, which is where a deployment that configures nothing writes.</summary>
    Database = 0,

    /// <summary>The S3-compatible endpoint the deployment configured.</summary>
    ObjectStorage = 1,
}
