// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.IntegrationTests.Orchestration;
using Xunit;

// One orchestration for the assembly, injected into whichever test class asks for it. A class fixture would start a
// PostgreSQL container per test class, which is the cost this suite is designed to pay exactly once.
[assembly: AssemblyFixture(typeof(MailMcpOrchestrationFixture))]

// The composed host connects to the same database the rest of the suite writes to and asserts on, so it runs after all
// of it. The orderer states that one constraint and leaves every other collection in the order xUnit produced.
[assembly: TestCollectionOrderer(typeof(ComposedHostRunsLastCollectionOrderer))]
