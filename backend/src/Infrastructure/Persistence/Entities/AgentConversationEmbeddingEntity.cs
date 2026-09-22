// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using Pgvector;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>Where one message of an Agent conversation sits in one vector space.</summary>
/// <remarks>
/// It hangs off the entry the text was read from rather than off the conversation, so it goes with that entry — and so
/// with the conversation and with the person — by the cascade every entry already goes by, and nothing that erases a
/// conversation has to know this table exists. It is as sensitive as the words it was placed from: a vector is derived
/// from somebody's own question or the agent's answer to it, and is not anonymous for being a list of numbers.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class AgentConversationEmbeddingEntity
{
    internal const string TableName = "agent_conversation_embeddings";

    internal const string ConversationIdColumnName = "ConversationId";

    internal const string SequenceColumnName = "Sequence";

    internal const string EmbeddingProfileIdColumnName = "EmbeddingProfileId";

    internal const string DimensionColumnName = "Dimension";

    internal const string EmbeddingColumnName = "Embedding";

    internal const string GeneratedAtColumnName = "GeneratedAt";

    /// <summary>Gets or sets the conversation the message belongs to.</summary>
    public Guid ConversationId { get; set; }

    /// <summary>Gets or sets the place of the entry the text was read from.</summary>
    public long Sequence { get; set; }

    /// <summary>Gets or sets the space the vector was placed in.</summary>
    public Guid EmbeddingProfileId { get; set; }

    /// <summary>Gets or sets how many components the vector has, which the space declares and the check enforces.</summary>
    public int Dimension { get; set; }

    /// <summary>Gets or sets the vector itself.</summary>
    public required Vector Embedding { get; set; }

    /// <summary>Gets or sets when the vector was recorded, in UTC.</summary>
    public DateTimeOffset GeneratedAt { get; set; }
}
