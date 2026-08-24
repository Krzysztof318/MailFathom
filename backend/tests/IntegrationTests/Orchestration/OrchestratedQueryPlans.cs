// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Reads the plan PostgreSQL chose for a query, for a test whose claim is about an index rather than a result.</summary>
/// <remarks>
/// <para>
/// An index a migration creates is proved by the model tests; that it is the plan a read is actually served from is only
/// answerable against a real server holding enough rows for the planner to have a choice. The commands are issued
/// through ADO rather than through EF, because a plan is something EF publishes no API for — the connection still comes
/// from the scoped context, so the credential the data source supplies per connection is the one a deployment uses.
/// </para>
/// <para>
/// Everything runs inside a transaction that is rolled back, which is what makes a planner setting safe to apply: a
/// <c>SET LOCAL</c> dies with the transaction, so a pooled connection is handed back with the planner it arrived with.
/// The plan is taken over the same parameterized command the read would issue, so what it describes is the query under
/// test rather than a rewritten copy of it with its values inlined.
/// </para>
/// </remarks>
internal static class OrchestratedQueryPlans
{
    /// <summary>Reads the plan for one query, under whatever planner the server would ordinarily use.</summary>
    /// <param name="services">The orchestrated service graph whose scoped context owns the connection.</param>
    /// <param name="sql">The statement to explain, which is a compile-time constant of the calling class.</param>
    /// <param name="parameters">The values the statement carries, which never reach it as text.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The plan, as the lines <c>EXPLAIN</c> produced.</returns>
    internal static Task<string> ReadAsync(
        OrchestratedMailFathomServices services,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken) =>
        ReadAsync(services, sql, parameters, plannerSettings: [], cancellationToken);

    /// <summary>Reads the plan for one query, with the planner narrowed so that a stated alternative can be costed.</summary>
    /// <param name="services">The orchestrated service graph whose scoped context owns the connection.</param>
    /// <param name="sql">The statement to explain, which is a compile-time constant of the calling class.</param>
    /// <param name="parameters">The values the statement carries, which never reach it as text.</param>
    /// <param name="plannerSettings">Statements applied with <c>SET LOCAL</c> before the plan is taken.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The plan, as the lines <c>EXPLAIN</c> produced.</returns>
    internal static Task<string> ReadAsync(
        OrchestratedMailFathomServices services,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        IReadOnlyList<string> plannerSettings,
        CancellationToken cancellationToken) => WithConnectionAsync(
            services,
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);

                foreach (var plannerSetting in plannerSettings)
                {
                    await using var settingCommand = CreateCommand(connection, plannerSetting, []);
                    settingCommand.Transaction = transaction;

                    await settingCommand.ExecuteNonQueryAsync(token);
                }

                await using var command = CreateCommand(connection, $"EXPLAIN {sql}", parameters);
                command.Transaction = transaction;

                var planLines = new List<string>();

                await using (var reader = await command.ExecuteReaderAsync(token))
                {
                    while (await reader.ReadAsync(token))
                    {
                        planLines.Add(reader.GetString(0));
                    }
                }

                await transaction.RollbackAsync(token);

                return string.Join(Environment.NewLine, planLines);
            },
            cancellationToken);

    /// <summary>Runs work on the connection the scoped context owns.</summary>
    /// <param name="services">The orchestrated service graph whose scoped context owns the connection.</param>
    /// <param name="work">What to do with the open connection.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whatever the work produced.</returns>
    /// <remarks>
    /// Published beside the reads above for a test whose claim needs SQL the provider does not write — an ordering EF
    /// cannot express, for instance — rather than a plan.
    /// </remarks>
    internal static Task<TResult> WithConnectionAsync<TResult>(
        OrchestratedMailFathomServices services,
        Func<NpgsqlConnection, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var database = scope.GetRequiredService<MailFathomDbContext>().Database;

                await database.OpenConnectionAsync(token);

                try
                {
                    return await work((NpgsqlConnection)database.GetDbConnection(), token);
                }
                finally
                {
                    await database.CloseConnectionAsync();
                }
            },
            cancellationToken);

    /// <summary>Builds one parameterized command, which is the one place the SQL review for this suite is recorded.</summary>
    /// <param name="connection">The open connection the command runs on.</param>
    /// <param name="sql">The statement, which is a compile-time constant of the calling class.</param>
    /// <param name="parameters">The values the statement carries, which never reach it as text.</param>
    /// <returns>The command, for the caller to execute and dispose.</returns>
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Every command text reaching here is a compile-time constant of the calling test class; every value reaches the command as a parameter.")]
    internal static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
