# The design's state inventory

Everything here is read out of the artboards beside it in `design/`. **This file is the source a session builds a
screen from; a rendered preview is not.** A preview shows the one state it was clicked into, and most of what is below
is invisible until something reveals it — which is exactly the reading a screenshot cannot give.

It is written in this repository beside the artboards it describes, so a change that adds, removes, or re-gates a
state in an artboard changes this file in the same pull request. `design/README.md` says what else lives in `design/`.

## What the seven artboards are

| Artboard | What it settles |
|---|---|
| `prototype.dc.html` | The whole signed-in application: seven screens, every overlay, every composition, and the only artboard that carries navigation. Its logic is `logic/prototype/component.js` and its sample data `data/prototype/` |
| `client-states.dc.html` | The error, empty, waiting, refusal and in-flight states the client renders and the prototype does not draw, one artboard per state at every composition |
| `sign-in.dc.html` | Sign-in, and the server screen behind it |
| `toasts.dc.html` | Toasts and the blocking overlay — the system's answer to what somebody just did |
| `notification-gesture.dc.html` | The phone's notification-panel gesture, as four frames plus the numbers behind them |
| `ai-blocks.dc.html` | The block catalogue both AI surfaces draw: the nine types Discover renders read-only, and the four the Agent renders with controls, each in its three phases |
| `mail-search.html` | Mail search: results, empty, waiting, lexical-only, failure |

## How a state is named here

The prototype is one component. A screen is a region of one template gated by a flag, and every flag
is computed once in `renderVals()`. So a state's name in this file **is** the flag name in the
source: `grep -n 'showThreadPane' design/prototype.dc.html design/logic/prototype/component.js` gives both
the gate in the template and the one line that decides it. *Reveal* is what makes it true.

Three things decide composition before any state does, and they are read from the width alone
(`renderVals`):

| Name | Condition | What it changes |
|---|---|---|
| `isMobile` | `w < 700` | Bottom navigation, drawers, touch sizing |
| `tablet` | `700 ≤ w < 1180` | Touch and drawers, toolbar controls without labels |
| `singlePane` | `w < 820` | One pane at a time — the list and the content never stand together |
| `narrow` | `700 ≤ w < 1020` | The narrower desktop composition |
| `touch` | `isMobile \|\| tablet` | Every `pointer: coarse` affordance |
| `drawer` | `tablet \|\| isMobile` | The side list arrives as a drawer rather than a column |

`FRAMES` is the design's own four sizes: `phone` 390 × 844, `fold` 884 × 832,
`tablet` 1024 × 768, and the artboard's 1440 × 900. A `fold` is a *tablet-class* width with two panes.

## Discover — `isDiscover`

The run is a phase machine (`st.phase`), and the blocks arrive one at a time (`ready.<block>`).

| State | Flag | Reveal |
|---|---|---|
| Nothing asked yet | `showIdle` | `st.phase === "idle"` — the state the prototype opens in |
| Running | `running` | `st.phase === "running"`, entered by submitting a question |
| A run has happened | `hasRun` | `st.phase !== "idle"`; gates the run's own chrome |
| Answer block | `answerReady` | `ready.answer`, set as the stream reaches it |
| Timeline block | `timelineReady` | `ready.timeline` |
| Fact table | `tableReady` | `ready.table` |
| Attachment gallery | `galleryReady` | `ready.gallery` |
| People block | `peopleReady` | `ready.people` |
| Proposed action | `actionReady` | `ready.action` |
| What the sources do **not** settle | `gapNote` | the chosen plan carries a `gap` string — `PLANS.contract` and `PLANS.people` do, `PLANS.files` does not |
| The run's own trace | `showTrace` | `hasRun && props.showTrace && phase === "done"` — a **prop**, so a preview shows it only when the property is on |
| Evidence inspector open | `inspectorOpen` | a citation is pressed |
| Evidence inspector closed | `inspectorClosed` | nothing selected — the resting state |

Which blocks a run produces is the plan, and there are three (`PLANS`): `contract`
(answer + timeline + table + action), `files` (answer + gallery + action), `people`
(answer + people + action). **No single run draws every block**, so a screenshot of one plan is not
the block set to build against; `ai-blocks.dc.html` is where the block types stand
together. **Discover draws them read-only.** The same catalogue with controls on it is the Agent's,
and the blocks screen carries both halves — see *Agent* below.

## Mail — `isMail`

### The panes

| State | Flag | Reveal |
|---|---|---|
| Thread list shown | `showThreadList` | `!singlePane \|\| mailPane === "list"` |
| Content pane shown | `showContentPane` | `!singlePane`, or the thread, composer, or document is open |
| Thread shown | `showThreadPane` | as above, and nothing else has taken the pane |
| Back to the list | `showMailBack` | `singlePane && mailPane === "thread"` — phone and fold only |
| Toolbar | `showMailToolbar` | `!selectionOn && !singlePane` |
| Primary actions in the bar | `tbPrimaryShown` | the toolbar has room for them |
| Resizer between panes | `showResizer` | `!singlePane && !tablet` |
| Side list expanded | `sideExpanded` | not collapsed; collapsed is 58 px, expanded is 210 px and draggable to 420 |
| Side list resizer | `showSideResizer` | `!drawer && !sideCollapsed && !isMobile` — drag widens the folder pane between 210 and 420 px, double-click returns it to 210 |
| Side list as a drawer | `showSide` / `sideScrim` | `drawer && drawerOpen` |
| Folder button | `showFolderBtn` | `drawer` |
| Account menu on a mailbox | `ctxMenu` through `g.ctxOpen` | right-click a mailbox header, or hold it for 460 ms — *New folder* and *Mark all as read*; **All accounts carries none** |
| Menu on a folder row | `folderMenu` | right-click a folder, or hold it — *Mark all as read* always, *New folder inside* while the path is under three segments, and *Edit folder* and *Delete folder* only on a folder the user made; **All accounts carries none** |
| New folder row | `g.canAdd` | drawn at the end of every account group as a `create_new_folder` row reading *New folder* — `g.key !== "all"`, so the unified view carries none, having no mail server of its own; a collapsed side list draws the glyph alone (`g.expanded`), and the row takes the phone's taller padding under `isMobile` |
| A folder the user made | `st.extraFolders[<mailbox>]` | the row above, the two menus above it, or *New folder here* in the move sheet; it is drawn below the standard set |

An account draws six folders in the same order — Inbox, Sent, Drafts, Archive, Spam, Trash
(`STD_FOLDERS`) — and a count only where the mock data gives one. **The unified view
draws three of them.** `STD_FOLDERS` takes a `unified` flag, and the last three carry `perAccount`:
*All accounts* is Inbox, Sent and Drafts alone, because Archive, Spam and Trash are always opened in
the account they live in rather than merged across accounts. Anything under either set is a folder
the user made.

### Folders the user made

Added on 2026-09-10. A folder is no longer a name in a flat
list: it nests, it is placed somewhere in the account's own tree, and it can be edited and deleted.

