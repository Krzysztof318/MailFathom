// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.IntegrationTests.Orchestration;
using Xunit;

// One orchestration for the assembly, injected into whichever test class asks for it. A class fixture would start a
// PostgreSQL container per test class, which is the cost this suite is designed to pay exactly once.
[assembly: AssemblyFixture(typeof(MailMcpOrchestrationFixture))]
