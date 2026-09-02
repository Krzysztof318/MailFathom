// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.IntegrationTests.Orchestration;
using Xunit;

// One orchestration for the assembly, injected into whichever test class asks for it. A class fixture would start a
// PostgreSQL container per test class, which is the cost this suite is designed to pay exactly once.
[assembly: AssemblyFixture(typeof(MailFathomOrchestrationFixture))]

// A composed host connects to the same database the rest of the suite writes to and asserts on, so both of them run
// after all of it, and after each other. The orderer states those two constraints and leaves every other collection in
// the order xUnit produced.
[assembly: TestCollectionOrderer(typeof(ComposedHostsRunLastCollectionOrderer))]
