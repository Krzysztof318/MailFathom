# The design's state inventory

**Mirror stamp `a97698feb767efae`** — the etag set this was extracted from. Run
`bash scripts/design-mirror.sh stamp`; a different answer means the design moved and this file
describes something older than the mirror beside it. Refresh with `$mf-sync-design` rather than reading
around it.

Everything here is read out of `design/files/`, which is the design project's screen
sources byte for byte. **This file is the source a session builds a screen from; a rendered preview
is not.** A preview shows the one state it was clicked into, and most of what is below is invisible
until something reveals it — which is exactly the reading a screenshot cannot give.

This file and the sources it was extracted from are tracked, so every clone reads the same design;
`design/README.md` says what the four parts of the mirror are and what writes them.

## What the six files are

| Mirrored file | What it settles |
|---|---|
| `MailFathom Prototype.dc.html` | The whole signed-in application: seven screens, every overlay, every composition. 8 749 lines, and the only file that carries navigation |
| `MailFathom Sign-in.dc.html` | Sign-in, and the server screen behind it |
| `MailFathom Toasts.dc.html` | Toasts and the blocking overlay — the system's answer to what somebody just did |
| `MailFathom Notification Gesture.dc.html` | The phone's notification-panel gesture, as four frames plus the numbers behind them |
| `MailFathom Result Blocks.dc.html` | The Discover answer's block types |
| `MailFathom Mail Search.html` | Mail search: results, empty, waiting, lexical-only, failure |

What the mirror carries is the list written into `scripts/design-mirror.sh` — those six files and
`support.js`, the generated `dc-runtime` bundle that boots them. Everything else the project holds is
left where it is: `deck-stage.js` is a copied slide-deck starter, and the rest are images. The
manifest still records every one of them, so a file arriving or leaving is visible in the diff, and a
new one is reported by `bash scripts/design-mirror.sh plan` rather than copied.

## How a state is named here

The prototype is one component. A screen is a region of one template gated by a flag, and every flag
is computed once in `renderVals()`. So a state's name in this file **is** the flag name in the
source: `grep -n 'showThreadPane' 'design/files/MailFathom Prototype.dc.html'` gives both
the gate in the template and the one line that decides it. *Reveal* is what makes it true.

Three things decide composition before any state does, and they are read from the width alone
(`renderVals`, lines 6038–6059):

| Name | Condition | What it changes |
|---|---|---|
| `isMobile` | `w < 700` | Bottom navigation, drawers, touch sizing |
| `tablet` | `700 ≤ w < 1180` | Touch and drawers, toolbar controls without labels |
| `singlePane` | `w < 820` | One pane at a time — the list and the content never stand together |
| `narrow` | `700 ≤ w < 1020` | The narrower desktop composition |
| `touch` | `isMobile \|\| tablet` | Every `pointer: coarse` affordance |
| `drawer` | `tablet \|\| isMobile` | The side list arrives as a drawer rather than a column |

`FRAMES` (line 3437) is the design's own four sizes: `phone` 390 × 844, `fold` 884 × 832,
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

Which blocks a run produces is the plan, and there are three (`PLANS`, line 3011): `contract`
(answer + timeline + table + action), `files` (answer + gallery + action), `people`
(answer + people + action). **No single run draws every block**, so a screenshot of one plan is not
the block set to build against; `MailFathom Result Blocks.dc.html` is where the block types stand
together.

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
| Side list resizer | `showSideResizer` | `!drawer && !sideCollapsed && !isMobile` (line 7071) — drag widens the folder pane between 210 and 420 px, double-click returns it to 210 |
| Side list as a drawer | `showSide` / `sideScrim` | `drawer && drawerOpen` |
| Folder button | `showFolderBtn` | `drawer` |
| Account menu on a mailbox | `ctxMenu` through `g.ctxOpen` | right-click a mailbox header, or hold it for 460 ms — *New folder* and *Mark all as read*; **All accounts carries none** |
| Menu on a folder row | `folderMenu` (line 4940) | right-click a folder, or hold it — *Mark all as read* always, *New folder inside* while the path is under three segments, and *Edit folder* and *Delete folder* only on a folder the user made; **All accounts carries none** |
| New folder row | `g.canAdd` (line 7092) | drawn at the end of every account group as a `create_new_folder` row reading *New folder* — `g.key !== "all"`, so the unified view carries none, having no mail server of its own; a collapsed side list draws the glyph alone (`g.expanded`), and the row takes the phone's taller padding under `isMobile` |
| A folder the user made | `st.extraFolders[<mailbox>]` | the row above, the two menus above it, or *New folder here* in the move sheet; it is drawn below the standard set |

