// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Persistence;

/// <summary>The bound on a single database command that startup configured the EF Core context with.</summary>
/// <remarks>
/// This exists as a registered value rather than as a setting to be read again for the same reason
/// <see cref="PostgresTextSearchConfiguration" /> does: it is captured once, during composition, and a later reload
/// cannot reach the context options it was written into. Publishing it is what lets a reloaded candidate be compared
/// against what the process is actually using instead of against configuration that describes an intent nothing
/// applied.
/// </remarks>
/// <param name="Value">How long a single database command may run before it is cancelled.</param>
public sealed record DatabaseCommandTimeout(TimeSpan Value);