| State | Flag | Reveal |
|---|---|---|
| The folder tree | `st.folderOpen[<mailbox·path>]` | a folder with children draws a twisty; each level is indented 15 px, and **three path segments is the ceiling** (`MAX_DEPTH`) |
| Children shown | `mb.hasKids` / `mb.twisty` | `expand_more` against `chevron_right`, titled *Collapse subfolders* and *Show subfolders*; a collapsed side list draws depth 0 only |
| New folder dialog | `newFolderOpen` | the *New folder* row at the end of an account group, *New folder inside* on a folder, or *New folder here* in the move sheet |
| Editing one | the same dialog in `mode: "edit"` | *Edit folder* — the title reads *Edit folder* and the button *Save changes* |
| Its two fields | `nfName` / `nfParent` | *FOLDER NAME*, and *INSIDE* as a select rather than a text field |
| Where it may sit | `nfLocations`, from `folderLocations` | the select's options: *Top level of the account* first, then the account's folders drawn as `Parent / Child`. Editing one drops the folder itself, everything under it, and any parent too shallow to hold its subtree |
| Nothing typed yet | `nfCreateStyle` / `nfCreateHover` | the create button is drawn flat and refuses the pointer while the name is empty |
| The name is taken | the `warning` notification *Pick another name* | saving an edit onto a name already used in that place — *There is already a folder called “<name>” inside …* |
| Delete confirmation | `delFolderOpen` / `delFolderText` | *Delete folder* — four outcomes computed from the state rather than one sentence |

**The design's own statement about what a folder is: the user places it, MailFathom puts it on the
server.** The hint under the select reads *MailFathom creates the folder on this account's mail
server and picks where it sits there — you only choose where it shows up in your folder list.*, and
the source says why in as many words — accounts are administrator-managed and the server path is not
the user's business. So nothing about a folder the user made shows a remote path or takes one — the
account editor's own folder mapping is the administrator's setting rather than theirs — and creating,
renaming and deleting one each post a success toast naming the mailbox and the place
(`locLabel` —
*inside Parent / Child*, or *top level*), never a path. `MAX_DEPTH` still caps the tree at three
segments, and `folderLocations` enforces it against the subtree being moved rather than against the
folder alone.

The delete confirmation states what goes with the folder, in three parts. *The folder* — or *The
folder and the folder nested inside it*, or *…and the N folders nested inside it* — *will be deleted
in `<mailbox>` and on your mail server.* Then the mail: where the folder holds stored conversations,
they go with it, permanently on both servers with nothing left to restore while the account's hard
delete is on, or to Trash and recoverable while it is off; where it holds none, *It holds no mail,
and deleting it cannot be undone.*

### The per-message AI reading

| State | Flag | Reveal |
|---|---|---|
| AI summary | `aiSumOpen` | the `auto_awesome` control on a message row, or *AI summary* at the top of that row's context menu |
| Its cards | `aiSumCards` | **three at most**, each a heading, the claim, a *Why:* line, a model chip reading *Model — mailfathom-email-enrichment*, and the fragment of the message it rests on |
| Which headings exist | — | *WHAT IS SETTLED*, *WHAT IS STILL OPEN*, *WHAT IS THE COMMITMENT*, *WHAT THE DEADLINE IS*, *WHAT CHANGED BETWEEN VERSIONS* for a `VERSION DIFFERENCE` state, and *WHAT THIS IS ABOUT* where the list's own reading fills a card out. The cards follow the thread's state list and stop at three, so a fourth state never reaches the dialog — which is where both seeded `VERSION DIFFERENCE` states sit |
| Jump to the source | `ac.showSrc` | only where the thread holds more than one message — *Open message N in the thread* |

The dialog opens at 620 px on the desktop and full-screen on the phone. Its own sentence is the
contract it states: *Every reading below comes from this message alone, and each one shows the
fragment it rests on.*

### The thread

| State | Flag | Reveal |
|---|---|---|
| Thread head | `showThreadHead` | `!st.stateBarOff \|\| singlePane` |
| State bar beside the thread | `showThreadStateBar` | `!singlePane && !tablet && !stateBarOff` |
| State cards | `showStateCards` | `!singlePane && !stateStale` |
| State inside the body | `stateInBody` | `tablet && !stateBarOff && !stateStale` — the tablet's own answer to the same content |
| The AI line on one pane | `mobileStateLine` | `singlePane && !stateStale` — the state bar's one-line reading, on the phone |
| Reading held back | `stateStale` / `stateFresh` | the thread carries `stateStale`: a message joined or left the conversation after its state was derived. The state cards, the in-body chips, the one-pane AI line and the thread sheet's state list stop drawing, and each place says instead *This conversation has changed since where it stands was last derived, so that reading is held back until it is derived again.* The prototype reaches it through *Refresh*, whose second step marks the thread it changes |
| Four states or more | `stateRowStyle` / `stateBodyRowStyle` / `sc.valueStyle` | `thread.state.length >= 4` — the state cards beside the thread and the chips inside the body stand as a two-column grid, and each chip's value wraps (`text-wrap:pretty`, line height 1.35) rather than ending in an ellipsis on one line. The phone keeps its single column. `contoso` and `piotr` reach it, each carrying a fourth state, `VERSION DIFFERENCE`, that says what a later message corrected in an earlier one |
| Held back, on the tablet | `staleInBody` | `tablet && !stateBarOff && stateStale` — the same sentence as a note at the top of the thread body, where `stateInBody` would have drawn the chips |
| Panels hidden | `st.stateBarOff` | the *fullscreen* toolbar control; nothing else sets it |
| Jump to the agent | `showAgentJump` | `!isMobile && !stateBarOff` |
| Arrived from a citation | `showCitationTag` / `cameFromResult` | reached from a Discover citation, and only for the `contoso` thread |
| Answer drawn in mail | `mailAnswer` | `st.mailAnswer` and the `contoso` thread |
| Thread actions sheet | `threadSheetOpen` | the sheet control, phone-shaped |
| Remote content removed | `m.remoteBlocked` | a message that is not the user's own and carries remote references, until they are loaded. A card at the top of the body says *This message asked to fetch content from another server. That was removed, so opening it told the sender nothing.*, then *Removed references: N*, then *Loading them tells the sender that this message was opened. This choice applies to this message only and is not remembered anywhere.*, over one accent button, *Load images from the sender*, 44 px tall on touch. The count comes from `m.remoteRefs` where a message sets one, and none does; otherwise a hash of sender and time gives about a third of the messages none and the rest between 3 and 18. *Images loaded automatically* in the settings does not reach it |
| Remote content loaded | `m.remoteLoaded` | pressing that button — the card gives way to one muted line with an `image` glyph, *Images from the sender are loaded for this message.* The choice is kept per message in `st.remoteLoaded` for the session and nowhere else |
| Reveal the earlier messages | `showEarlier` in the thread head | *Show N earlier messages* / *Hide earlier messages*, in the pane already open |
| Expand every message | `showExpandAll` | **constant `false`** — the source hands the screen no per-message collapse at all, so a revealed message is drawn in full |

### Waiting

Added to the prototype after this inventory was first written, and read against the new source on
2026-09-08: every wait in mail draws a skeleton rather than leaving the screen unchanged.

| State | Flag | What it draws |
|---|---|---|
| The folder's list is arriving | `listLoading` / `listReady` | `listSkelRows` — eight shimmering rows at the desktop, seven at the phone, each an avatar circle and two lines |
| A message is arriving | `msgLoading` | `msgSkel*` over the content pane: subject, meta, head with an avatar, then body lines |
| The full HTML surface is arriving | `htmlLoading` / `htmlDoc` | the HTML view's own skeleton before the document |
| An attachment is opening | `docLoading` / `docZoom` | the document surface's skeleton, and the zoom the surface opens at |

### Selection, search and filters

