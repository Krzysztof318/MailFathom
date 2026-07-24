// Copyright © 2026 Krzysztof Kasprowicz

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var database = postgres.AddDatabase("mailmcp");

builder.AddProject<Projects.Host>("mailmcp-host")
    .WithReference(database)
    .WaitFor(database);

builder.Build().Run();
