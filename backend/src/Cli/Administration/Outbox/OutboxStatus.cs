// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Outbox;

/// <summary>How much a deployment has standing at each stage of its outbox.</summary>
/// <param name="Stages">One count per stage, in the order the deployment declares them.</param>
/// <param name="OutstandingCount">How many sends nothing has finished with, which is the depth an operator means.</param>
internal sealed record OutboxStatus(
    [property: JsonPropertyName("stages")] IReadOnlyList<OutboxStageReading> Stages,
    [property: JsonPropertyName("outstandingCount")] int OutstandingCount);

/// <summary>How many sends stand at one stage.</summary>
/// <param name="Stage">The stage, as the deployment names it.</param>
/// <param name="Count">How many sends stand at it.</param>
internal sealed record OutboxStageReading(
    [property: JsonPropertyName("stage")] string? Stage,
    [property: JsonPropertyName("count")] int Count);