| State | Flag | Reveal |
|---|---|---|
| Selection running | `selectionOn` | one or more rows selected; it *replaces* the toolbar |
| Selection bar | `selBarShown` | the same, and it appears on five screens, not only mail |
| Search idle | `listSearchIdle` | `!st.listSearchOn` |
| Search active | `listSearchActive` | the search control |
| Filters open | `filtersOpen` | the filter control |
| Filters in force | `filtersActive` | `filterCount > 0` over unread, flagged, attachment, date range and sort |
| Undo after archiving | `undoShown` | `isMobile` and the last archived thread is still archived — **phone only** |
| A thread being dragged | `st.dragIds` (`dragThreadsStart`) | a row is `draggable` and starts a drag; the dragged rows go to `opacity:0.45` while it lasts, and a drag started on a row inside a selection of more than one carries **the whole selection** rather than that row |
| A folder under the drag | `st.dragOverKey` (`dragOverFolder`) | the pointer over a folder row in the side list while `dragIds` is set — that row draws the accent-soft background, the accent text and an `inset 0 0 0 2px var(--accent)` ring, which is the only affordance saying a drop lands here |

Dropping is a move and nothing new: `dropOnFolder` hands the ids to `moveThreads`,
which now takes them as an argument rather than reading `moveFor`, so a drag and the
move dialog end in the same act, the same row animation and the same toast. The drop targets are the
folder rows alone — a mailbox header carries none of the four handlers. `draggable` and the two drag
handlers sit on every row at every width, beside the long-press and swipe the touch compositions
already use, so the design states no separate touch gesture for a move and leaves the move dialog as
what a finger reaches.

### Composing

| State | Flag | Reveal |
|---|---|---|
| Composer open | `composeOpen` | the compose control |
| Composer expanded | `composeExpanded` | open and not minimised |
| Minimise offered | `composeMinShown` | `!composeInPane && !isMobile` |
| Composer chrome | `composeChromeShown` | the composer is not the active tab |
| Suggestion chips | `showComposerChips` | `!singlePane && !tablet` |
| Cc/Bcc | `ccOpen` | the Cc control |
| Formatting bar | `fmtBarShown` | `!touch`, or the touch toggle was pressed |
| Formatting toggle | `fmtToggleShown` | `touch` |
| Formatting hint | `fmtHintShown` | `!touch` |
| AI drafting | `aiBusy` | the AI draft action, while it streams |
| AI draft standing | `aiDraftShown` | the same, once it finishes |
| Send confirmation | `sendAskOpen` | pressing send with something to warn about |
| Warnings in it | `sendHasWarn` | the warning list is not empty — an unaccepted AI draft is one |

### Documents and HTML

| State | Flag | Reveal |
|---|---|---|
| Document open | `docOpen` | an attachment is opened |
| HTML view open | `htmlOpen` | the `code` control on a message head, titled *Show the original message* |
| HTML mode warning | `htmlModeWarn` | `st.msgView === "html"`, chosen in Settings |
| HTML confirmation | `htmlAskOpen` | opening the original from a message — *Show the original message?*, answered by *Stay in simplified* or *Show the original* |

### Tabs

Tabs are a **work mode**, not a layout: `tabsMode = w >= 1180 && workMode === "tabs"`. The
prototype's initial state is `workMode: "classic"`, so **every tab state is invisible until the user
menu's *Tab mode* switch is turned on at a width of at least 1180 px**.

| State | Flag | Reveal |
|---|---|---|
| Tab bar | `tabsBarShown` | tabs mode with at least one tab |
| Nothing open | `noTabsOpen` | tabs mode with none — the tabbed empty state |
| Close one | `closeAskOpen` | closing a tab with unsaved work |
| Close all | `closeAllAsk` | the close-all control |
| The row does not fit | `tabsRowNote` | `w < 1180`, and it says *available on a wider screen* |

## Cases — `isCases`

| State | Flag | Reveal |
|---|---|---|
| List | `showCaseList` | `!isMobile \|\| casePane !== "detail"` |
| Detail | `showCaseDetail` | `!isMobile \|\| casePane === "detail"` |
| Back | `showCaseBack` | `isMobile && casePane === "detail"` |
| Full head | `caseHeadFullShown` | `!isMobile`, or the detail has not been scrolled |
| Head actions | `caseHeadActionsShown` | `!isMobile` |
| Fact head | `caseFactHeadShown` | `!isMobile` |
| Extra asks | `caseAskExtrasShown` | `!isMobile` |
| **No documents** | `caseNoDocs` | the case's `docs` is empty — true of no case in the mock data |
| New case | `newCaseOpen` | the new-case control |
| Its search results | `csFoundShown` | searching inside the new-case dialog |

## Tasks — `isTasks`

| State | Flag | Reveal |
|---|---|---|
| More menu | `moreOpen` | the overflow control |
| New task | `newTaskOpen` | the new-task control |
| Context menu | `ctxMenuOpen` | long press, or right-click |
| Delete confirmation | `confirmDelOpen` | a delete action |
| Reminder dialog | `remModalOpen` | the reminder control on a task |

## Calendar — `isCal`

| State | Flag | Reveal |
|---|---|---|
| Week | `calIsWeek` | the default view |
| Day | `calIsDay` | the view control |
| Month | `calIsMonth` | the view control |
| Agenda | `calIsAgenda` | the view control |
| A month day's own list | `calMonthDayShown` | `isMobile` — **phone only**, and invisible at every other width |
| That day is empty | `calMonthDayEmpty` | the selected day has no events |
| **No proposals left** | `calNoProposals` | every proposed slot has been dealt with — reachable only by clearing them all |
| Event open | `eventOpen` | an event is pressed |
| Event has a source | `eventHasSrc` | the event carries the mail it came from |
| Event has reminders | `eventRemAny` / `eventRemNone` | the reminder list for that event |
| Parsed from a message | `evParsedShown` | the event was read out of mail |
| New event | `newEventOpen` | the new-event control |

## People — `isPeople`

| State | Flag | Reveal |
|---|---|---|
| List | `showPeopleList` | `!isMobile \|\| peoplePane !== "detail"` |
| Detail | `showPersonDetail` | `!isMobile \|\| peoplePane === "detail"` |
| Back | `showPeopleBack` | `isMobile && peoplePane === "detail"` |
| Avatar | `personHasAvatar` / `personNoAvatar` | whether an avatar file exists for the name — **both states are in the mock data** |
| Collected person | `personCollected` | the person was gathered rather than added; it gates three regions |
| Next step | `personHasNext` | the person's AI reading proposes one |
| **No documents** | `personNoDocs` | that person's `docs` is empty |
| New contact | `newContactOpen` | the new-contact control |

## Agent — `isAgent`

| State | Flag | Reveal |
|---|---|---|
| Working | `agentThinking` | a question was sent, or the scripted example is playing — a spinning `progress_activity` glyph beside `agentStatus` |
| Context chip | `agentCtxShown` | the agent was opened from a screen that carries context, **or** the open conversation carries its own `ctx` and it was not cleared |
| Starter chips | `agentShowChips` | fewer than two messages in the conversation |
| The scripted example playing | `st.demoRun` (`playDemo`) | the *Real example* starter chip |
| Cancel offered | `agentCancelActive` | `agentThinking \|\| demoRun`; the control itself is drawn at all times and is disabled otherwise |
| Proposed next steps | `agentNextShown`, from `nextActs` | the agent has stopped and the last reply carries `next` — at most three, under a `PROPOSED NEXT` label |
| An answer with prose | `m.hasMd`, drawing `m.md` | the bot message carries `text`, `paras`, or both — each is read as Markdown and drawn as one list of blocks, a paragraph's blocks animating in together as it lands. `m.hasText`, `m.hasParas` and `m.paras` are still computed beside it and nothing reads them |
| An answer still being worked | `m.working` | the message carries `working` — it draws an `autorenew` glyph and *The agent is going through your mail…* instead of prose |
| An answer carrying blocks | `m.hasBlocks` | the message carries `blocks` |
| Back to the conversation | `convReturnShown` | a block's `nav` row was pressed, which is what `goLook` records in `agentReturn`; the pill stands fixed over whatever screen was opened until `backToConv` is pressed |
| History open | `historyOpen` | `!isMobile` by default, and the history control otherwise |
| History close | `historyCloseShown` | `isMobile` |
| History search has a value | `historySearchValue` | typing in it |
| **Archive section exists** | `hasArchived` | at least one conversation is archived |
| Archive open | `archiveSectionOpen` | pressing the archive section |
| History resizer | `showHistoryResizer` | `!isMobile` |
| Conversation bar | `convBarShown` | tabs mode with more than one conversation — see the tabs note above |
| Delete a conversation | `deleteConvAskOpen` | the delete control on a conversation |

