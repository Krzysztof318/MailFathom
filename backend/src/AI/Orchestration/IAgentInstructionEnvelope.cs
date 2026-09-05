// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Orchestration;

/// <summary>Supplies the text every composed agent's own instruction is wrapped in.</summary>
/// <remarks>
/// <para>
/// One seam for what is true of every AI operation rather than of one of them: the language a person reads, a
/// deployment's own wording, anything else that would otherwise have to be written into each instruction in the tree
/// and kept in step with the others. What ships is the seam and an implementation returning nothing, so composing an
/// agent today produces exactly the instruction the operation carries.
/// </para>
/// <para>
/// Consulted once per composition rather than once per process, which is what lets an implementation return different
/// text per person or per request without any agent changing. An implementation is therefore registered with whatever
/// lifetime its answer varies over, and must be cheap enough to be asked at the start of every run.
/// </para>
/// <para>
/// There is no substitution syntax, no placeholder, and no configuration key: whatever is returned is text, and the
/// composition puts one part before the operation's instruction and the other after it.
/// </para>
/// <para>
/// **Neither half may carry mail content, an address, or anything else personal.** The envelope is composed into the
/// instruction, which every turn of every run sends to the provider, so an implementation reading a mailbox to fill it
/// would put that mail in the one position <see cref="MailAnsweringInstructions" /> exists to keep it out of. What
/// belongs here is what the deployment or the person chose to say about how they are addressed, and nothing derived
/// from mail.
/// </para>
/// <para>
/// An operation recording which policy a run was conducted under records the version of its own instruction, which is
/// the composed one exactly while this stays empty. The first implementation that returns anything therefore has to
/// decide what such a record names, because an audited answer produced under a wrapper the record does not mention is a
/// record that misstates its own policy.
/// </para>
/// </remarks>
public interface IAgentInstructionEnvelope
{
    /// <summary>Gets the text placed before the operation's own instruction, or an empty string to place none.</summary>
    string Preamble { get; }

    /// <summary>Gets the text placed after the operation's own instruction, or an empty string to place none.</summary>
    string Postamble { get; }
}
