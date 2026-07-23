# Initial scaffold scope

The initial scaffold creates the project boundaries needed for the first release without implementing mail, persistence, retrieval, or MCP behavior yet.

The ASP.NET Core host exposes only a root readiness response and `/health` endpoint. The Aspire AppHost wires the host to a PostgreSQL resource for future persistence work.