### The thread is flat, and the blocks are the only cards in it

The agent's turn carries no card at all — no panel, no border, no shadow, no tail. It is plain text
on the page, `width:100%` to a `max-width` of 900 px on the desktop and the whole width on the phone
(`bubbleStyle`). The reader's own turn stands on the right instead: `rowStyle`
justifies `flex-end` and `colStyle` aligns the column, and the turn is drawn as a lightly
tinted block on `--accent-soft` in `--text` at radius 10, 775 px wide on the desktop and 92 % on the
phone. The line above a reply is `AI · <scope>` in 10.5 px tracked caps with the `AI` alone in
`--accent-d`, where it used to be a filled accent pill.

**The column centres itself with padding rather than a width cap.** Off the phone, the scrolling
thread (`agentScrollStyle`) and the field under it (`agentInputWrapStyle`) take the same
side padding: 26 px under `narrow`, and otherwise `max(26px, calc((100% - 1290px) / 2))`, with 1170 px
in place of 1290 under `compact` (`!isMobile && w < 1180`). So the field lines up with the
thread at every width, and a wide window keeps both in the middle instead of stretching them. The
thread used to stop at a `max-width` of 1520 px, or 780 px under `compact`, and the framed box around
the field (`agentBoxStyle`) carried the same cap; neither has one now.

**An answer's prose is Markdown.** `m.md` runs the reply's `text` and each of its `paras` through
`mdView`, the same helpers a mail body is drawn with (`mdParse`, `mdBlock`): headings, paragraphs, bold, italic, strikethrough, inline and fenced code, plain,
ordered and task lists, quotes, tables, rules and links. The template draws the blocks
12 px apart, and each text run is either a `span` or, for a link, an `a`. `DEMO_SCRIPT` exercises
most of it: a bold lead-in, a quoted clause, a bulleted list with inline code, a level-two heading
over a numbered list, a task list of the three things waiting on the reader, and italics.

A mail body in the simplified view now draws its runs the same way: a paragraph's or a
list item's runs sit side by side with no whitespace between them, so only a run's own text separates
it from the next. The template used to break a line between runs, and that break rendered as a space,
which put a stray gap before the punctuation after a bold, code or link run.

So the separation between one answer and the next is carried by the blocks, and `bkView` says so in
as many words: a settled block sits on `--panel` behind a `--border2` edge with
`0 1px 2px var(--sh-1)`, a pending one keeps `--accent-soft` on `--accent-line` and takes the same
shadow, the chip on a settled block moves to `--sub` on `--border2`, and the draft `textarea` moves
to `--sub`. The conversation history follows the same rule and stops being a stack of cards: a 3 px
left edge, `--accent` on the open row and transparent otherwise, and a 1 px gap between rows.

### What a run looks like while it runs

**The status line says what the agent is doing right now.** `agentStatus` falls back to
*Going through your mail* and is replaced as the work moves on, inside a `role="status"`
`aria-live="polite"` region beside a spinning `progress_activity` glyph. It stands for the whole run:
content landing is not the end of the work, so the indicator goes only when there is nothing left to
load.

**Content arrives whole, never token by token.** `DEMO_SCRIPT` is a scripted conversation
played back in real time by `playDemo`, `demoNext` and `demoApply`,
one timer at a time. Its step kinds are `u` a reader's turn, `s` a status, `b` a new
reply, `t` text appended to one, `p` a whole paragraph appended, `k` a block appended, and `n` the
proposed next steps. The source states the rule itself: a widget appears only once it is whole, and a
longer answer lands one paragraph at a time. `mfmsgin` animates a turn or a paragraph in
and `mfblkin` a block; `mfdot` is declared beside them and nothing reads it.

**Cancel stands beside Send at all times.** `agentCancel` stops the playback or a pending
reply where it stands. Disabled it is `tabIndex` `-1` with `aria-disabled` and the label
*Cancel — nothing is running*; active it is filled `--err` and labelled *Stop what the agent is
doing*. Nothing is rolled back — what arrived stays, and one reply scoped *stopped by you* is written
reading *Stopped. What arrived so far stays — tell me where to pick it up.*.

**The thread pins itself to the bottom while a run is in flight.** `scrollAgentDown`
takes two `requestAnimationFrame`s so the new block has been laid out before `pinAgentBottom`
measures; the hop is smooth under 85 % of the viewport and an instant jump above it,
which is what stops a large block leaving the status line below the fold. `startAgentPin`
re-pins every 140 ms until the run ends, and the pinning stands aside the moment the reader scrolls
more than 40 px up (`onAgentWheel`), taking it back under 40 px (`onAgentScroll`).

**Proposed next steps arrive only once the agent has stopped.** `nextActs` reads the last
reply's own `next`, at most three, and draws nothing while `agentThinking` or `demoRun` is set;
`NEXT_DEFAULT` is what a reply naming none falls back to. Each chip — and each starter
chip beside it — is a `role="button"` carrying an `aria-label` that names the ask, with Enter and
Space bound and a focus outline.

### The answer is blocks, and three of them are actionable

An answer is not prose with buttons under it. `bkView(b, key, isMobile)` draws one block,
and the catalogue is the Discover one: `TIMELINE`, `EVIDENCE`, `THREAD STATE`, `FACT TABLE`,
`ATTACHMENTS`, `DRAFT`, `EVENT PROPOSAL`, `TASK PROPOSAL`, `SUGGESTED ACTION`. What a conversation
adds is that the last four carry controls, and therefore a phase.

| State | Flag | Reveal |
|---|---|---|
| Pending | `phase === "pending"` | the block's own `phase`, or `bkPhase[key]` once something was pressed — an accent card (`--accent-soft` on `--accent-line`) with a `help` chip reading *needs confirmation* and the type's controls under a rule |
| Accepted | `accepted` | `acceptBk` — the controls go, a `check_circle` chip reads *done*, and one result line states what happened and at what time |
| Accepted but not done | `failed` | the block carries `failed` — an `error` chip reading *not done*, and *Try again* as the only control |
| Declined | `isDeclined` | `declineBk` — the card collapses to a dashed outline holding one struck line, *Declined — nothing was done*, and its `nav` rows go with it |
| Loading | `isLoading` | the block's `state` is `loading` — three skeleton bars |
| Partial | `isPartial` | `state` is `partial` — an `hourglass_top` note, `partialNote`, under the body it did return |
| Empty | `isEmpty` | `state` is `empty` — a per-type icon and sentence, `event_busy`, `task_alt`, `edit_note` or `find_in_page` |
| Error | `isError` | `state` is `error` — `errorText` and a *Try again* control that `retryBk` puts back to `ready` |
| Editing a draft | `editing` | *Edit here* — `editBk` swaps the paragraphs for a `textarea` inside the conversation, and the controls become *Send* and *Stop editing* |

**The controls are the type's.** `EVENT PROPOSAL` takes *Add to calendar*, *Another time* and
*Decline*; `TASK PROPOSAL` takes *Add* and *Decline*; `DRAFT` takes *Send*, *Edit here* and
*Discard*; `SUGGESTED ACTION` takes the block's own `cta` and *Decline*. Each is a `role="button"`
with its own `aria-label` naming the object, and on touch they stack full width at 44 px.

