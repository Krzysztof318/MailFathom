// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

{
  /* ——— Agent answer blocks ———
     The agent renders the SAME catalogue as Discover (see “MailFathom Result Blocks”):
     ANSWER, EVIDENCE, TIMELINE, FACT TABLE, PEOPLE, THREAD STATE, ATTACHMENTS, DRAFT, SUGGESTED ACTION.
     What differs in a conversation is that an actionable block carries real controls and therefore
     three phases: pending (proposal + controls), accepted (result line), declined (one quiet struck line). */

  const atToday = (h, m) => { const d = new Date(); d.setHours(h, m || 0, 0, 0); return d.getTime(); };

  const BK_TIMELINE_TODAY = {
    type: "timeline", meta: "today · 5 items",
    items: [
      { date: "09:00", title: "Fabrikam rollout review", detail: "Piotr Zieliński · 45 min" },
      { date: "11:00", title: "Call with Wrona Law Office", detail: "addendum wording · 30 min" },
      { date: "13:00", title: "Call with Anna Kowalska", detail: "Contoso · 30 min" },
      { date: "15:00", title: "Rollout status with Piotr", detail: "until 16:30" },
      { date: "all day", title: "Decision: Contoso addendum", detail: "deadline today" },
    ],
  };

  const BK_DRAFT_CONTOSO = {
    type: "draft", meta: "reply · 96 words",
    to: "Anna Kowalska", subject: "Re: Contract addendum — signatures",
    paras: [
      "Thank you for the addendum. We accept shortening the response time for critical incidents to 2 hours on business days.",
      "In return we ask for an upper cap on CPI indexation of 5% per year. At the 4.1% forecast this keeps the cost predictable for both sides through 2027.",
      "If the cap is acceptable, we are ready to close the decision by 28 August.",
    ],
    done: "Sent into “Contract addendum — signatures”",
    nav: [{ label: "Open the thread", icon: "forum", kind: "thread", tid: "contoso" }],
  };

  /* REAL EXAMPLE — a scripted conversation played back in real time. Content never arrives token
     by token: a widget appears only once it is whole. Between arrivals the status line says what
     the agent is doing right now, and it changes as the work moves on.
     Step kinds — u: user turn · s: status · b: new agent bubble · t: text appended · k: block appended. */
  const DEMO_CONV_ID = "c-real";
  const DEMO_SCRIPT = [
    { d: 300, u: "Anna Kowalska is pressing on the addendum. Go through the thread and get me a reply." },
    { d: 450, s: "Reading your request" },
    { d: 950, s: "Searching 3 accounts for the thread" },
    { d: 1150, b: { scope: "3 accounts · mail, calendar, cases", text: "Found it — “Contract addendum — signatures”. 6 messages since 18 August, 3 attachments." } },
    { d: 600, s: "Reading 6 messages" },
    {
      d: 1700, b: { scope: "thread · 6 messages", text: "Here is where the thread actually stands." },
      k: {
        type: "threadstate", meta: "this thread",
        cols: [
          { label: "AGREED", items: [{ t: "2 h response time for critical incidents" }, { t: "Fee rises 8% from January" }] },
          { label: "OPEN QUESTIONS", items: [{ t: "Cap on CPI indexation" }, { t: "Penalty for a missed response" }] },
          { label: "COMMITMENTS", items: [{ t: "You: counter-proposal by 28 August" }] },
        ],
      },
    },
    { d: 500, s: "Opening 3 attachments" },
    { d: 1400, s: "Checking them against case CN-114" },
    {
      d: 1500,
      k: {
        type: "evidence", meta: "3 sources",
        items: [
          { n: "1", source: "Contract addendum — signatures", badge: "mail", snippet: "“…indexation by the CPI published for the previous year…”", relevance: "94%", freshness: "yesterday · Anna Kowalska" },
          { n: "2", source: "CPI addendum 2023.pdf", badge: "attachment", snippet: "“Indexation shall not exceed 5% per year.”", relevance: "91%", freshness: "18 August · attachment" },
          { n: "3", source: "Case CN-114 — framework contract", badge: "case", snippet: "“Response time 4 h, penalty 0.2% of the monthly fee.”", relevance: "86%", freshness: "March · case file" },
        ],
      },
    },
    { d: 600, s: "Weighing the 2023 cap against the new wording" },
    { d: 1500, b: { scope: "reading · 3 sources", text: "" } },
    { d: 700, p: "**Short version:** the cap you already agreed has quietly disappeared. The 2023 addendum says:\n\n> Indexation shall not exceed 5% per year.\n\nThe new draft only points at *the CPI published for the previous year*, with no ceiling on it at all." },
    { d: 1500, p: "**That matters more than the 8% headline does.** At the 4.1% forecast the first year costs you roughly the same either way — but with no cap, a repeat of 2022 would push the fee up by a fifth in one step, and nothing in the framework contract stops it." },
    { d: 800, s: "Checking the penalty in case CN-114" },
    { d: 1700, p: "The response time is the smaller trade:\n\n- **Critical incidents:** response drops from 4 h to 2 h\n- **Penalty:** stays at `0.2%` of the monthly fee — the same figure as in the framework contract\n\nSo no new exposure there." },
    { d: 700, s: "Putting my reading together" },
    { d: 1600, p: "## My reading\n\n1. Take the **8%**.\n2. Insist the **5% cap** comes back in writing.\n3. Leave the penalty alone.\n\nThat keeps the whole thing closable by **28 August**, the date you already promised Anna." },
    { d: 1900, u: "Accept the 8%, but keep the 5% cap. Draft it." },
    { d: 450, s: "Writing the draft" },
    { d: 1500, s: "Matching the tone to your earlier replies" },
    {
      d: 1900, b: { scope: "draft · from 6 messages and 3 attachments", text: "Draft is ready. Nothing goes out until you approve it." },
      k: Object.assign({}, BK_DRAFT_CONTOSO, { phase: "pending", cta: "Send it" }),
    },
    { d: 700, s: "Looking for what follows from this" },
    {
      d: 1400,
      k: {
        type: "task", meta: "tasks", phase: "pending",
        title: "Close the CPI cap decision with Anna Kowalska",
        due: "28 August", from: "Contract addendum — signatures", reminder: "2 days before",
        done: "Added to your task list",
        cta: "Add it",
      },
    },
    { d: 550, s: "Checking your calendar for a slot" },
    {
      d: 1400,
      k: {
        type: "event", meta: "calendar · work", phase: "pending",
        title: "Call with Anna Kowalska — CPI cap",
        when: "27 August 13:00–13:30", duration: "30 min", attendees: "You, Anna Kowalska", from: "Contract addendum — signatures",
        done: "Added to the calendar",
        cta: "Propose it",
        nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }],
      },
    },
    { d: 1000, b: { scope: "waiting for you", text: "" } },
    { d: 600, p: "Nothing has left your outbox. Three things above wait on you:\n\n- [ ] The reply to Anna\n- [ ] The task for the 28th\n- [ ] The half-hour with Anna\n\nApprove the draft and I will send it — the other two follow on their own." },
    { d: 1800, p: "If you would rather not concede the 8% at all, say so and I will rewrite the draft around holding January's fee flat. *Be aware* that the 28 August date then becomes unlikely — Anna's last two messages both pushed for a decision this week." },
    /* Work is done — three things the agent can do next, never more. */
    { d: 500, n: ["Rewrite the draft firmer", "What if Anna refuses the cap?", "Open the 2023 addendum"] },
  ];

  /* Fallback next steps for any reply that does not name its own. */
  const NEXT_DEFAULT = ["Turn this into a task", "Draft a reply", "Show me the sources"];

  const SEED_CONVS = () => [
    {
      id: "c-plan", title: "Plan my day", when: "09:41",
      msgs: [
        { role: "user", text: "Plan my day", ts: atToday(9, 41) },
        {
          role: "bot", ts: atToday(9, 41), scope: "today · 3 accounts",
          text: "Four fixed items today and one all-day decision. Two focus blocks cover what is actually at risk.",
          blocks: [
            BK_TIMELINE_TODAY,
            {
              type: "event", meta: "calendar · work", phase: "accepted",
              title: "Focus block: CPI cap counter-proposal",
              when: "today 10:00–11:00", duration: "1 h", attendees: "You", from: "Contract addendum — signatures",
              result: "Added to the calendar · 09:42",
              nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }],
            },
            {
              type: "event", meta: "calendar · work", phase: "pending",
              title: "Focus block: IT contract audit list",
              when: "today 16:00–17:00", duration: "1 h", attendees: "You", from: "IT contract audit",
              conflict: "overlaps Rollout status with Piotr (15:00–16:30)",
              done: "Added to the calendar",
              nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }],
            },
          ],
        },
        { role: "user", text: "Move the second one to tomorrow", ts: atToday(9, 44) },
        {
          role: "bot", ts: atToday(9, 44), scope: "calendar · tomorrow",
          text: "Moved. Tomorrow morning is free before the Fabrikam call.",
          blocks: [{
            type: "event", meta: "calendar · work", phase: "pending",
            title: "Focus block: IT contract audit list",
            when: "tomorrow 09:00–10:00", duration: "1 h", attendees: "You", from: "IT contract audit",
            done: "Added to the calendar",
            nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }],
          }],
        },
      ],
    },
    {
      id: "c-slip", title: "What slipped this week?", when: "08:20",
      msgs: [
        { role: "user", text: "What slipped this week?", ts: atToday(8, 20) },
        {
          role: "bot", ts: atToday(8, 20), scope: "7 days · 3 accounts",
          text: "Two commitments have passed their date, and both of them are yours.",
          blocks: [
            {
              type: "threadstate", meta: "3 threads · 7 days",
              cols: [
                { label: "AGREED", items: [{ t: "2 h response time for critical incidents" }, { t: "Decision closes on 28 August" }] },
                { label: "OPEN QUESTIONS", items: [{ t: "Upper cap on CPI indexation — no answer from Contoso" }] },
                { label: "COMMITMENTS", items: [
                  { t: "You: notice-period list to the audit — overdue since 02.09", late: true },
                  { t: "You: confirm the 12.11 date to Fabrikam — overdue since 04.09", late: true },
                ] },
              ],
            },
            {
              type: "task", meta: "tasks · from mail", phase: "pending",
              title: "Send the notice-period list to the audit",
              due: "tomorrow, 17:00", from: "IT contract audit", reminder: "09:00 tomorrow",
              done: "Added to tasks",
              nav: [{ label: "Open the thread", icon: "forum", kind: "thread", tid: "audyt1" }],
            },
            {
              type: "action", meta: "one step", phase: "declined",
              title: "Mark the Fabrikam thread as waiting on them",
              reason: "No reply from Tomasz Zieliński for 5 days.",
              effect: "Changes the thread state only — nothing is sent.",
              cta: "Mark it",
            },
          ],
        },
      ],
    },
    {
      id: "c-draft", title: "Draft a reply to Contoso", when: "11:02",
      msgs: [
        { role: "user", text: "Draft a reply to Contoso", ts: atToday(11, 2) },
        {
          role: "bot", ts: atToday(11, 2), scope: "thread · 6 messages",
          text: "We accept the 2 h SLA and ask for a 5% annual cap on indexation in return. Two sources carry the numbers.",
          blocks: [
            {
              type: "evidence", meta: "2 sources",
              items: [
                { n: "1", source: "SLA addendum.pdf", badge: "attachment", snippet: "“Response time drops from 4 to 2 hours, fee +8%…”", relevance: "94%", freshness: "current · 18.11.2025" },
                { n: "2", source: "Private message — Marta Nowak", badge: "private source", private: true, relevance: "76%", freshness: "today" },
              ],
            },
            Object.assign({ phase: "pending" }, BK_DRAFT_CONTOSO),
          ],
        },
        { role: "user", text: "Send it", ts: atToday(11, 7) },
        {
          role: "bot", ts: atToday(11, 7), scope: "thread · Contoso",
          text: "Sent. I will flag it if nothing comes back before the 28 August decision.",
          blocks: [Object.assign({ phase: "accepted", result: "Sent into “Contract addendum — signatures” · 11:07" }, BK_DRAFT_CONTOSO)],
        },
      ],
    },
    {
      id: "c-cases", title: "Status of open cases", when: "14:15",
      msgs: [
        { role: "user", text: "Status of open cases", ts: atToday(14, 15) },
        {
          role: "bot", ts: atToday(14, 15), scope: "3 cases in progress",
          text: "The Contoso 2027 renegotiation is the hot one — one open question, decision tomorrow. Here is where the versions differ.",
          blocks: [
            {
              type: "facttable", meta: "Contoso renegotiation",
              cols: ["Version", "Price", "SLA", "Indexation"],
              rows: [
                ["v2 · 12.04.2021", "€1,200 / mo", "4 h", "CPI, no cap"],
                ["v3 · 18.11.2025", "€1,296 / mo", "2 h", "CPI, no cap"],
                ["v4 · proposed", "€1,296 / mo", "2 h", "CPI, 5% cap"],
              ],
            },
            {
              type: "attachments", meta: "3 documents",
              items: [
                { name: "SLA addendum.pdf", icon: "picture_as_pdf", meta: "18.11.2025 · 240 kB" },
                { name: "Calculation_2027.xlsx", icon: "table_chart", meta: "yesterday · Marta Nowak" },
                { name: "Legal opinion — indexation.docx", icon: "description", meta: "05.09 · Wrona Law Office" },
              ],
            },
            {
              type: "action", meta: "one step", phase: "pending",
              title: "Attach the legal opinion to the Contoso 2027 case",
              reason: "The opinion answers the open question about the indexation cap and is not in the case file.",
              effect: "Adds the document to the case — nothing is sent to anyone.",
              cta: "Attach it", done: "Attached to Contoso 2027",
              nav: [{ label: "Open the case", icon: "folder_open", kind: "case" }],
            },
          ],
        },
      ],
    },
    {
      id: "c-thread", title: "Contract addendum — what is agreed", when: "now",
      ctx: "thread “Contract addendum — signatures”",
      msgs: [
        { role: "user", text: "What has been agreed here so far?", ts: atToday(15, 12) },
        {
          role: "bot", ts: atToday(15, 12), scope: "thread · 6 messages", working: true,
          text: "",
          blocks: [
            {
              type: "threadstate", meta: "this thread",
              cols: [
                { label: "AGREED", items: [{ t: "2 h response time for critical incidents" }, { t: "Fee rises by 8% from January" }] },
                { label: "OPEN QUESTIONS", items: [{ t: "Upper cap on CPI indexation" }] },
                { label: "COMMITMENTS", items: [{ t: "You: counter-proposal by 28 August" }] },
              ],
            },
            { type: "evidence", state: "loading", meta: "reading the attachments…" },
          ],
        },
      ],
    },
    {
      id: "c-states", title: "Blocks that did not come back whole", when: "yesterday",
      msgs: [
        { role: "user", text: "Check the Fabrikam delivery dates", ts: atToday(7, 30) },
        {
          role: "bot", ts: atToday(7, 30), scope: "2 accounts · partial",
          text: "One account did not answer in time, so part of this is incomplete.",
          blocks: [
            { type: "event", state: "loading", meta: "checking the calendar…", phase: "pending", title: "Fabrikam delivery date confirmation" },
            {
              type: "task", state: "error", meta: "tasks", phase: "pending",
              title: "Confirm the 12.11 date to Fabrikam",
              errorText: "The task list did not answer.",
            },
            { type: "draft", state: "empty", meta: "reply", phase: "pending", to: "Tomasz Zieliński", title: "Reply to Fabrikam", emptyText: "Nothing to draft — the last message in the thread is yours." },
            {
              type: "evidence", state: "partial", meta: "1 of 3 sources",
              items: [{ n: "1", source: "Q4 delivery dates", badge: "mail", snippet: "“Delivery moved to 12.11…”", relevance: "89%", freshness: "yesterday · Tomasz Zieliński" }],
              partialNote: "Fabrikam’s archive is still being read — more sources may appear.",
            },
            {
              type: "action", meta: "one step", phase: "accepted", failed: true,
              title: "Add the 12.11 date to the Fabrikam case",
              effect: "Writes the date into the case file.",
              cta: "Add it",
              result: "Could not add it — the case server rejected the write · 07:31",
            },
          ],
        },
      ],
    },
    {
      id: "c-invoice", title: "Invoice 08/2026 — what is missing", when: "Monday", archived: true,
      msgs: [
        { role: "user", text: "What is missing on invoice 08/2026?", ts: atToday(10, 5) },
        {
          role: "bot", ts: atToday(10, 5), scope: "1 account · accounting",
          text: "The invoice has no purchase-order number, which is why accounting bounced it.",
          blocks: [{
            type: "action", meta: "one step", phase: "accepted",
            title: "Ask Marek Wiśniewski for the PO number",
            effect: "Sends a one-line question into the invoice thread.",
            cta: "Ask him", result: "Sent into “Invoice 08/2026” · 10:06",
            nav: [{ label: "Open the thread", icon: "forum", kind: "thread", tid: "faktura1" }],
          }],
        },
      ],
    },
  ];

  const AGENT_REPLIES = [
    {
      match: /(plan|schedule).*(day)|plan my day/i,
      scope: "calendar · 14 new messages",
      text: "Two meetings today and one deadline tomorrow. One focus block would cover what is actually at risk.",
      bullets: [],
      blocks: [
        BK_TIMELINE_TODAY,
        {
          type: "event", meta: "calendar · work", phase: "pending",
          title: "Focus block: CPI cap counter-proposal",
          when: "today 16:00–17:00", duration: "1 h", attendees: "You", from: "Contract addendum — signatures",
          conflict: "overlaps Rollout status with Piotr (15:00–16:30)",
          done: "Added to the calendar",
          nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }],
        },
      ],
    },
    {
      match: /slip|missed|forgot|overdue/i,
      scope: "7 days · 3 accounts",
      text: "Three things have been waiting on you for more than two days — the audit list is the one with a date on it.",
      bullets: [],
      blocks: [
        {
          type: "task", meta: "tasks · from mail", phase: "pending",
          title: "Send the notice-period list to the audit",
          due: "tomorrow, 17:00", from: "IT contract audit", reminder: "09:00 tomorrow",
          done: "Added to tasks",
          nav: [{ label: "Open the thread", icon: "forum", kind: "thread", tid: "audyt1" }],
        },
        {
          type: "action", meta: "one step", phase: "pending",
          title: "Mark the Fabrikam thread as waiting on them",
          reason: "No reply from Tomasz Zieliński for 5 days.",
          effect: "Changes the thread state only — nothing is sent.",
          cta: "Mark it", done: "Marked as waiting on them",
          nav: [{ label: "Open the Fabrikam thread", icon: "forum", kind: "thread", tid: "fabrikam1" }],
        },
      ],
    },
    {
      match: /contoso|addendum|cpi|reply|write|draft/i,
      scope: "thread: Contract addendum — 6 messages",
      text: "We accept the 2 h SLA and ask for a 5% annual cap on indexation in return. The draft is ready for review.",
      bullets: [],
      blocks: [Object.assign({ phase: "pending" }, BK_DRAFT_CONTOSO)],
      sources: [
        { n: "1", title: "Contract addendum — signatures", snippet: "“Response time drops from 4 to 2 hours, fee +8%…”", meta: "18.11.2025 · SLA addendum.pdf", tid: "contoso" },
        { n: "2", title: "CPI calculation 2027", snippet: "“A 4.1% CPI forecast adds €10,542…”", meta: "yesterday · Marta Nowak", tid: "marta" },
      ],
    },
    {
      match: /case|status|contoso 2027|in progress/i,
      scope: "3 cases in progress",
      text: "The hottest one is the Contoso 2027 renegotiation — one open question and a decision tomorrow. Fabrikam and the audit have deadlines next week.",
      bullets: [],
      blocks: [
        {
          type: "facttable", meta: "Contoso renegotiation",
          cols: ["Version", "Price", "SLA", "Indexation"],
          rows: [
            ["v2 · 12.04.2021", "€1,200 / mo", "4 h", "CPI, no cap"],
            ["v3 · 18.11.2025", "€1,296 / mo", "2 h", "CPI, no cap"],
            ["v4 · proposed", "€1,296 / mo", "2 h", "CPI, 5% cap"],
          ],
        },
        {
          type: "action", meta: "one step", phase: "pending",
          title: "Attach the legal opinion to the Contoso 2027 case",
          reason: "The opinion answers the open question about the indexation cap and is not in the case file.",
          effect: "Adds the document to the case — nothing is sent to anyone.",
          cta: "Attach it", done: "Attached to Contoso 2027",
          nav: [{ label: "Open the case", icon: "folder_open", kind: "case" }],
        },
      ],
      sources: [
        { n: "1", title: "Contract addendum — signatures", snippet: "“Response time drops from 4 to 2 hours, fee +8%…”", meta: "18.11.2025 · SLA addendum.pdf", tid: "contoso" },
        { n: "2", title: "Q4 delivery dates", snippet: "“Delivery moved to 12.11…”", meta: "yesterday · Tomasz Bąk", tid: "fabrikam1" },
        { n: "3", title: "Contract register request", snippet: "“Please send a complete list of IT contracts for 2024–2026…”", meta: "yesterday · Internal Audit", tid: "audyt1" },
      ],
    },
  ];

  const AGENT_FALLBACK = {
    scope: "214,138 messages in scope",
    text: "I went through the correspondence in that scope. I can draft a reply, block time in the calendar or show the threads behind it — tell me what should happen next.",
    bullets: [],
    blocks: [{
      type: "action", meta: "one step", phase: "pending",
      title: "Draft a reply to the oldest unanswered thread",
      reason: "Three threads have been waiting on you for more than two days.",
      effect: "Writes a draft into the conversation — you approve it before it is sent.",
      cta: "Write it", done: "Draft prepared",
    }],
  };

  const AGENT_DRAFT_HTML = '<div id="ai-draft-block" style="border-left:3px solid var(--accent);padding-left:14px;margin-left:-14px"><p style="margin:0 0 12px 0">Hello,</p><p style="margin:0 0 12px 0">thank you for the addendum. We accept shortening the response time for critical incidents to 2 hours on business days.</p><p style="margin:0 0 12px 0">At the same time we ask for an upper cap on CPI indexation of 5% per year. At the 4.1% forecast this gives both sides a predictable cost through 2027.</p><p style="margin:0">If the cap is acceptable, we are ready to close the decision by 28 August.</p></div><p style="margin:12px 0 0 0">Best regards,<br />Karolina Kowalska</p>';

  Object.assign(MailFathomDesign.data, {
    timelineToday: BK_TIMELINE_TODAY,
    draftContoso: BK_DRAFT_CONTOSO,
    demoConversationId: DEMO_CONV_ID,
    demoScript: DEMO_SCRIPT,
    nextDefault: NEXT_DEFAULT,
    seedConversations: SEED_CONVS,
    agentReplies: AGENT_REPLIES,
    agentFallback: AGENT_FALLBACK,
    agentDraftHtml: AGENT_DRAFT_HTML,
  });
}

// The chips that open a finished conversation from an empty agent screen.
MailFathomDesign.data.starterChips = [
  { label: "Plan my day", id: "c-plan" },
  { label: "What slipped this week?", id: "c-slip" },
  { label: "Draft a reply to Contoso", id: "c-draft" },
  { label: "Status of open cases", id: "c-cases" },
];

// What the agent answers when a proposed time is turned down.
MailFathomDesign.data.anotherTimeReply = {
  scope: "calendar · next free slot",
  text: "The next clear hour is tomorrow morning, before the Fabrikam call.",
  when: "tomorrow 09:00–10:00",
};

// What the agent's calendar block puts on the calendar once accepted.
MailFathomDesign.data.agentScheduledEvents = [
  { day: "27", time: "10:00", h: 10, t: "Reply to Marta — calculation", kind: "meeting", src: "agent block" },
  { day: "27", time: "16:00", h: 16, t: "CPI cap counter-proposal", kind: "meeting", src: "agent block" },
];
