// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Selects where a deployment writes the raw MIME of the messages it stores next, and describes that place.</summary>
/// <remarks>
/// <para>
/// A configuration root of its own rather than a block inside <see cref="PersistenceOptions" />, because what it selects
/// is whether message payloads are in the database at all: a deployment writing them into a bucket still runs every
/// metadata row, every index, and every job through PostgreSQL, so the two are separate decisions about separate remote
/// parties. A root is also what gives the endpoint's credentials their own secret-name uniqueness scope.
/// </para>
/// <para>
/// An absent section is the database backend, which is what every deployment that has never heard of this setting is
/// already running. Selecting the object-storage backend is what makes the block beneath it required, and startup then
/// refuses a declaration that is missing an address, a bucket, or either half of a credential — because a deployment
/// that named none of those must fail rather than acquire the host's own identity from the environment.
/// See <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 1.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ContentStorageOptions : IValidatableObject
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "ContentStorage";

    /// <summary>Gets or sets where the next stored payload is written.</summary>
    public ContentStorageBackend Backend { get; set; } = ContentStorageBackend.Database;

    /// <summary>Gets or sets the endpoint the object-storage backend writes to.</summary>
    /// <remarks>An absent block takes the working defaults, which are refused the moment <see cref="Backend" /> selects that backend, because none of them names an address, a bucket, or a credential.</remarks>
    public ObjectStorageOptions ObjectStorage { get; set; } = new();

    /// <summary>Gets whether this deployment stores payloads in the configured object-storage endpoint.</summary>
    /// <remarks>Read by the composition root to decide whether the endpoint, its transport, and its readiness probe are registered at all.</remarks>
    public bool IsObjectStorageSelected => this.Backend is ContentStorageBackend.ObjectStorage;

    /// <summary>Checks what no annotation on the block itself can state.</summary>
    /// <param name="validationContext">The context the options framework validates against.</param>
    /// <returns>The failures of the selected backend, or nothing when the section is usable.</returns>
    /// <remarks>
    /// The nested block is judged from here rather than by the framework, which validates the annotations of the type it
    /// was handed and never descends into a property's own object — and it is judged only when it was selected, so a
    /// deployment storing content in the database is never refused for a bucket it does not have.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The binder converts a bare number onto an enum without asking whether any member carries it, and
        // ErrorOnUnknownConfiguration does not catch that: it rejects unknown keys and failed conversions, and this
        // conversion succeeds. An undefined value is not the object-storage backend, so the block beneath it would go
        // unjudged and the deployment would write payloads to a database the operator did not select.
        if (!Enum.IsDefined(this.Backend))
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(this.Backend)} — '{(int)this.Backend}' names no backend; state one of {string.Join(", ", Enum.GetNames<ContentStorageBackend>())}.",
                [nameof(this.Backend)]);

            yield break;
        }

        if (!this.IsObjectStorageSelected)
        {
            yield break;
        }

        foreach (var error in this.ObjectStorage.FindConfigurationErrors())
        {
            yield return new ValidationResult(error, [nameof(this.ObjectStorage)]);
        }
    }
}