**A phase is not stored in the message.** `bkPhase`, `bkState`, `bkResult` and `bkEdit` are keyed by
`<conversation>#<message>#<block>` (`setBk`), which is what lets the seeded histories in
`SEED_CONVS()` stay immutable and lets the same block object be proposed twice.
*Another time* is the case that shows why: `anotherTimeBk` declines the block, writes the
user's own line into the conversation, and comes back with the same proposal at a new hour, pending
again.

**The starter chips open a seeded conversation** rather than asking the question (`agentChips`). Seven conversations are seeded, and two exist for states nothing else reaches:
`c-states`, whose blocks came back loading, error, empty, partial and failed, and `c-invoice`, which
is archived. Four chips open one of them; the fifth is *Real example*, which carries `play_arrow` and
plays the script above instead of opening anything.

### States the blocks screen holds and the prototype does not

`ai-blocks.dc.html` draws the catalogue rather than a conversation, so it is where a
phase is read against its siblings. Thirteen `PROPOSAL_SPECS` cards stand
under the heading *Proposal blocks — the Agent's three phases*, two to a row, one per
type and phase. Its `state` property — `ready`, `loading`, `partial`, `empty`, `error`, `offline` —
drives every card at once, so the empty and error wording of each type is reachable there and
nowhere else. It also states the rule for which types earn a proposal block at all: `EVENT` and
`TASK` exist only because the object has to be seen and corrected before it is approved, and moving
to a folder, marking as read, adding to a case and closing a case stay a one-line `SUGGESTED ACTION`.

## Chrome that stands over every screen

| State | Flag | Reveal |
|---|---|---|
| Phone composition | `isPhone` / `notMobile` | width alone |
| Single pane | `isMobileState` | `singlePane` |
| Screen FAB | `showScreenFab` | `isMobile` or the toolbar forced it, **and** no dialog is open |
| Mail FAB | `showFab` | no composer, no document, no selection; on one pane it needs the list |
| User menu | `userMenuOpen` | the avatar |
| Settings | `settingsOpen` | the settings control; closing it also drops the account draft. It clears `acctDel` too, which nothing sets any more — removing an account left the design and that one line stayed behind |
| Settings: its five tabs | `tabProfil` / `tabAccounts` / `tabAi` / `tabNotif` / `tabApp` | the tab strip — *Profile*, *Accounts*, *AI*, *Notifications*, *Application* (`settingsTabs`), `tabProfil` being the default |
| Own photo | `meHasPhoto` | a photo was uploaded |
| **Photo rejected** | `photoError` | a file over 1 MB — a real file has to be chosen, so **no click reaches this** |
| Telemetry off | `telemetryOptOut` | the telemetry switch |
| Notification centre | `notifCenterOpen` | the bell, or the phone's upward gesture |
| Unread notifications | `notifHasUnread` | the unread count; it gates three regions including the rail badge. The panel's own badge is one composed string, `notifNewLabel`, reading *1 new* or *N new* |
| **No notifications** | `notifEmpty` | the shown list is empty — reachable by filtering to unread after reading them all |
| Refresh | `refreshAll` / `refreshBusy` | the `refresh` control in the rail, titled *Refresh*, and its floating copy at the phone's bottom-left — it re-reads the mail in front of the reader and the notification centre, pulses its glyph (`mfpulse`, 1 s) rather than spinning while it waits, and refuses a second press until the re-read lands |
| Move dialog | `moveOpen` | the move action on a selection — grouped by mailbox (`moveGroups`), each group ending in *New folder here*, and the toast it produces names the mailbox |
| Permanent delete | `confirmDel` under `inTrash` | a delete action while the open folder is a Trash — *Delete permanently*, no undo offered, and the toast reads *Permanently deleted* rather than *Moved to trash* |
| The rail's nav scrolls | `railNavStyle` | a viewport too short for the seven destinations — the nav scrolls and the bottom cluster stays pinned |
| Cancel confirmation | `cancelAskOpen` | closing a toast that carries a running operation |
| Toasts | `toasts` | any operation that reports; `toasts.dc.html` is the whole of it |

Every label in the prototype is English, among them the Settings tabs *Profile* and *Application*, the user menu's
*Sign out*, and the single-pane state toggle beside the thread head, *Hide panels — correspondence only* against
*Show thread panels* — and `design/parity.json` presses two of those under their English names.

### The settings panel

Added on 2026-09-11, where the panel had held two tabs and a fixed card.

The card itself is two shapes. On one pane it fills the screen; on two it is `st.settingsW || 700`
by `st.settingsH || 720` with a `south_east` grip in the bottom-right corner
(`settingsResizable` / `settingsResizeDown`) that drags it between 560 × 420 and the
viewport less 40 px. **The grip exists on two panes only**, so a phone or fold capture draws none.

The card also **moves**. Its header is the handle: `settingsHeadDown` is `null` on one
pane and a pointer drag on two, so the header carries `cursor:move;user-select:none` there and the
card is drawn at `transform:translate(settingsDX, settingsDY)`. The travel is clamped to half the
free space in each direction, so the card cannot be dragged past the viewport edge, and the close
control is marked `data-nodrag` so pressing it closes the panel instead of starting a drag.
*Reset layout* clears the offset with the sizes.

| State | Flag | Reveal |
|---|---|---|
| The account list | `acctListShown` | the *Accounts* tab with no draft open — one row per account carrying a colour dot, the display name, the address, `IMAP host:port · SMTP host:port` and `chevron_right`, under a `MAIL ACCOUNTS` caption beside `acctCount`, and under the rows the sentence that settles what this tab is: *Accounts are set up by your administrator — here you can open one and change its settings.* |
| The account editor | `acctEditShown` | a row. `acctTitle` reads *Edit account* and nothing else, there being no second way in |
| The address is fixed | the `E-MAIL ADDRESS` input | it is `readOnly` with `tabIndex` `-1`, drawn on `--bg` rather than `--sub`, under *The address identifies the account and cannot be changed here.* |
| Save refused | `acctSaveStyle` | the display name or the address is empty — the button is drawn flat and refuses the pointer |
| The password shown | `acctPassType` / `acctPassIcon` | the `visibility` control beside the field |
| Login left empty | `acctLoginHint` | nothing typed in LOGIN — *Empty = the e-mail address is used as the login.* |
| Connection test refused | `acctTestBad` | *Test connection* while a host, a port, a login or a password is missing — it names every field still empty |
| Connection test passed | `acctTestOk` | *Test connection* with all six filled — it names the two hosts |
| An earliest date set | `acctEarliestClearShown` / `acctEarliestHint` | the date field — the hint states that older mail stays on the mail server, and reverts to *No limit* when cleared |
| The folder mapping | `acctFolders` | six rows pairing Inbox, Sent, Drafts, Archive, Spam and Trash with `INBOX`, `Sent`, `Drafts`, `Archive`, `Junk` and `Trash` |
| The three switches over what it deletes and sends | `acctHardDelete` / `acctPurgeGone` / `acctSaveSent` | each is on by default and each carries a hint that changes with its position — hard delete states that a message goes from both servers with nothing left to restore |
| The secret check | `acctSecretScan` / `acctSecretScanWarn` | the account editor's own switch, under `BEFORE SENDING TO THE MODEL — THIS ACCOUNT ONLY`, on by default; while it is on, a warning states the check is best-effort and can both miss a secret and hide ordinary text |
| The AI language | `aiLangOptions` / `aiMatchReply` | *AI* — English or Polski, **English first and English selected** (`st.aiLang` falls back to `"en"`), separate from the interface language, with a note that a change applies only to content written from now on, and *Match the language of the message* beneath it. `langOptions`, the interface language in the account menu, is drawn and defaulted the same way |
| Notifications cleared on read | `notifAutoClear` | the *Notifications* tab's switch; while it is on, a `REMOVE` select offers Right away, After 1 hour, After 24 hours and After 7 days |
| Notified on this device | `notifDevice` | the *Notifications* tab's `THIS DEVICE` group, off by default — *When the window is not in front of you, this device tells you how many things arrived and of what kind — never from whom, never what it is about.* |
| How long one stays up | `notifDuration` / `notifDurationLabel` | the range beneath it, 1 to 30 whole seconds and 5 by default, drawn as `<n> s` in the accent; its copy ties the value to the undo window, which *waits exactly as long as its notification is up* |
| The message view | `viewAiStyle` / `viewSimpleStyle` / `viewHtmlStyle` | *Application* — a `MESSAGE VIEW` strip of three, *AI simplified*, *Simplified* and *Original* in that order, each drawn by one `segStyle` helper that is the only place the segment's own styling lives |
| Images loaded automatically | `autoImages` | *Application* — while it is on, a warning states the image is fetched from the sender's server and is how tracking pixels work |
| Layout reset | `resetLayout` / `resetLayoutDone` | *Reset layout* under `LAYOUT ON THIS DEVICE`; the confirmation *Layout restored to the defaults.* stands for 3 200 ms and then goes |