An account draws six folders in the same order — Inbox, Sent, Drafts, Archive, Spam, Trash
(`STD_FOLDERS`, line 6331) — and a count only where the mock data gives one. **The unified view
draws three of them.** `STD_FOLDERS` takes a `unified` flag, and the last three carry `perAccount`:
*All accounts* is Inbox, Sent and Drafts alone, because Archive, Spam and Trash are always opened in
the account they live in rather than merged across accounts. Anything under either set is a folder
the user made.

### Folders the user made

Added on 2026-09-10. A folder is no longer a name in a flat
list: it nests, it is placed somewhere in the account's own tree, and it can be edited and deleted.

| State | Flag | Reveal |
|---|---|---|
| The folder tree | `st.folderOpen[<mailbox·path>]` | a folder with children draws a twisty; each level is indented 15 px, and **three path segments is the ceiling** (`MAX_DEPTH`, line 4953) |
| Children shown | `mb.hasKids` / `mb.twisty` | `expand_more` against `chevron_right`, titled *Collapse subfolders* and *Show subfolders*; a collapsed side list draws depth 0 only |
| New folder dialog | `newFolderOpen` (line 7901) | the *New folder* row at the end of an account group, *New folder inside* on a folder, or *New folder here* in the move sheet |
| Editing one | the same dialog in `mode: "edit"` | *Edit folder* — the title reads *Edit folder* and the button *Save changes* |
| Its two fields | `nfName` / `nfParent` | *FOLDER NAME*, and *INSIDE* as a select rather than a text field |
| Where it may sit | `nfLocations` (line 7935), from `folderLocations` (line 4959) | the select's options: *Top level of the account* first, then the account's folders drawn as `Parent / Child`. Editing one drops the folder itself, everything under it, and any parent too shallow to hold its subtree |
| Nothing typed yet | `nfCreateStyle` / `nfCreateHover` | the create button is drawn flat and refuses the pointer while the name is empty |
| The name is taken | the `warning` notification *Pick another name* (line 4995) | saving an edit onto a name already used in that place — *There is already a folder called “<name>” inside …* |
| Delete confirmation | `delFolderOpen` (line 7905) / `delFolderText` (line 7909) | *Delete folder* — four outcomes computed from the state rather than one sentence |

