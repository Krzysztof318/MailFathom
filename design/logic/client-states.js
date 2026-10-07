// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.artboards.clientStates = (DCLogic, React) => {
  const {
    clientStatesSample: SAMPLE,
    clientStatesMessageRows: ROWS,
    clientStatesSearchResultRows: SEARCH_RESULT_ROWS,
    clientStatesFolders: TREE,
    clientStatesTasksToday: TASKS_TODAY,
    clientStatesPlacements: PLACEMENTS,
    clientStatesPeople: PEOPLE,
    clientStatesConversationHistory: HISTORY,
  } = MailFathomDesign.data;
  const { askedBefore: ASKED_BEFORE } = SAMPLE;

  const W = { telefon: 390, fold: 884, tablet: 1024, desktop: 1440 };
  const H = { telefon: 844, fold: 832, tablet: 768, desktop: 900 };
  const ORDER = ["telefon", "fold", "tablet", "desktop"];
  const RAIL = ["explore", "mail", "topic", "auto_awesome", "task_alt", "calendar_month", "group"];
  const SCR = { discover: "explore", mail: "mail", tasks: "task_alt", cal: "calendar_month", people: "group", agent: "auto_awesome" };
  const IC = "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;";
  const SHIM = "border-radius:6px;background:linear-gradient(90deg,var(--line) 0%,var(--line2) 42%,var(--line) 78%);background-size:260% 100%;animation:mfshim 1.05s linear infinite;";

  const STATES = [
    ["d_refusal", "1.1", "discover", "Question refused before a run"],
    ["d_cancel", "1.2a", "discover", "Cancelling, and cancel failed"],
    ["d_ended", "1.2b", "discover", "The run ended"],
    ["d_finished", "1.2c", "discover", "After a finished run"],
    ["d_coming", "1.3a", "discover", "Still-coming placeholder"],
    ["d_empty", "1.3b", "discover", "Nothing composed, newer version, unknown block"],
    ["ev_opening", "1.4a", "discover", "Evidence - opening"],
    ["ev_failed", "1.4b", "discover", "Evidence - read failures"],
    ["ev_private", "1.4c", "discover", "Evidence - private source"],
    ["ev_outdated", "1.4d", "discover", "Evidence - outdated copy"],
    ["ev_kinds", "1.4e", "discover", "Evidence - other kinds of citation"],
    ["d_asked", "1.5", "discover", "Asked before"],
    ["m_verdict", "2.1", "mail", "Sender verdict"],
    ["m_more", "2.2a", "mail", "Reading a thread in pages"],
    ["m_partial", "2.2b", "mail", "Partial thread failure and head notes"],
    ["m_pane", "2.2c", "mail", "Whole reading pane failed"],
    ["m_folders", "2.3a", "mail", "Folder column states"],
    ["m_list", "2.3b", "mail", "Message list states"],
    ["m_kind", "2.4a", "mail", "Kind of folder"],
    ["m_roles", "2.4b", "mail", "Four more folder roles"],
    ["m_search", "2.5", "mail", "Searching by describing"],
    ["m_pending", "2.6", "mail", "Changes not yet in the mailbox"],
    ["c_edit", "3.1", "work", "Editing an event"],
    ["t_layout", "3.2", "work", "Lay out today as an offer"],
    ["t_proposed", "3.3a", "work", "Proposed tasks, No day, status line"],
    ["t_states", "3.3b", "work", "Task list empty, loading, failed"],
    ["p_none", "3.4a", "work", "People - nobody open"],
    ["p_states", "3.4b", "work", "People - empties, loading, failures"],
    ["a_steer", "4.1", "agent", "Steering a running answer"],
    ["a_failed", "4.2a", "agent", "Failed answer and not-sent line"],
    ["a_read", "4.2b", "agent", "Reading and failure states"],
  ];

  const LEDGER = [
    ["1.1", "d_refusal", "Drawn", "Refusal before a run", "One line directly under the field, above the scope chips. Signed out and not permitted read neutral; unavailable and unreadable read as a warning with Try again. The question stays in the field."],
    ["1.2", "d_cancel", "Changed", "How a run ends", "The ending takes the running bar's slot at the top of the canvas, so blocks already shown never move. Eleven reasons fall into five tones (1.2b). “Completed” gets no card: “Answered by …” and what the run spent move into the existing run line beside the plan chip (1.2c)."],
    ["1.3", "d_coming", "Drawn", "Canvas in progress and empty", "Trailing dashed card after the last block; Retry only when offline, as the client does. Newer-version and unknown-block share one dashed neutral card (1.3b)."],
    ["1.4", "ev_opening", "Merged", "Evidence inspector", "Every state drawn in the inspector body (1.4a-e). The “not permitted” read failure is merged into Private source - same cause, same remedy, same lock badge - so the client should render it as 1.4c."],
    ["1.5", "d_asked", "Changed", "Asked before", "Kept in Discover, idle only, under the scope chips. Chips carry question and scope and re-ask it; the stored short answer and freshness are not shown - recall should start a fresh run, not replay a possibly stale answer. savedAnswers.a and .fresh can go."],
    ["2.1", "m_verdict", "Changed", "Sender verdict", "Sits under the sender line, above the body. Warning is a notice box; healthy is one quiet line so it never competes with the message. Copy tightened."],
    ["2.2", "m_more", "Drawn", "Thread in pages and failures", "Paging control at the thread's end, partial-failure notice where the missing messages would be, whole-pane states in the “Nothing open” layout, head notes under the subject (2.2a-c)."],
    ["2.3", "m_folders", "Changed", "Folder column and list states", "Folder loading becomes a skeleton; the sentence stays for screen readers only. Failed, offline and no account drawn; the four list states drawn (2.3a-b)."],
    ["2.4", "m_kind", "Changed", "Kind of folder, four roles", "“Kind of folder” moves to the top as a radio list (new component: a select hides what each option does); choosing a role collapses name and parent into the hint. Flagged, Important, All mail and Outbox drawn in the tree (2.4b)."],
    ["2.5", "m_search", "Drawn", "Searching by describing", "Both capabilities drawn. Ranking chips sit on their own row under the filter chips, set apart by icon and tint. The stage-3 note in Mail Search is updated."],
    ["2.6", "m_pending", "Changed", "Changes not yet in the mailbox", "The connection summary itself becomes the indicator and opens a panel (bottom sheet on phone). Toasts still announce each action; a failure that lands after its toast is gone raises one warning toast pointing to the panel."],
    ["3.1", "c_edit", "Drawn", "Editing an event", "Edit in place in the same dialog; All day hides the times; Save stays disabled until something changes and says why when times conflict. The new-event form should take the same Starts / Ends fields."],
    ["3.2", "t_layout", "Drawn", "Lay out today", "Waiting, offer, nothing left and allowance spent drawn. Unavailable hides the button and keeps the capacity line."],
    ["3.3", "t_proposed", "Drawn", "Proposed tasks and list states", "Accept on the row and in the menu, “Dismiss task” for proposals, a “No day” group, both empties, loading, failure and the status line (3.3a-b)."],
    ["3.4", "p_none", "Drawn", "People", "“Nobody open” from fold upward (phone has no detail pane). Empties, loading, failures, no shared threads and the status line (3.4a-b)."],
    ["4.1", "a_steer", "Changed", "Steering a running answer", "While a run is going, Send reads “Steer”; the note shows as a user turn tagged “Joins at the next turn”, then “Taken in”. The status line names it; Cancel is unchanged and still stops the whole answer."],
    ["4.2", "a_failed", "Drawn", "Agent reading and failures", "Failed answer, not-sent line and conversation read failure drawn, plus history, refusals and delete-several. Refusals share the error toast; the five read-failure reasons share one centred treatment (4.2a-b)."],
  ];

  const TONE = {
    neutral: ["background:var(--sub);border:1px solid var(--line);", "var(--muted)"],
    warn: ["background:var(--warn-soft);border:1px solid var(--warn);", "var(--warn-text)"],
    err: ["background:var(--err-soft);border:1px solid var(--err);", "var(--err-text)"],
    ok: ["background:var(--ok-soft);border:1px solid var(--ok);", "var(--ok-text)"],
    quiet: ["background:transparent;border:1px dashed var(--border2);", "var(--ok-text)"],
  };
  const note = (tone, extra) => "display:flex;align-items:flex-start;gap:10px;padding:11px 13px;border-radius:8px;font-size:13px;line-height:1.5;color:var(--text);" + TONE[tone][0] + (extra || "");
  const noteIc = (tone) => IC + "font-size:18px;line-height:1.2;color:" + TONE[tone][1];
  const cat = (arr) => arr.map((x) => Object.assign({ body: "", action: "", when: "" }, x, { style: note(x.tone, x.big ? "padding:14px 16px;" : ""), icStyle: noteIc(x.tone), hasAction: !!x.action, hasWhen: !!x.when, hasBody: !!x.body }));

  const REFUSALS = cat([
    { label: "Not permitted", tone: "neutral", icon: "lock", title: "This account may not ask questions of its mail.", body: "Your administrator can change that." },
    { label: "Deployment unavailable", tone: "warn", icon: "cloud_off", title: "The deployment is not answering, so no run started.", body: "Your question is kept.", action: "Try again" },
    { label: "Answer the client could not read", tone: "warn", icon: "error", title: "The deployment replied in a way this app could not read, so no run started.", body: "Updating the app may help.", action: "Try again" },
  ]);

  const ENDINGS = cat([
    { group: "Finished", label: "completed", tone: "quiet", icon: "check_circle", title: "No card.", body: "The running bar gives way to the “Answered by …” line in the run row (1.2c)." },
    { group: "Stopped by you", label: "cancelled", tone: "neutral", icon: "stop_circle", title: "You stopped this run.", body: "What arrived before you stopped stays below.", action: "Ask again", big: 1 },
    { group: "Limit reached", label: "period allowance spent", tone: "warn", icon: "hourglass_disabled", title: "This month's questions are used up.", body: "What arrived before the limit stays below.", when: "Questions become available again on Thursday 1 October at 00:00.", big: 1 },
    { group: "Limit reached", label: "run allowance spent", tone: "warn", icon: "data_usage", title: "This question reached the most one run may spend.", body: "Below is what it found before stopping. One mailbox or one year costs less.", action: "Ask again", big: 1 },
    { group: "Try again", label: "timed out", tone: "warn", icon: "timer_off", title: "The run ran out of time.", body: "It took longer than this deployment allows for one question.", action: "Ask again", big: 1 },
    { group: "Try again", label: "temporarily unavailable", tone: "warn", icon: "cloud_off", title: "Answering is paused for a moment.", body: "The deployment is busy or restarting. Nothing you asked is lost.", action: "Try again", big: 1 },
    { group: "Could not finish", label: "failed", tone: "err", icon: "error", title: "The run failed.", body: "Something went wrong on the deployment while answering.", action: "Ask again", big: 1 },
    { group: "Could not finish", label: "gone", tone: "err", icon: "link_off", title: "This run no longer exists.", body: "The deployment has no record of it - it was probably restarted.", action: "Ask again", big: 1 },
    { group: "Could not finish", label: "stopped", tone: "err", icon: "block", title: "The deployment stopped this run.", body: "A deployment rule ended it before it finished.", action: "Ask again", big: 1 },
    { group: "Not possible here", label: "unavailable", tone: "neutral", icon: "do_not_disturb_on", title: "This deployment does not answer questions.", body: "Questions are switched off here. Your administrator can turn them on.", big: 1 },
    { group: "Not possible here", label: "retrieval refused", tone: "neutral", icon: "lock", title: "The mail this question needs could not be read.", body: "The deployment refused to read a mailbox in the scope. Narrow the scope, or ask your administrator for access.", big: 1 },
  ]);

  const EVFAIL = cat([
    { label: "Signed out", tone: "neutral", icon: "login", title: "Sign in to open this source.", action: "Sign in" },
    { label: "Unreadable", tone: "warn", icon: "error", title: "The source arrived in a form this app could not read.", action: "Try again" },
    { label: "Missing", tone: "neutral", icon: "draft", title: "This message is no longer in the mailbox.", body: "It was deleted or moved after the answer was composed." },
  ]);

  const NOTSENT = cat([
    { label: "Signed out", tone: "err", icon: "error", title: "Not sent - you are signed out.", body: "Your message stays in the field.", action: "Sign in" },
    { label: "Not permitted", tone: "err", icon: "error", title: "Not sent - this account may not use the agent.", body: "Your administrator can change that." },
    { label: "Deployment unavailable", tone: "err", icon: "error", title: "Not sent - the deployment is not answering.", action: "Send again" },
    { label: "Answer unreadable", tone: "err", icon: "error", title: "Not sent - the deployment's reply could not be read.", action: "Send again" },
  ]);

  const CONVFAIL = cat([
    { label: "Signed out", tone: "neutral", icon: "login", title: "Sign in to read this conversation.", action: "Sign in" },
    { label: "Not permitted", tone: "neutral", icon: "lock", title: "This conversation belongs to another account.", body: "Only the person who started it can read it." },
    { label: "Not found", tone: "neutral", icon: "forum", title: "This conversation no longer exists.", body: "It was deleted, here or on another device." },
    { label: "Unreadable", tone: "warn", icon: "error", title: "This conversation arrived in a form this app could not read.", action: "Try again" },
  ]);

  const treeRow = (t) => ({
    icon: t.icon, label: t.label, count: t.count,
    style: "display:flex;align-items:center;gap:10px;padding:7px 12px;border-radius:8px;font-size:13.5px;" + (t.active ? "background:var(--accent-soft);color:var(--accent-d);font-weight:600;" : "color:var(--text2);"),
    ic: IC + "font-size:19px;width:20px;text-align:center;flex:0 0 20px",
    countStyle: "font-size:11.5px;" + (t.waiting ? "color:var(--warn-text)" : "color:var(--faint)"),
  });

  class Component extends DCLogic {
    state = {};

    frame(l, th, screen, multi) {
      const w = W[l], phone = w < 700, touch = w < 1180, desk = !touch, narrow = !phone && w < 1020;
      const railW = phone ? 0 : touch ? 66 : 96;
      const inspW = desk ? 400 : narrow ? 320 : 340;
      const listW = desk ? 340 : 320;
      const btn = (p) => "display:inline-flex;align-items:center;justify-content:center;gap:7px;white-space:nowrap;flex:0 0 auto;box-sizing:border-box;" +
        (phone ? "min-height:44px;padding:0 16px;border-radius:12px;font-size:14px;" : "padding:7px 14px;border-radius:8px;font-size:13px;") +
        (p ? "font-weight:600;color:var(--onaccent);background:var(--accent);border:1px solid var(--accent);" : "color:var(--text2);background:var(--panel);border:1px solid var(--border2);");
      return {
        key: l + th, theme: th, phone, notPhone: !phone, desk, notDesk: !desk, touchWide: !phone && touch,
        label: l + " · " + w + "×" + H[l] + (multi ? " · " + th : ""),
        frame: "width:" + w + "px;flex:none;display:flex;align-items:stretch;background:var(--bg);color:var(--text);font-family:'Geist',system-ui,sans-serif;font-size:15px;border:1px solid var(--line);border-radius:12px;overflow:hidden;box-shadow:0 14px 34px var(--sh-1);position:relative",
        rail: "flex:0 0 " + railW + "px;display:flex;flex-direction:column;align-items:center;gap:" + (narrow ? 8 : 12) + "px;padding:16px 0;background:var(--rail);border-right:1px solid var(--line)",
        railItems: RAIL.map((icon) => ({ icon, style: IC + "font-size:" + (touch ? 21 : 22) + "px;width:" + (touch ? 44 : 60) + "px;height:" + (touch ? 38 : 42) + "px;display:flex;align-items:center;justify-content:center;border-radius:12px;" + (icon === SCR[screen] ? "background:var(--accent-soft);color:var(--accent-d)" : "color:var(--muted)") })),
        body: "flex:1;min-width:0;display:flex;flex-direction:column",
        head: phone
          ? "display:flex;align-items:center;gap:10px;flex:0 0 auto;padding:11px 14px;border-bottom:1px solid var(--line);background:var(--panel)"
          : "display:flex;align-items:center;gap:16px;padding:0 26px;height:56px;flex:0 0 56px;border-bottom:1px solid var(--line);background:var(--panel)",
        searchWrap: phone
          ? "flex:0 0 auto;display:flex;flex-direction:column;gap:9px;padding:12px 14px;background:var(--panel);border-bottom:1px solid var(--line)"
          : "flex:0 0 auto;display:flex;flex-direction:column;gap:10px;padding:18px 26px;background:var(--panel);border-bottom:1px solid var(--line)",
        searchBox: "display:flex;align-items:center;gap:12px;border:2px solid var(--accent);border-radius:12px;padding:11px 14px;background:var(--panel)" + (phone || narrow ? "" : touch ? ";max-width:780px" : ";max-width:900px"),
        fieldMax: phone || narrow ? "" : touch ? "max-width:780px;" : "max-width:900px;",
        main: phone ? "display:flex;flex-direction:column;gap:12px;padding:14px;min-width:0;flex:1" : "flex:1;display:flex;flex-direction:column;gap:14px;padding:20px 24px;min-width:0",
        split: phone ? "flex:1;display:flex;flex-direction:column;min-height:0" : "flex:1;display:flex;min-height:0",
        insp: phone ? "flex:1;display:flex;flex-direction:column;background:var(--sub);min-height:560px" : "width:" + inspW + "px;flex:0 0 " + inspW + "px;display:flex;flex-direction:column;background:var(--sub);border-left:1px solid var(--line);min-height:560px",
        list: phone ? "flex:1;display:flex;flex-direction:column;background:var(--panel);min-width:0" : "width:" + listW + "px;flex:0 0 " + listW + "px;display:flex;flex-direction:column;background:var(--panel);border-right:1px solid var(--line)",
        read: "flex:1;min-width:0;display:flex;flex-direction:column;background:var(--panel)",
        readPad: phone ? "display:flex;flex-direction:column;gap:14px;padding:14px" : "display:flex;flex-direction:column;gap:16px;padding:20px 28px;max-width:860px",
        folders: desk
          ? "width:232px;flex:0 0 232px;display:flex;flex-direction:column;gap:2px;padding:14px 10px;background:var(--rail);border-right:1px solid var(--line);min-height:560px;box-sizing:border-box"
          : phone ? "flex:1;display:flex;flex-direction:column;gap:2px;padding:14px 10px;background:var(--panel);min-height:560px;box-sizing:border-box"
          : "width:300px;flex:0 0 300px;display:flex;flex-direction:column;gap:2px;padding:14px 10px;background:var(--panel);border-right:1px solid var(--line);box-shadow:12px 0 32px var(--sh-2);min-height:560px;box-sizing:border-box;position:relative;z-index:2",
        beside: desk ? "flex:1;display:flex;background:var(--panel)" : phone ? "display:none" : "flex:1;background:var(--scrim)",
        btnP: btn(true), btnS: btn(false),
        noteBody: "flex:1;min-width:0;display:flex;flex-wrap:wrap;align-items:center;gap:8px 12px",
        noteNeutral: note("neutral", phone ? "" : "max-width:" + (narrow ? "none" : touch ? "780px" : "900px") + ";box-sizing:border-box;"),
        noteWarn: note("warn"), noteErr: note("err"), noteN: note("neutral"),
        statusCard: "display:flex;align-items:center;gap:12px;flex-wrap:wrap;background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:" + (phone ? "12px 14px" : "14px 18px"),
        endCard: note("warn", "padding:" + (phone ? "12px 14px" : "14px 18px") + ";font-size:14px;"),
        dlgWrap: phone ? "flex:1;display:flex;flex-direction:column;background:var(--panel)" : "flex:1;display:flex;align-items:center;justify-content:center;padding:48px 24px;background:var(--scrim);min-height:" + (H[l] - 120) + "px",
        dlg: phone ? "flex:1;display:flex;flex-direction:column;background:var(--panel)" : "width:440px;max-width:100%;display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-radius:12px;box-shadow:0 22px 52px var(--sh-3)",
        center: "flex:1;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;padding:" + (phone ? "56px 22px" : "80px 32px") + ";text-align:center;background:var(--sub);min-height:420px",
        histCol: "width:280px;flex:0 0 280px;display:flex;flex-direction:column;gap:6px;padding:14px 12px;background:var(--rail);border-right:1px solid var(--line);box-sizing:border-box",
        agentMain: "flex:1;min-width:0;display:flex;flex-direction:column;background:var(--sub)",
        thread: phone ? "display:flex;flex-direction:column;gap:14px;padding:16px 14px" : "display:flex;flex-direction:column;gap:16px;padding:22px 26px" + (desk ? ";max-width:860px" : ""),
        composer: phone ? "display:flex;flex-direction:column;gap:9px;background:var(--panel);border-top:1px solid var(--line);padding:12px 14px" : "display:flex;flex-direction:column;gap:9px;padding:14px 26px 20px" + (desk ? ";max-width:860px" : ""),
        tasksWrap: phone ? "display:flex;flex-direction:column;gap:14px;padding:14px" : "display:flex;gap:22px;align-items:flex-start;padding:20px 26px",
        tasksList: "flex:1;min-width:0;display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-radius:12px;overflow:hidden",
        sidePanel: phone ? "order:-1;display:flex;flex-direction:column;gap:10px;background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:14px 15px" : "width:" + (desk ? 340 : 300) + "px;flex:0 0 auto;display:flex;flex-direction:column;gap:10px;background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:15px 16px;box-sizing:border-box",
        popWrap: phone ? "flex:1;display:flex;flex-direction:column;justify-content:flex-end;background:var(--scrim);min-height:600px" : "flex:1;padding:6px 26px 40px " + (desk ? 118 : 108) + "px;background:var(--bg);min-height:520px",
        pop: phone ? "display:flex;flex-direction:column;background:var(--panel);border-radius:16px 16px 0 0;box-shadow:0 -12px 32px var(--sh-2)" : "width:430px;display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-radius:12px;box-shadow:0 1px 0 var(--inset) inset,0 16px 38px var(--sh-2),0 2px 6px var(--sh-1);overflow:hidden",
      };
    }

    renderVals() {
      const st = this.state, p = this.props;
      let layout = st.layout || p.layout || "all";
      if (layout === "phone") layout = "telefon";
      const theme = st.theme || p.theme || "light";
      const focus = st.focus || p.state || "all";
      const layouts = layout === "all" ? ORDER : [layout];
      const themes = theme === "both" ? ["light", "dark"] : [theme];
      const multi = themes.length > 1;
      const rows = (scr) => themes.map((th) => ({ key: th, frames: layouts.map((l) => this.frame(l, th, scr, multi)) }));
      const show = {}, g = {};
      STATES.forEach(([k, , grp]) => { show[k] = focus === "all" || focus === k; if (show[k]) g[grp] = true; });
      const cur = STATES.find((s) => s[0] === focus);
      const seg = (active) => "text-align:center;font-size:12px;padding:5px 11px;border-radius:8px;" + (active ? "background:var(--accent);color:var(--onaccent);font-weight:600;" : "color:var(--muted);");
      const VCH = { Drawn: "ok", Changed: "accent", Merged: "warn" };
      const chipTone = (v) => VCH[v] === "ok" ? "background:var(--ok-soft);color:var(--ok-text);" : VCH[v] === "warn" ? "background:var(--warn-soft);color:var(--warn-text);" : "background:var(--accent-soft);color:var(--accent-d);";
      const shim = (w, h) => SHIM + "width:" + w + ";height:" + (h || 10) + "px;";
      return {
        pageTheme: themes[0],
        sample: SAMPLE,
        askedBefore: ASKED_BEFORE,
        show, g,
        focused: focus !== "all",
        focusLabel: cur ? cur[1] + " " + cur[3] : "",
        showAll: () => this.setState({ focus: "all" }),
        showLedger: focus === "all",
        layoutSegs: ["all", ...ORDER].map((v) => ({ label: v === "all" ? "All four" : v, pick: () => this.setState({ layout: v }), style: seg(layout === v) })),
        themeSegs: [["light", "Light"], ["dark", "Dark"], ["both", "Both"]].map(([v, label]) => ({ label, pick: () => this.setState({ theme: v }), style: seg(theme === v) })),
        ledger: LEDGER.map(([id, k, verdict, title, text]) => ({ id, verdict, title, text, open: () => this.setState({ focus: k }), chip: "justify-self:start;font-size:11.5px;font-weight:600;border-radius:12px;padding:3px 10px;" + chipTone(verdict) })),
        R: { discover: rows("discover"), mail: rows("mail"), tasks: rows("tasks"), cal: rows("cal"), people: rows("people"), agent: rows("agent") },
        vthemes: themes.map((th) => ({ theme: th, style: "width:1100px;box-sizing:border-box;display:flex;flex-wrap:wrap;gap:22px;padding:22px 24px;background:var(--bg);color:var(--text);font-family:'Geist',system-ui,sans-serif;font-size:15px;border:1px solid var(--line);border-radius:12px", head: "flex-basis:100%;font-size:11px;color:var(--muted);font-weight:600" })),
        refusals: REFUSALS, endings: ENDINGS, evfail: EVFAIL, notsent: NOTSENT, convfail: CONVFAIL,
        rows: ROWS.map((r, i) => Object.assign({}, r, { ini: r.initials, subj: r.subject, style: "display:flex;flex-direction:column;gap:4px;padding:11px 14px;border-bottom:1px solid var(--line2);" + (i === 0 ? "background:var(--accent-soft);" : "") })),
        shimRows: [0, 1, 2, 3, 4, 5].map((i) => ({ a: SHIM + "width:18px;height:18px;flex:0 0 18px;", b: shim((52 + ((i * 17) % 38)) + "%", 11) })),
        shimLines: [92, 100, 84, 96, 58].map((w) => ({ s: shim(w + "%", 12) })),
        shimList: [0, 1, 2, 3].map((i) => ({ a: SHIM + "width:26px;height:26px;flex:0 0 26px;border-radius:50%;", b: shim((40 + i * 9) + "%", 11), c: shim((70 - i * 6) + "%", 10) })),
        shimCard: [100, 88, 64].map((w) => ({ s: shim(w + "%", 11) })),
        shimLabel: shim("60%", 9),
        tree: TREE.map(treeRow),
        treeShort: TREE.filter((t) => ["Inbox", "Drafts", "Sent", "Archive", "Trash"].includes(t.label)).map(treeRow),
        rowsSearch: SEARCH_RESULT_ROWS.map((r) => ({ subj: r.subject, time: r.time })),
        tasksToday: TASKS_TODAY.map((t) => ({ t: t.title, meta: t.meta })),
        placements: PLACEMENTS.map((p) => ({ at: p.at, t: p.title })),
        layoutStates: [
          { label: "Waiting", cap: "5 h 20 min free · 6 open tasks, about 4 h.", spin: true, text: "Laying out the rest of the day…" },
          { label: "Nothing left to lay out", cap: "2 h free · every task for today already has a time.", plain: true, icon: "event_available", text: "Nothing is left to lay out today.", tone: "ok" },
          { label: "Allowance spent", cap: "5 h 20 min free · 6 open tasks, about 4 h.", plain: true, btn: true, icon: "hourglass_disabled", text: "Laying out days is used up until tomorrow at 06:00.", tone: "warn" },
          { label: "Unavailable - button hidden", cap: "5 h 20 min free · 6 open tasks, about 4 h.", plain: false },
        ].map((x) => Object.assign({}, x, {
          textStyle: "display:flex;align-items:flex-start;gap:7px;font-size:13px;line-height:1.45;color:" + (x.tone === "warn" ? "var(--warn-text)" : "var(--ok-text)"),
          icStyle: IC + "font-size:16px;line-height:1.2",
        })),
        people: PEOPLE.map((p) => ({ ini: p.initials, name: p.name, org: p.organisation })),
        history: HISTORY.map((h, i) => Object.assign({}, { t: h.title, meta: h.meta }, { style: "display:flex;flex-direction:column;gap:2px;padding:9px 10px;border-radius:8px;min-width:0;" + (i === 0 ? "background:var(--hover);" : "") })),
      };
    }
  }

  return Component;
};
