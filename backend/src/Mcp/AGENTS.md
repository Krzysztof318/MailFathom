# MCP Development Instructions

These instructions apply under `backend/src/Mcp/` in addition to parent instructions.

## Where a published type lives

- **The type a tool answers with goes in `Tools/Results/`, and every type nested inside one goes in the directory of the tool family that produces it.** A reader looking for what `save_draft` returns looks for `SaveDraftToolResult`, and one directory holding every result is what makes that lookup one listing; a state enum, a per-recipient record, and a mapping from a stored stage to a published spelling are read while reading the tool, so they belong beside it — `SavedDraftState` in `Tools/Drafts/`, `PublishedContact` in `Tools/Contacts/`. A family whose tools are still flat under `Tools/` gets a directory of its own the first time it has such a type.
