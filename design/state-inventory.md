# The design's state inventory

**Mirror stamp `51f0ca5a520463ae`** — the etag set this was extracted from. Run
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
| `MailFathom Prototype.dc.html` | The whole signed-in application: seven screens, every overlay, every composition. 7 658 lines, and the only file that carries navigation |
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
(`renderVals`, lines 5305–5326):

| Name | Condition | What it changes |
|---|---|---|
| `isMobile` | `w < 700` | Bottom navigation, drawers, touch sizing |
| `tablet` | `700 ≤ w < 1180` | Touch and drawers, toolbar controls without labels |
| `singlePane` | `w < 820` | One pane at a time — the list and the content never stand together |
| `narrow` | `700 ≤ w < 1020` | The narrower desktop composition |
| `touch` | `isMobile \|\| tablet` | Every `pointer: coarse` affordance |
| `drawer` | `tablet \|\| isMobile` | The side list arrives as a drawer rather than a column |

`FRAMES` (line 3055) is the design's own four sizes: `phone` 390 × 844, `fold` 884 × 832,
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

Which blocks a run produces is the plan, and there are three (`PLANS`, line 2629): `contract`
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
| Side list expanded | `sideExpanded` | not collapsed; collapsed is 58 px against 210 px |
| Side list as a drawer | `showSide` / `sideScrim` | `drawer && drawerOpen` |
| Folder button | `showFolderBtn` | `drawer` |
| Account menu on a mailbox | `ctxMenu` through `g.ctxOpen` | right-click a mailbox header, or hold it for 460 ms — *New folder* and *Mark all as read*; **All accounts carries none** |
| A folder the user made | `st.extraFolders[<mailbox>]` | *New folder* in that menu, or *New folder here* in the move sheet; it is drawn below the standard five |

Every mailbox draws the same five folders in the same order — Inbox, Sent, Drafts, Archive, Trash
(`STD_FOLDERS`, line 5581) — and a count only where the mock data gives one. *All accounts* is those
same five over both mailboxes rather than a set of its own, and anything under them is a folder the
user made.

### The thread

| State | Flag | Reveal |
|---|---|---|
| Thread head | `showThreadHead` | `!st.stateBarOff \|\| singlePane` |
| State bar beside the thread | `showThreadStateBar` | `!singlePane && !tablet && !stateBarOff` |
| State cards | `showStateCards` | `!singlePane` |
| State inside the body | `stateInBody` | `tablet && !stateBarOff` — the tablet's own answer to the same content |
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
| HTML view open | `htmlOpen` | the *show the full HTML version* control |
| HTML mode warning | `htmlModeWarn` | `st.msgView === "html"`, chosen in Settings |
| HTML confirmation | `htmlAskOpen` | opening HTML from a message |

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
| Settings | `settingsOpen` | the settings control |
| Settings: profile tab | `tabProfil` | the default tab, labelled *Profil* |
| Settings: application tab | `tabApp` | the tab control, labelled *Aplikacja* |
| Own photo | `meHasPhoto` | a photo was uploaded |
| **Photo rejected** | `photoError` | a file over 1 MB — a real file has to be chosen, so **no click reaches this** |
| Telemetry off | `telemetryOptOut` | the telemetry switch |
| Notification centre | `notifCenterOpen` | the bell, or the phone's upward gesture |
| Unread notifications | `notifHasUnread` | the unread count; it gates three regions including the rail badge |
| **No notifications** | `notifEmpty` | the shown list is empty — reachable by filtering to unread after reading them all |
| Move dialog | `moveOpen` | the move action on a selection — grouped by mailbox (`moveGroups`, line 7069), each group ending in *New folder here*, and the toast it produces names the mailbox |
| Permanent delete | `confirmDel` under `inTrash` | a delete action while the open folder is a Trash (line 4446) — *Delete permanently*, no undo offered, and the toast reads *Permanently deleted* rather than *Moved to trash* |
| Cancel confirmation | `cancelAskOpen` | closing a toast that carries a running operation |
| Toasts | `toasts` | any operation that reports; `MailFathom Toasts.dc.html` is the whole of it |

Every label in the prototype is English. The four that were not — the Settings tabs *Profil* and
*Aplikacja* (line 7535), the user menu's sign-out item *Wyloguj* (line 135), and the single-pane
state toggle beside the thread head, *ukryj* (line 7271) — were translated in the project, and
`design/parity.json` presses two of them under their English names.

## Sign-in — `MailFathom Sign-in.dc.html`

Two views in one file, `isLogin` and `isServer`, and the server view is unreachable when the address
is forced by configuration.

| State | Flag | Reveal |
|---|---|---|
| Sign-in | `isLogin` | the opening view |
| Server address | `isServer` | *Change server* under *Advanced* — and `envLocked` refuses it |
| OAuth offered | `showOauth` | the server declares it (prop `oauthAvailable`) |
| Password offered | `showBasic` | the server declares it (prop `basicAvailable`) |
| Both, so a divider | `showDivider` | both of the above |
| **No method at all** | `noMethods` | neither — a **prop pair**, so only a property change reaches it |
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

- **The application with no mailbox at all.** `const emptyApp = false;` (line 5576) is a constant, and
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
- **No sign-in method offered** (`noMethods`). Two properties turned off together.
- **A case or a person with no documents** (`caseNoDocs`, `personNoDocs`). No mock record has an empty
  `docs`, so both empty states are drawn by no preview at any click.
- **No proposals left** (`calNoProposals`). Reachable only by dealing with every proposed slot first.
- **A conversation archive** (`hasArchived`, `archiveSectionOpen`). Needs a conversation archived first.
- **The `fold` composition's own case.** 884 px is `tablet` and `!singlePane`: touch, drawers, and two
  panes at once. Neither the phone artboard nor the desktop artboard shows it, and no click produces
  it — only a width does.
