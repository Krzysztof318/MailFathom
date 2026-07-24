// Copyright © 2026 Krzysztof Kasprowicz

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("0.8.2-pg17");
var database = postgres.AddDatabase("mailmcp");

builder.AddProject<Projects.Host>("mailmcp-host")
    .WithReference(database)
    .WaitFor(database);

builder.Build().Run();