**An account is neither added nor removed here any more.** The dashed *Add account* row, `acctAdd`,
*Remove this account* and the two-step removal dialog that made the name be typed out are all gone
from the source, so the *Accounts* tab opens an existing account and changes its settings and does
nothing else. A client screen offering either act is offering something the design does not have.

**The secret check is per account, and the AI tab says so rather than holding it.** The switch and
its warning moved into the account editor, and where the global switch stood the *AI* tab now draws a
`shield` note reading that the check *is set separately for each mail account — open
Accounts and pick the account*. Which is why the editor's own group is captioned
`BEFORE SENDING TO THE MODEL — THIS ACCOUNT ONLY` and its copy says *from this account*.

The message view is **three** options rather than two, drawn *AI simplified*, *Simplified*,
*Original* — the last reading **Original** and not *HTML*. `msgView` carries `"ai"`, `"simple"` and
`"html"` behind them, and *Simplified* is what an unset value draws: `viewSimpleStyle` is on whenever
`msgView` is neither of the other two, rather than testing for its own value.

`viewHint` changes with the choice and `htmlModeWarn` does not: the warning, the inline original and
the *Show the original message?* confirmation all still turn on `msgView === "html"` alone, so
*AI simplified* reveals none of them and behaves as *Simplified* does everywhere but the hint. That
hint is the panel's longest copy and states what the view is: the same deterministic cleanup as
*Simplified*, with AI deciding only what to keep and what to drop, never rewriting a word, so the
text stays exactly as the sender wrote it and only the presentation changes — usually cleaner than
plain *Simplified*, especially on newsletters and long reply chains.

### What a refresh turns up, and which row animates

*Refresh* is a product control (the table above), and what a re-read turns up in the prototype is one
scripted batch out of three (`DEMO_BATCHES`) — a message lands at the top of the list and
in the notification centre, a second after 1 000 ms an existing thread is replaced in place and its
reading held back, and a third after 2 050 ms is simply no longer in the list, with the toast *The
sender withdrew a message — the thread has left the list*.

**The batch is the prototype's stand-in for live data, not a state to build.** What the client owes
here is the states it reveals, each of which the client reaches from its own live data:

| State | Flag | What it draws |
|---|---|---|
| A row arriving | `rowAnim[<id>] === "in"` | `mfrowin` — 480 ms, opening from zero height with a 7 px drop |
| A row deleted | `rowAnim[<id>] === "outdel"` | the collapse below, and `mfrowdel` over it — 460 ms, an error bar and an error wash that come up and stay while the row goes |
| A row archived | `rowAnim[<id>] === "outarc"` | the same collapse, and `mfrowarc` — the warning bar and wash, otherwise identical |
| **A row removed with no act named** | `rowAnim[<id>] === "out"` | the collapse alone, with no wash — the fallback `animateRowsOut` takes when no act is passed, and **all four of its callers pass one**, so nothing in the prototype reaches it |
| A row moved to a folder | `rowAnim[<id>] === "move"` | `mfrowmoved` — 900 ms, a warning wash that comes up and fades back out; the row stays, so nothing collapses |
| A row changing in place | `rowAnim[<id>] === "edit"` | `mfrowedit` — 1 700 ms, an accent bar and a soft accent wash that fade out |
| A notification arriving | `notifAnim[<id>] === "in"` | the same `mfrowin` on the notification-centre row |

The collapse the three removals share is `mfrowout` — 440 ms, closing to zero height and sliding
22 px left, pointer events off while it goes — and the state change itself lands at 380 ms, before
the wash has finished (`animateRowsOut`). The wash is drawn on the row rather than on the
wrapper that collapses (`rowWash`), which is what lets the two run at different lengths.

The comment above them is the requirement rather than the animation: *Only the touched tile
animates — the list is never re-rendered as a whole, so scroll position and every other row stay
exactly where they were.*

**The three `out` states belong to a removal the person performed** — archive, delete, delete
permanently, and the swipe that archives: the row collapses under the gesture that removed it, and
then the state change lands. A row that goes because a re-read returned a list without it plays
nothing at all, since that arrives as a new list rather than as an act — which is why the batch's
third row leaves without it. An undo pressed while the row is still leaving cancels the removal that
was pending.

**The wash names the act, and the colour is the whole of the naming**: the error pair for a deletion
and for a permanent deletion, the warning pair for an archive. The swipe that archives draws the same
warning colours behind the row as it is dragged (`swipeBgStyle`) rather than the success
green it used to, so the gesture and the wash that follows it are one colour rather than two. A move
is the one act that washes a row it does not remove: the row keeps its place under its new folder
chip while `mfrowmoved` fades back out behind it.

## Sign-in — `sign-in.dc.html`

Two views in one file, `isLogin` and `isServer`, and the server view is unreachable when the address
is forced by configuration.

| State | Flag | Reveal |
|---|---|---|
| Sign-in | `isLogin` | the opening view |
| Server address | `isServer` | *Change server* under *Advanced* — and `envLocked` refuses it |
| SSO offered | `showSso` | the server declares it (prop `ssoAvailable`, on by default) — the accent primary button above everything else, `shield_person` and `open_in_new` around `{{ ssoLabel }}`, under a hint reading *<provider> opens in your browser.* The provider's name is the prop `ssoName`, *MailFathom SSO* by default |
| SSO handing off | `ssoBusy` / `ssoIdle` | pressing it — the two glyphs give way to the spinner, the label becomes *Opening <provider>…*, the button drops to `opacity:0.85`, and the hint becomes *Finish signing in in the browser window — this screen picks up the session when it returns.* It returns to idle after 2 000 ms |
| SSO, then something else | `showSsoDivider` | SSO offered beside OAuth or a password — the divider reads *or another method* |
| OAuth offered | `showOauth` | the server declares it (prop `oauthAvailable`) |
| Password offered | `showBasic` | the server declares it (prop `basicAvailable`) |
| Both, so a divider | `showDivider` | OAuth and a password both — this one reads *or with a password* |
| **No method at all** | `noMethods` | none of the three — a **prop triple** now, so only a property change reaches it |
| Kept signed in | `remember` | the *Keep me signed in* checkbox, drawn under the password field and **only where the password form is** — checked it reads *This device stays signed in for 30 days. Applies to password sign-in only.*, unchecked *Applies to password sign-in only — provider sessions follow their own rules.* |
| Connecting | `connecting` | pressing *Connect*, or an OAuth provider |
| Advanced open | `advanced` | the *Advanced* control |
| Insecure allowed | `insecure` | the checkbox on the server view; it changes the lock glyph, the port hint and the certificate row |
| Server was customised | `customized` | a non-default address or the TLS exception; it reveals *Restore defaults* |
| Probing an address | `probing` | *Connect* on the server view |
| An error | `hasError` | an empty login, an empty password, or an address with no host |
| Theme is auto | — | `themePicks` carries `auto`, `light`, `dark`; `auto` resolves through `prefers-color-scheme` |