**The design's own statement about what a folder is: the user places it, MailFathom puts it on the
server.** The hint under the select reads *MailFathom creates the folder on this account's mail
server and picks where it sits there — you only choose where it shows up in your folder list.*, and
the source says why in as many words — accounts are administrator-managed and the server path is not
the user's business. So nothing about a folder the user made shows a remote path or takes one — the
account editor's own folder mapping is the administrator's setting rather than theirs — and creating,
renaming and deleting one each post a success toast naming the mailbox and the place
(`locLabel`, line 4973 —
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
| AI summary | `aiSumOpen` (line 7862) | the `auto_awesome` control on a message row, or *AI summary* at the top of that row's context menu |
| Its cards | `aiSumCards` (line 5166) | **three at most**, each a heading, the claim, a *Why:* line, a model chip reading *Model — mailfathom-email-enrichment*, and the fragment of the message it rests on |
| Which headings exist | — | *WHAT IS SETTLED*, *WHAT IS STILL OPEN*, *WHAT IS THE COMMITMENT*, *WHAT THE DEADLINE IS*, and *WHAT THIS IS ABOUT* where the list's own reading fills a card out |
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
| The AI line on one pane | `mobileStateLine` (line 8134) | `singlePane && !stateStale` — the state bar's one-line reading, on the phone |
| Reading held back | `stateStale` / `stateFresh` (line 8079) | the thread carries `stateStale`: a message joined or left the conversation after its state was derived. The state cards, the in-body chips, the one-pane AI line and the thread sheet's state list stop drawing, and each place says instead *This conversation has changed since where it stands was last derived, so that reading is held back until it is derived again.* The prototype reaches it through *Refresh*, whose second step marks the thread it changes |
| Held back, on the tablet | `staleInBody` (line 8082) | `tablet && !stateBarOff && stateStale` — the same sentence as a note at the top of the thread body, where `stateInBody` would have drawn the chips |
| Panels hidden | `st.stateBarOff` | the *fullscreen* toolbar control; nothing else sets it |
| Jump to the agent | `showAgentJump` | `!isMobile && !stateBarOff` |
| Arrived from a citation | `showCitationTag` / `cameFromResult` | reached from a Discover citation, and only for the `contoso` thread |
| Answer drawn in mail | `mailAnswer` | `st.mailAnswer` and the `contoso` thread |
| Thread actions sheet | `threadSheetOpen` | the sheet control, phone-shaped |
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
| A thread being dragged | `st.dragIds` (`dragThreadsStart`, line 5100) | a row is `draggable` and starts a drag; the dragged rows go to `opacity:0.45` while it lasts, and a drag started on a row inside a selection of more than one carries **the whole selection** rather than that row |
| A folder under the drag | `st.dragOverKey` (`dragOverFolder`, line 5111) | the pointer over a folder row in the side list while `dragIds` is set — that row draws the accent-soft background, the accent text and an `inset 0 0 0 2px var(--accent)` ring, which is the only affordance saying a drop lands here |

Dropping is a move and nothing new: `dropOnFolder` (line 5117) hands the ids to `moveThreads`,
which now takes them as an argument (line 5088) rather than reading `moveFor`, so a drag and the
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
| Thinking | `agentThinking` | a question was sent |
| Context chip | `agentCtxShown` | the agent was opened from a screen that carries context |
| Starter chips | `agentShowChips` | fewer than two messages in the conversation |
| History open | `historyOpen` | `!isMobile` by default, and the history control otherwise |
| History close | `historyCloseShown` | `isMobile` |
| History search has a value | `historySearchValue` | typing in it |
| **Archive section exists** | `hasArchived` | at least one conversation is archived |
| Archive open | `archiveSectionOpen` | pressing the archive section |
| History resizer | `showHistoryResizer` | `!isMobile` |
| Conversation bar | `convBarShown` | tabs mode with more than one conversation — see the tabs note above |
| Delete a conversation | `deleteConvAskOpen` | the delete control on a conversation |

## Chrome that stands over every screen

| State | Flag | Reveal |
|---|---|---|
| Phone composition | `isPhone` / `notMobile` | width alone |
| Single pane | `isMobileState` | `singlePane` |
| Screen FAB | `showScreenFab` | `isMobile` or the toolbar forced it, **and** no dialog is open |
| Mail FAB | `showFab` | no composer, no document, no selection; on one pane it needs the list |
| User menu | `userMenuOpen` | the avatar |
| Settings | `settingsOpen` | the settings control; closing it also drops the account draft. It clears `acctDel` too, which nothing sets any more — removing an account left the design and that one line stayed behind |
| Settings: its five tabs | `tabProfil` / `tabAccounts` / `tabAi` / `tabNotif` / `tabApp` | the tab strip — *Profile*, *Accounts*, *AI*, *Notifications*, *Application* (`settingsTabs`, line 8455), `tabProfil` being the default |
| Own photo | `meHasPhoto` | a photo was uploaded |
| **Photo rejected** | `photoError` | a file over 1 MB — a real file has to be chosen, so **no click reaches this** |
| Telemetry off | `telemetryOptOut` | the telemetry switch |
| Notification centre | `notifCenterOpen` | the bell, or the phone's upward gesture |
| Unread notifications | `notifHasUnread` | the unread count; it gates three regions including the rail badge |
| **No notifications** | `notifEmpty` | the shown list is empty — reachable by filtering to unread after reading them all |
| Refresh | `refreshAll` (line 5642) / `refreshBusy` | the `refresh` control in the rail, titled *Refresh*, and its floating copy at the phone's bottom-left (line 209) — it re-reads the mail in front of the reader and the notification centre, pulses its glyph (`mfpulse`, 1 s) rather than spinning while it waits, and refuses a second press until the re-read lands |
| Move dialog | `moveOpen` | the move action on a selection — grouped by mailbox (`moveGroups`, line 7877), each group ending in *New folder here*, and the toast it produces names the mailbox |
| Permanent delete | `confirmDel` under `inTrash` | a delete action while the open folder is a Trash (line 4892) — *Delete permanently*, no undo offered, and the toast reads *Permanently deleted* rather than *Moved to trash* |
| The rail's nav scrolls | `railNavStyle` (line 6550) | a viewport too short for the seven destinations — the nav scrolls and the bottom cluster stays pinned |
| Cancel confirmation | `cancelAskOpen` | closing a toast that carries a running operation |
| Toasts | `toasts` | any operation that reports; `MailFathom Toasts.dc.html` is the whole of it |

Every label in the prototype is English. The four that were not — the Settings tabs *Profil* and
*Aplikacja* (line 8455, now *Profile* and *Application*, with *Accounts*, *AI* and *Notifications*
standing between them), the user menu's sign-out item *Wyloguj* (line 143, now *Sign out*), and the
single-pane state toggle beside the thread head, *ukryj* (line 7845, now *Hide panels —
correspondence only* against *Show thread panels*) — were translated in the project, and
`design/parity.json` presses two of them under their English names.

### The settings panel

Added on 2026-09-11, where the panel had held two tabs and a fixed card.

The card itself is two shapes. On one pane it fills the screen; on two it is `st.settingsW || 700`
by `st.settingsH || 720` with a `south_east` grip in the bottom-right corner
(`settingsResizable` / `settingsResizeDown`, line 8433) that drags it between 560 × 420 and the
viewport less 40 px. **The grip exists on two panes only**, so a phone or fold capture draws none.

The card also **moves**. Its header is the handle: `settingsHeadDown` (line 8404) is `null` on one
pane and a pointer drag on two, so the header carries `cursor:move;user-select:none` there and the
card is drawn at `transform:translate(settingsDX, settingsDY)`. The travel is clamped to half the
free space in each direction, so the card cannot be dragged past the viewport edge, and the close
control is marked `data-nodrag` so pressing it closes the panel instead of starting a drag.
*Reset layout* clears the offset with the sizes.

| State | Flag | Reveal |
|---|---|---|
| The account list | `acctListShown` (line 8563) | the *Accounts* tab with no draft open — one row per account carrying a colour dot, the display name, the address, `IMAP host:port · SMTP host:port` and `chevron_right`, under a `MAIL ACCOUNTS` caption beside `acctCount`, and under the rows the sentence that settles what this tab is: *Accounts are set up by your administrator — here you can open one and change its settings.* |
| The account editor | `acctEditShown` | a row. `acctTitle` (line 8574) reads *Edit account* and nothing else, there being no second way in |
| The address is fixed | the `E-MAIL ADDRESS` input | it is `readOnly` with `tabIndex` `-1`, drawn on `--bg` rather than `--sub`, under *The address identifies the account and cannot be changed here.* (line 2478) |
| Save refused | `acctSaveStyle` | the display name or the address is empty — the button is drawn flat and refuses the pointer |
| The password shown | `acctPassType` / `acctPassIcon` | the `visibility` control beside the field |
| Login left empty | `acctLoginHint` | nothing typed in LOGIN — *Empty = the e-mail address is used as the login.* |
| Connection test refused | `acctTestBad` (line 8601) | *Test connection* while a host, a port, a login or a password is missing — it names every field still empty |
| Connection test passed | `acctTestOk` | *Test connection* with all six filled — it names the two hosts |
| An earliest date set | `acctEarliestClearShown` / `acctEarliestHint` (line 8611) | the date field — the hint states that older mail stays on the mail server, and reverts to *No limit* when cleared |
| The folder mapping | `acctFolders` (line 8618) | six rows pairing Inbox, Sent, Drafts, Archive, Spam and Trash with `INBOX`, `Sent`, `Drafts`, `Archive`, `Junk` and `Trash` |
| The three switches over what it deletes and sends | `acctHardDelete` / `acctPurgeGone` / `acctSaveSent` (line 8623) | each is on by default and each carries a hint that changes with its position — hard delete states that a message goes from both servers with nothing left to restore |
| The secret check | `acctSecretScan` / `acctSecretScanWarn` (line 8586) | the account editor's own switch, under `BEFORE SENDING TO THE MODEL — THIS ACCOUNT ONLY` (line 2597), on by default; while it is on, a warning states the check is best-effort and can both miss a secret and hide ordinary text |
| The AI language | `aiLangOptions` / `aiMatchReply` | *AI* — Polski or English, separate from the interface language, with a note that a change applies only to content written from now on, and *Match the language of the message* beneath it |
| Notifications cleared on read | `notifAutoClear` (line 8546) | the *Notifications* tab's switch; while it is on, a `REMOVE` select offers Right away, After 1 hour, After 24 hours and After 7 days |
| Notified on this device | `notifDevice` (line 8423) | the *Notifications* tab's `THIS DEVICE` group (line 2661), off by default — *When the window is not in front of you, this device tells you how many things arrived and of what kind — never from whom, never what it is about.* |
| How long one stays up | `notifDuration` / `notifDurationLabel` (line 8426) | the range beneath it, 1 to 30 whole seconds and 5 by default, drawn as `<n> s` in the accent; its copy ties the value to the undo window, which *waits exactly as long as its notification is up* |
| The message view | `viewAiStyle` / `viewSimpleStyle` / `viewHtmlStyle` (line 8484) | *Application* — a `MESSAGE VIEW` strip of three (line 2690), *AI simplified*, *Simplified* and *Original* in that order, each drawn by one `segStyle` helper (line 3474) that is the only place the segment's own styling lives |
| Images loaded automatically | `autoImages` (line 8464) | *Application* — while it is on, a warning states the image is fetched from the sender's server and is how tracking pixels work |
| Layout reset | `resetLayout` / `resetLayoutDone` (line 8539) | *Reset layout* under `LAYOUT ON THIS DEVICE`; the confirmation *Layout restored to the defaults.* stands for 3 200 ms and then goes |

**An account is neither added nor removed here any more.** The dashed *Add account* row, `acctAdd`,
*Remove this account* and the two-step removal dialog that made the name be typed out are all gone
from the source, so the *Accounts* tab opens an existing account and changes its settings and does
nothing else. A client screen offering either act is offering something the design does not have.

**The secret check is per account, and the AI tab says so rather than holding it.** The switch and
its warning moved into the account editor, and where the global switch stood the *AI* tab now draws a
`shield` note (line 2631) reading that the check *is set separately for each mail account — open
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
scripted batch out of three (`DEMO_BATCHES`, line 4468) — a message lands at the top of the list and
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
| A row moved to a folder | `rowAnim[<id>] === "move"` | `mfrowmoved` — 900 ms, a warning wash that comes up and fades back out; the row stays, so nothing collapses (line 5095) |
| A row changing in place | `rowAnim[<id>] === "edit"` | `mfrowedit` — 1 700 ms, an accent bar and a soft accent wash that fade out |
| A notification arriving | `notifAnim[<id>] === "in"` | the same `mfrowin` on the notification-centre row |

The collapse the three removals share is `mfrowout` — 440 ms, closing to zero height and sliding
22 px left, pointer events off while it goes — and the state change itself lands at 380 ms, before
the wash has finished (`animateRowsOut`, line 5629). The wash is drawn on the row rather than on the
wrapper that collapses (`rowWash`, line 6122), which is what lets the two run at different lengths.

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
warning colours behind the row as it is dragged (`swipeBgStyle`, line 8162) rather than the success
green it used to, so the gesture and the wash that follows it are one colour rather than two. A move
is the one act that washes a row it does not remove: the row keeps its place under its new folder
chip while `mfrowmoved` fades back out behind it.

## Sign-in — `MailFathom Sign-in.dc.html`

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

## Toasts — `MailFathom Toasts.dc.html`

Six kinds — `neutral`, `success`, `error`, `warning`, `info`, `loading` — and the kind decides the
glyph, the colour and the bar. What is worth reading in the source rather than in a screenshot:

- **A `loading` toast is sticky.** Its close control does not dismiss it; it asks whether to abort
  the operation, and aborting posts a `warning` toast saying what was and was not saved.
- The stack is newest-first and capped at `maxStack` (4 by default); past it the oldest is dropped.
- Resting opacity is 0.8 and 1.0 under the pointer, so the content beneath stays readable.
- The **blocking overlay** is a separate thing from a toast: determinate (a percentage) or
  indeterminate, no dismissal by scrim or Escape, and its *Cancel* asks before it aborts.
- On a narrow screen the stack is full-width at the top instead of a 400 px column on the right.

## The notification gesture — `MailFathom Notification Gesture.dc.html`

Numbers the implementation reads, not decoration: 1 : 1 finger tracking with no easing, scrim opacity
**equal to** the travelled fraction, a **0.32** distance threshold, a **0.5 px/ms** velocity
threshold, a **260 ms** `cubic-bezier(.32,.72,0,1)` spring back, a **12 px** navigation-bar slop and a
**10 px** row slop that cancels the 420 ms long press. Three hand-overs are stated: a scrolled list
gives the gesture up at its own top *without lifting the finger*, a row's long press and the drag are
mutually exclusive, and the navigation bar hands over past 12 px and cancels its own tap.

**Phone composition and `pointer: coarse` only.** The fold, the tablet and the desktop keep the panel
they draw today, and there is no drag gesture there at all.

## Mail search — `MailFathom Mail Search.html`

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
- The file itself says the prototype's mail column is wrong in one place: the field promises a
  description of what somebody needs while the server matches words only, and it names the correction
  — *Words from the message you are looking for*.

## States the source has and a preview does not reach

This is the class the inventory exists for. Each is in the source, and none of them can be reached by
clicking a preview.

- **The application with no mailbox at all.** `const emptyApp = false;` (line 6325) is a constant, and
  every one of the seven screen gates is `st.screen === "…" && !emptyApp`. Nothing anywhere draws the
  true branch, so **the design does not cover a deployment with no mailbox** — a session that needs
  that screen is designing something the project has not settled, and it goes to the owner rather than
  into the client.
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