The layout collapses twice rather than once: `stacked` at `w < 940` puts the brand above the form,
and `phone` at `w < 700` changes every target size. The pitch beside the form (`showPitch`) exists
**only when not stacked**.

## Toasts — `toasts.dc.html`

Six kinds — `neutral`, `success`, `error`, `warning`, `info`, `loading` — and the kind decides the
glyph, the colour and the bar. What is worth reading in the source rather than in a screenshot:

- **A `loading` toast is sticky.** Its close control does not dismiss it; it asks whether to abort
  the operation, and aborting posts a `warning` toast saying what was and was not saved.
- The stack is newest-first and capped at `maxStack` (4 by default); past it the oldest is dropped.
- Resting opacity is 0.8 and 1.0 under the pointer, so the content beneath stays readable.
- The **blocking overlay** is a separate thing from a toast: determinate (a percentage) or
  indeterminate, no dismissal by scrim or Escape, and its *Cancel* asks before it aborts.
- On a narrow screen the stack is full-width at the top instead of a 400 px column on the right.

## The notification gesture — `notification-gesture.dc.html`

Numbers the implementation reads, not decoration: 1 : 1 finger tracking with no easing, scrim opacity
**equal to** the travelled fraction, a **0.32** distance threshold, a **0.5 px/ms** velocity
threshold, a **260 ms** `cubic-bezier(.32,.72,0,1)` spring back, a **12 px** navigation-bar slop and a
**10 px** row slop that cancels the 420 ms long press. Three hand-overs are stated: a scrolled list
gives the gesture up at its own top *without lifting the finger*, a row's long press and the drag are
mutually exclusive, and the navigation bar hands over past 12 px and cancels its own tap.

**Phone composition and `pointer: coarse` only.** The fold, the tablet and the desktop keep the panel
they draw today, and there is no drag gesture there at all.

## Mail search — `mail-search.html`

Five states, all drawn side by side in the file: results, empty, waiting, a lexical-only banner, and
a failure banner. Worth carrying out of it:

- The result row **is** the folder row, and the third line — whose height is reserved always — carries
  the reason for the match.
- An empty result never stands alone: the scope that produced it stands beside it, with one press
  that widens it.
- *This server does not search by meaning* and *you may not see this* are different sentences and are
  never merged.
- Earlier searches live only in client state and go with the credential; no typed phrase reaches a log
  or telemetry.
- The field follows what the deployment can do. A deployment that reads phrases gets *Search, or
  describe what you need*, the ranking row *What this search is ranked by*, and one line naming what
  the sentence could not use. A words-only deployment keeps *Words from the message you are looking
  for*, and the ranking row never appears. The file points at `m_search` in the Client States file for
  both, which is where they are drawn.

## Client states — `client-states.dc.html`

It draws the states the client renders and the prototype does not: refusals, failures, waits, empties,
and the in-flight states of a run. Each one is drawn at the full width of each composition, with the
navigation rail in place. The prototype carries the same list as a comment block right after its
Markdown helpers, so a session reading the prototype finds its way here.

**How a state is reached.** Three component properties decide what the artboard draws:

| Property | Values | Default |
|---|---|---|
| `state` | `all`, or one state id below | `all` |
| `layout` | `all`, `telefon`, `fold`, `tablet`, `desktop` | `all` |
| `theme` | `light`, `dark`, `both` | `dark` |

`state: all` draws a ledger first: one row per item, with a *Drawn*, *Changed* or *Merged* verdict and a
sentence. Clicking a row sets the same focus the property does. Each state's gate is
`show.<id>`, set in `renderVals()` from the `STATES` table. The ledger is `LEDGER`. The compositions use the prototype's four sizes — `W` and `H` — and
`f.notPhone` hides the side columns at `telefon`.

It reuses the prototype's tokens, notice box, chips, buttons, skeleton shimmer, toast, confirm dialog,
and *Nothing open* layout. **The one new component is the radio list in *Kind of folder*.**

### Discover

| State | Gate | What it settles |
|---|---|---|
| `d_refusal` 1.1 | `show.d_refusal` | A question refused before a run is one line directly under the field, above the scope chips — never a toast, never a card, because no run exists. The question stays in the field. Signed out and not permitted read neutral; unavailable and unreadable read as a warning with *Try again*. On a phone the action drops under the text at 44 px |
| `d_cancel` 1.2a | `show.d_cancel` | The running bar keeps its slot through running → cancelling → ended. While cancelling, the pulse becomes a muted spinner and *Cancel run* becomes a disabled *Cancelling…*. A failed stop turns the bar into a warning with *Try stopping again*, while blocks keep arriving underneath |
| `d_ended` 1.2b | `show.d_ended` | The ending card replaces the running bar in the same slot, above the blocks that arrived, and nothing below it moves. The answer that arrived gains a *cut short* chip. Eleven reasons fall into five tones (`ENDINGS`): *Stopped by you*, *Limit reached* (period and run allowance), *Try again* (timed out, temporarily unavailable), *Could not finish* (failed, gone, stopped) and *Not possible here* (unavailable, retrieval refused). Only the period allowance carries the when-line |
| `d_finished` 1.2c | `show.d_finished` | A completed run gets **no card**. The bar leaves, and *Answered by {endpoint} ({model})* and what the run spent go into the run line beside the plan chip. The allowance count shows only where the deployment meters questions |
| `d_coming` 1.3a | `show.d_coming` | One dashed card trails the last arrived block. Loading is the prototype shimmer. Error and offline keep the dashed outline and swap the shimmer for a sentence, and **only offline offers Retry** |
| `d_empty` 1.3b | `show.d_empty` | *Nothing composed* is a plain card with no button, because asking again gives the same result. A newer-version answer (the whole answer) and an unknown block (one block among others) share one dashed neutral card |
| `ev_opening` 1.4a | `show.ev_opening` | The inspector header paints at once from what the citation knows. Only the body waits, as shimmer with *Opening the source…*, and the footer appears once the source opens. On a phone the inspector covers the screen |
| `ev_failed` 1.4b | `show.ev_failed` | The failure takes the quote's place under the header. Unavailable and unreadable are faults (warning, *Try again*); signed out and missing are conditions (neutral). There is no footer |
| `ev_private` 1.4c | `show.ev_private` | The lock sits in the badge slot the inspector and evidence list already have, and the body names the mailbox and points to the administrator. **A "not permitted" read failure renders exactly this** — the ledger marks 1.4 *Merged* for that reason |
| `ev_outdated` 1.4d | `show.ev_outdated` | The warning comes first. Below it is the quoted passage in a plain box, not the highlight, because it can no longer be pointed to. *Open in mail* stays |
| `ev_kinds` 1.4e | `show.ev_kinds` | A whole-message citation shows the message unhighlighted, with one line saying so. An attachment file row, a source the client cannot open, and text recognised in an image each replace only the body; header and footer keep their places |
| `d_asked` 1.5 | `show.d_asked` | *Asked before* is kept, **only while the field is idle**, under the scope chips. A chip carries the question and its scope, and tapping it asks again. The stored answer and its freshness are deliberately not shown, and the ledger says `savedAnswers.a` and `.fresh` can go. *Forget these* clears the row on this device without confirmation |

### Mail

| State | Gate | What it settles |
|---|---|---|
| `m_verdict` 2.1 | `show.m_verdict` | The sender verdict sits directly under the sender line, above the body. A warning is the prototype's notice box; **a healthy verdict is one quiet line**. On a phone both wrap under the sender line at full width |
| `m_more` 2.2a | `show.m_more` | The paging control ends the thread, with how much has been read beside it. While loading it becomes a status line in the same place, and once everything is read one faint line closes the thread. On a phone the button is 44 px and the count drops underneath |
| `m_partial` 2.2b | `show.m_partial` | A partial failure sits **where the missing messages would be**, between the ones read. The two head notes go under the subject: *more people* in muted ink, *more messages than were assembled* as a warning line |
| `m_pane` 2.2c | `show.m_pane` | Failed, offline and *nothing you are allowed to see* reuse the *Nothing open* layout, with an icon for the logo and one action where one exists. The list stays usable beside it |
| `m_folders` 2.3a | `show.m_folders` | The folder column loads as **a skeleton shaped like the tree**. *Reading mailboxes and folders…* stays only as live-region text for screen readers. Failed, offline (the last tree dimmed) and *No account configured* are drawn too. The column is in place on desktop, a drawer over the list on fold and tablet, and full screen on a phone |
| `m_list` 2.3b | `show.m_list` | The list's empty states are centred in the column, one sentence each. *Nothing matches* names what is narrowing and offers *Clear filters*. A partial failure is a warning strip above the rows that did arrive. An empty list that also failed says the folder could not be read — never that it is empty |
| `m_kind` 2.4a | `show.m_kind` | When a mailbox lacks a role folder, *Kind of folder* becomes **the first field**, as a radio list. Picking a role folds *Folder name* and *Inside* away and puts the hint in their place, and *An ordinary folder* brings them back. On a phone the dialog is full screen |
| `m_roles` 2.4b | `show.m_roles` | Flagged (`flag`), Important (`label_important`), All mail (`all_inbox`) and Outbox (`outbox`) join the standard folders in reading order: what needs you, what you wrote, then everything. Outbox shows what is waiting rather than an unread count, and only while something waits |
| `m_search` 2.5 | `show.m_search` | A described search turns the sentence into two kinds of chip. Filters keep the Mail Search chip (rail tint) and decide what is in. Ranking criteria get their own labelled row in accent tint with a sort icon, and each one's × reads *Stop ranking by {criterion}*. One muted line under both names what was not used. *Reading what you wrote…* and the words-only field are drawn below |
| `m_pending` 2.6 | `show.m_pending` | **The connection summary itself becomes the indicator**: its dot and sentence take the most urgent state, and clicking it opens a panel under it — a bottom sheet on a phone, where the summary is a chip at the right of the header. The panel lists accounts first, then changes on their way, most urgent first. Toasts still announce each action, and a failure landing after its toast is gone raises one warning toast whose action opens the panel |

### Tasks, Calendar and People

| State | Gate | What it settles |
|---|---|---|
| `c_edit` 3.1 | `show.c_edit` | *Edit* turns the open event's dialog into the form in place: same dialog, title *Edit event*, *Cancel* and *Save*. Starts and Ends each take a day and a time, and *All day* hides both times. *Save* stays disabled until something changes and says why when the times conflict. **The new-event form should take the same fields** |
| `t_layout` 3.2 | `show.t_layout` | *Lay out today* happens inside the capacity panel, which grows to hold the offer. Nothing is written until *Add to calendar*, and what did not fit is named, not only counted. The panel draws waiting, the offer, nothing left and allowance spent. Unavailable hides the button and keeps the capacity line. On a phone the panel sits above the list |
| `t_proposed` 3.3a | `show.t_proposed` | A proposal has **no checkbox**; the AI tag takes its place. *Accept* is on the row, and the menu has *Accept* and *Dismiss task*. Undated tasks gather under *No day* at the end. The status line sits between the list head and the first group and is replaced by the next change; a partly refused change is a warning line |
| `t_states` 3.3b | `show.t_states` | The two empties point at different things — nothing at all, versus everything done and hidden (naming *Show done*). Loading is the list skeleton; reading more is a spinner line at the foot |
| `p_none` 3.4a | `show.p_none` | From fold upward the detail pane shows *Nothing open* with one sentence before anyone is picked. A phone shows the list alone. After a change, one status line sits under the name: green when it worked, red when refused |
| `p_states` 3.4b | `show.p_states` | Each address book says why it is empty in its own terms. Loading and failure look the same for the book (in the list) and for correspondence (in a person's *Conversations*), and each fails on its own |

### Agent

| State | Gate | What it settles |
|---|---|---|
| `a_steer` 4.1 | `show.a_steer` | While a run is going, *Send* reads **Steer**. The note appears as a user turn tagged *Joins at the next turn*, then *Taken in*, and the status line names what it is taking in. *Cancel* is unchanged and still stops the whole answer, note included |
| `a_failed` 4.2a | `show.a_failed` | A failed answer keeps what it wrote and ends with an error notice in the flow, with *Try again*. A message that was not sent never enters the conversation: it stays in the field, and one red line under the field says why, for any of five reasons |
| `a_read` 4.2b | `show.a_read` | A conversation that could not be read fills its pane with the *Nothing open* layout, and all five reasons share it. History reads and fails inside its own column (a drawer on smaller compositions). Refused deletes, archives and proposal decisions use the prototype's error toast. Deleting several uses the confirm dialog with a plural count |

## States the source has and a preview does not reach

This is the class the inventory exists for. Each is in the source, and none of them can be reached by
clicking a preview.

- **The application with no mailbox at all.** `const emptyApp = false;` is a constant, and
  every one of the seven screen gates is `st.screen === "…" && !emptyApp`. Nothing anywhere draws the
  true branch, so **the design does not cover a deployment with no mailbox** — a session that needs
  that screen is designing something the design has not settled, and it is drawn in `design/` first, in a
  change of its own, rather than invented in the client. The folder column alone has one: `m_folders` in the Client States file draws
  *No account configured*, which is the column's state and not the application's.
- **A rejected profile photo** (`photoError`). Set only by choosing a file over 1 MB; no control in
  the prototype can produce one.
- **Every tab state** (`tabsBarShown`, `noTabsOpen`, `convBarShown`, `closeAllAsk`). Two conditions at
  once: the user menu's *Tab mode* switch turned on and a width of at least 1180 px. The initial state
  is `classic`, so a preview opened at any width shows none of it.
- **The run trace** (`showTrace`). A component property, off unless the property is set.
- **No sign-in method offered** (`noMethods`). Three properties turned off together — `ssoAvailable` joined `oauthAvailable` and `basicAvailable`.
- **A case or a person with no documents** (`caseNoDocs`, `personNoDocs`). No mock record has an empty
  `docs`, so both empty states are drawn by no preview at any click.
- **No proposals left** (`calNoProposals`). Reachable only by dealing with every proposed slot first.
- **A conversation archive** (`hasArchived`, `archiveSectionOpen`). Needs a conversation archived first.
- **The `fold` composition's own case.** 884 px is `tablet` and `!singlePane`: touch, drawers, and two
  panes at once. Neither the phone artboard nor the desktop artboard shows it, and no click produces
  it — only a width does.
