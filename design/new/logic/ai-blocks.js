// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.artboards.aiBlocks = (DCLogic, React) => {
  const {
    aiBlocksAnswerParts: ANSWER_PARTS_FULL,
    aiBlocksEvidence: EVIDENCE_FULL,
    aiBlocksPrivateEvidence: EVIDENCE_PRIVATE,
    aiBlocksTimeline: TIMELINE_FULL,
    aiBlocksFactTable: TABLE_FULL,
    aiBlocksPeople: PEOPLE_FULL,
    aiBlocksAttachments: GALLERY_FULL,
    aiBlocksThread: THREAD,
    aiBlocksCommitments: COMMITMENTS_FULL,
    aiBlocksAgreed: AGREED_FULL,
    aiBlocksOpenQuestions: OPEN_Q_FULL,
    aiBlocksParticipants: PARTICIPANTS,
    aiBlocksDraft: DRAFT,
    aiBlocksSuggestedAction: SUGGESTED_ACTION,
    aiBlocksUnknownBlock: UNKNOWN_BLOCK,
    aiBlocksProposals: PROPOSAL_SPECS,
  } = MailFathomDesign.data;

  const FRAMES = { tablet: { w: 1024, h: 768 }, fold: { w: 884, h: 832 }, phone: { w: 390, h: 844 } };

  const PROP_LABEL = { event: "EVENT PROPOSAL", task: "TASK PROPOSAL", draft: "DRAFT", action: "SUGGESTED ACTION" };

  function srcTypeInfo(type) {
    const M = {
      mail: { icon: "mail", label: "message" },
      attachment: { icon: "attach_file", label: "attachment" },
      calendar: { icon: "calendar_month", label: "event" },
      contact: { icon: "person", label: "contact" },
      multi: { icon: "layers", label: "multiple sources" },
    };
    return M[type] || M.mail;
  }
  const SRC_BADGE_STYLE = "display:inline-flex;align-items:center;gap:4px;font-size:10.5px;color:var(--muted);background:var(--rail);border-radius:10px;padding:2px 8px;white-space:nowrap;cursor:default";
  const SRC_BADGE_ICON_STYLE = "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:12px;line-height:1";
  function srcBadge(item) {
    const types = item.srcTypes && item.srcTypes.length > 1 ? item.srcTypes : [item.srcType || "mail"];
    if (types.length > 1) {
      return { icon: "layers", label: "multiple sources", title: types.map(t => srcTypeInfo(t).label).join(", ") };
    }
    const si = srcTypeInfo(types[0]);
    return { icon: si.icon, label: si.label, title: si.label };
  }

  function verdictInfo(v) {
    const M = {
      supported: { label: "supported", icon: "check_circle", bg: "var(--ok-soft)", color: "var(--ok-text)" },
      unsupported: { label: "unsupported", icon: "cancel", bg: "var(--warn-soft)", color: "var(--warn-text)" },
      outdated: { label: "outdated", icon: "history", bg: "var(--warn-soft)", color: "var(--warn-text)" },
      conflicting: { label: "conflicts with another source", icon: "sync_problem", bg: "var(--warn-soft)", color: "var(--warn-text)" },
    };
    return M[v] || M.supported;
  }
  function verdictChipStyle(v) {
    const i = verdictInfo(v);
    return "display:flex;align-items:center;gap:4px;font-size:10.5px;background:" + i.bg + ";color:" + i.color + ";border-radius:10px;padding:2px 8px";
  }
  const CELL_CITE_STYLE = "font-size:11px;color:var(--accent-d);background:var(--accent-soft);border-radius:4px;padding:2px 7px;cursor:pointer";
  function tableCell(row, fieldLabel, value, cite, verdict, isLast) {
    const hasCite = !!cite;
    const hasVerdict = !!verdict;
    const vi = hasVerdict ? verdictInfo(verdict) : null;
    return {
      value, hasCite, cite,
      ariaLabel: hasCite ? (fieldLabel + " “" + value + "”, row " + row.version + ": citation " + cite + (hasVerdict ? ", verdict " + vi.label : "")) : "",
      hasVerdict, verdictLabel: vi ? vi.label : "", verdictIcon: vi ? vi.icon : "",
      verdictChipStyle: hasVerdict ? verdictChipStyle(verdict) : "",
      verdictIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:11px;line-height:1",
      cellStyle: "font-size:14px;border-bottom:1px solid var(--line2);padding:10px " + (isLast ? "0" : "10px") + " 10px 0;display:flex;align-items:center;gap:6px;flex-wrap:wrap",
    };
  }

  class Component extends DCLogic {
    state = {};
    rootRef = React.createRef();

    componentDidMount() {
      this.measure();
      if (typeof ResizeObserver !== "undefined" && this.rootRef.current) {
        this.ro = new ResizeObserver(this.measure);
        this.ro.observe(this.rootRef.current);
      }
      if (typeof window !== "undefined") window.addEventListener("resize", this.measure);
    }
    componentWillUnmount() {
      if (this.ro) this.ro.disconnect();
      if (typeof window !== "undefined") window.removeEventListener("resize", this.measure);
    }
    measure = () => {
      const el = this.rootRef.current;
      const w = (el && el.clientWidth) || (typeof window !== "undefined" ? window.innerWidth : 1440);
      if (w && w !== this.state.vw) this.setState({ vw: w });
    };

    renderVals() {
      const theme = this.props.theme ?? "light";
      const stan = this.props.state ?? "ready";
      const mode = this.props.layout ?? "auto";
      const FR = FRAMES[mode] || null;
      const w = FR ? FR.w : (this.state.vw || 1440);
      const isMobile = w < 700;
      const stacked = isMobile || w < 1180;

      const isReady = stan === "ready";
      const isLoading = stan === "loading";
      const isPartial = stan === "partial";
      const isEmpty = stan === "empty";
      const isError = stan === "error";
      const isOffline = stan === "offline";
      const bodyReady = isReady || isPartial;

      const cardStyle = "display:flex;flex-direction:column;gap:12px;background:var(--panel);border:1px solid var(--line);border-radius:9px;padding:16px 18px";
      const headStyle = "display:flex;align-items:center;gap:10px;flex-wrap:wrap";
      const labelStyle = "font-family:'Instrument Sans',system-ui,sans-serif;font-size:11px;letter-spacing:0.1em;color:var(--muted)";
      const metaStyle = "margin-left:auto;font-size:12px;color:var(--muted);white-space:nowrap";
      const noteStyle = "display:flex;align-items:flex-start;gap:7px;font-size:12.5px;line-height:1.5;color:var(--warn-text);background:var(--warn-soft);border-radius:7px;padding:8px 11px";
      const noteIconStyle = "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:15px;line-height:1.3";
      const emptyBodyStyle = "display:flex;flex-direction:column;align-items:center;gap:7px;padding:22px 10px;text-align:center;color:var(--muted)";
      const emptyIconStyle = "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:26px;line-height:1;color:var(--faint)";
      const errorIconStyle = "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:26px;line-height:1;color:var(--warn)";
      const emptyTextStyle = "font-size:13px;color:var(--text2);max-width:380px;text-wrap:pretty";
      const emptyReasonStyle = "font-size:12px;color:var(--faint)";
      const retryBtnStyle = "font-size:12.5px;color:var(--onaccent);background:var(--accent);border-radius:7px;padding:7px 14px;margin-top:2px";
      const skelRows = [92, 78, 55].map(pct => ({ style: "height:13px;border-radius:6px;background:var(--hover);animation:mfpulse 1.4s ease-in-out infinite;width:" + pct + "%" }));

      const answerParts = (isPartial ? ANSWER_PARTS_FULL.slice(0, 2) : ANSWER_PARTS_FULL).map(p => Object.assign({}, p, {
        ariaLabel: "Citation " + p.cite + ": " + p.source,
      }));
      const evidenceItems = (isPartial ? EVIDENCE_FULL.slice(0, 2) : EVIDENCE_FULL).map(e => {
        const vi = verdictInfo(e.verdict);
        const sb = srcBadge(e);
        return Object.assign({}, e, {
          ariaLabel: "Evidence: " + e.source + " — " + sb.title + " — relevance " + e.relevance + ", verdict " + vi.label,
          verdictLabel: vi.label, verdictIcon: vi.icon, verdictChipStyle: verdictChipStyle(e.verdict),
          verdictIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:12px;line-height:1",
          badgeStyle: SRC_BADGE_STYLE, badgeIconStyle: SRC_BADGE_ICON_STYLE, badgeIcon: sb.icon, badgeLabel: sb.label, badgeTitle: sb.title,
        });
      });
      const timelineItems = (isPartial ? TIMELINE_FULL.slice(0, 3) : TIMELINE_FULL).map(t => Object.assign({}, t, {
        ariaLabel: "Event: " + t.title + ", " + t.date + " — citation " + t.cite,
      }));
      const tableRows = (isPartial ? TABLE_FULL.slice(0, 2) : TABLE_FULL).map(r => ({
        version: tableCell(r, "Version", r.version, r.versionCite),
        price: tableCell(r, "Price", r.price, r.priceCite, r.priceVerdict),
        sla: tableCell(r, "SLA", r.sla, r.slaCite, r.slaVerdict),
        index: tableCell(r, "Indexation", r.index, r.indexCite, r.indexVerdict, true),
      }));
      const peopleItems = (isPartial ? PEOPLE_FULL.slice(0, 2) : PEOPLE_FULL).map(p => Object.assign({}, p, {
        ariaLabel: "Person: " + p.name + ", " + p.role,
      }));
      const galleryItems = (isPartial ? GALLERY_FULL.slice(0, 2) : GALLERY_FULL).map(g => Object.assign({}, g, {
        ariaLabel: "Attachment: " + g.name + ", " + g.avail,
        availStyle: "font-size:10px;border-radius:9px;padding:2px 7px;" + (g.avail === "available" ? "background:var(--ok-soft);color:var(--ok-text)" : "background:var(--warn-soft);color:var(--warn-text)"),
      }));
      const threadCommitments = isPartial ? [] : COMMITMENTS_FULL;
      const threadAgreed = AGREED_FULL;
      const threadOpenQuestions = isPartial ? [] : OPEN_Q_FULL;

      const mIcon = (size, color) => "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:" + size + "px;line-height:1;flex:0 0 auto;color:" + color;
      const propBtn = (primary) => "display:inline-flex;align-items:center;justify-content:center;gap:7px;border-radius:9px;font-size:13.5px;white-space:nowrap;" +
        (isMobile ? "width:100%;min-height:44px;padding:0 16px;" : "padding:9px 15px;") +
        (primary ? "font-weight:600;color:var(--onaccent);background:var(--accent);border:1px solid var(--accent);" : "color:var(--text2);background:var(--panel);border:1px solid var(--border2);");
      const propSpecs = bodyReady ? PROPOSAL_SPECS : ["event", "task", "draft", "action"].map(t => PROPOSAL_SPECS.filter(s => s.type === t && s.phase === "pending")[0]);
      const propCards = propSpecs.map(s => {
        const declined = s.phase === "declined";
        const accepted = s.phase === "accepted";
        const pending = s.phase === "pending";
        const controls = !bodyReady ? [] : pending ? (
          s.type === "event" ? [
            { label: "Add to calendar", aria: "Add “" + s.title + "” to the calendar", primary: true },
            { label: "Another time" , aria: "Ask for another time for “" + s.title + "”" },
            { label: "Decline", aria: "Decline putting “" + s.title + "” in the calendar" },
          ] : s.type === "task" ? [
            { label: "Add", aria: "Add the task “" + s.title + "”", primary: true },
            { label: "Decline", aria: "Decline the task “" + s.title + "”" },
          ] : s.type === "draft" ? [
            { label: "Send", aria: "Send the draft to " + DRAFT.recipient, primary: true },
            { label: "Edit here", aria: "Edit the draft to " + DRAFT.recipient + " inside the conversation" },
            { label: "Discard", aria: "Discard the draft to " + DRAFT.recipient },
          ] : [
            { label: s.cta || "Do it", aria: s.title, primary: true },
            { label: "Decline", aria: "Decline: " + s.title },
          ]
        ) : (accepted && s.failed ? [{ label: "Try again", aria: "Try again: " + s.title, primary: true }] : []);
        return {
          label: PROP_LABEL[s.type],
          meta: s.meta || "",
          title: s.title,
          cardStyle: "display:flex;flex-direction:column;gap:10px;border-radius:9px;padding:16px 18px;" +
            (declined ? "border:1px dashed var(--border2);background:transparent;"
              : pending ? "border:1px solid var(--accent-line);background:var(--accent-soft);"
              : "border:1px solid var(--line);background:var(--panel);"),
          phaseLabel: pending ? "pending · needs confirmation" : declined ? "declined" : s.failed ? "accepted · not done" : "accepted",
          phaseIcon: pending ? "help" : declined ? "block" : s.failed ? "error" : "check_circle",
          phaseIconStyle: mIcon(13, "inherit"),
          phaseChipStyle: "display:inline-flex;align-items:center;gap:5px;border-radius:16px;padding:3px 9px;font-size:11px;white-space:nowrap;" +
            (pending ? "color:var(--accent-d);background:var(--panel);border:1px solid var(--accent-line);"
              : s.failed ? "color:var(--warn-text);background:var(--warn-soft);border:1px solid var(--warn-soft);"
              : "color:var(--muted);background:var(--rail);border:1px solid var(--line);"),
          isDeclined: declined,
          showBody: !declined,
          declinedStyle: "display:flex;align-items:center;gap:8px;flex-wrap:wrap;font-size:13px;color:var(--muted)",
          declinedIconStyle: mIcon(16, "var(--muted)"),
          strikeStyle: "text-decoration:line-through;min-width:0",
          titleStyle: "font-size:15px;font-weight:600;letter-spacing:-0.01em;text-wrap:pretty",
          kStyle: "flex:0 0 " + (isMobile ? 66 : 82) + "px;font-size:11.5px;letter-spacing:0.03em;color:var(--muted)",
          vStyle: "flex:1;min-width:0;font-size:13.5px;color:var(--text);text-wrap:pretty",
          fields: (s.fields || []).map(f => ({ k: f.name, v: f.value })),
          hasBody: (s.paras || []).length > 0,
          paras: (s.paras || []).map(p => ({ t: p })),
          paraStyle: "font-size:13.5px;line-height:1.6;color:var(--text2);text-wrap:pretty",
          hasConflict: !!s.conflict,
          conflict: s.conflict || "",
          conflictStyle: "display:flex;align-items:flex-start;gap:7px;font-size:12.5px;color:var(--warn-text);background:var(--warn-soft);border-radius:7px;padding:8px 11px;text-wrap:pretty",
          conflictIconStyle: mIcon(16, "var(--warn)"),
          hasNoConflictNote: !!s.noConflict,
          noConflictNote: s.noConflict || "",
          okStyle: "display:flex;align-items:center;gap:7px;font-size:12.5px;color:var(--ok-text);background:var(--ok-soft);border-radius:7px;padding:8px 11px;text-wrap:pretty",
          okIconStyle: mIcon(16, "var(--ok)"),
          hasReason: !!s.reason,
          reason: s.reason || "",
          reasonStyle: "font-size:13px;color:var(--text2);text-wrap:pretty",
          hasEffect: !!s.effect,
          effect: s.effect || "",
          effectStyle: "display:flex;align-items:flex-start;gap:7px;font-size:12.5px;color:var(--muted);text-wrap:pretty",
          effectIconStyle: mIcon(15, "var(--muted)"),
          partialNote: s.type === "event" ? "One calendar has not answered — a conflict may still appear."
            : s.type === "draft" ? "The closing paragraph is still being written."
            : "Part of the source data is still being read.",
          emptyIcon: s.type === "draft" ? "edit_note" : s.type === "task" ? "task_alt" : s.type === "event" ? "event_busy" : "task_alt",
          emptyText: s.type === "event" ? "No free slot long enough this week — nothing to propose."
            : s.type === "task" ? "Nothing in this thread carries a date or an owner."
            : s.type === "draft" ? "Nothing to draft — the last message in the thread is yours."
            : "Nothing worth doing here — the thread needs no step.",
          errorText: "Could not build this proposal.",
          errorReason: s.type === "event" ? "The calendar did not answer." : s.type === "task" ? "The task list did not answer." : "The model timed out.",
          retryAria: "Try again: " + PROP_LABEL[s.type].toLowerCase(),
          hasResult: accepted,
          result: s.result || "",
          resultIcon: s.failed ? "error" : "check_circle",
          resultIconStyle: mIcon(16, s.failed ? "var(--warn)" : "var(--ok)"),
          resultStyle: "display:flex;align-items:center;gap:7px;font-size:12.5px;text-wrap:pretty;color:" + (s.failed ? "var(--warn-text)" : "var(--ok-text)"),
          hasControls: controls.length > 0,
          controls: controls.map(c => ({ label: c.label, aria: c.aria, style: propBtn(!!c.primary) })),
          controlsStyle: (isMobile ? "display:flex;flex-direction:column;gap:8px;" : "display:flex;gap:8px;flex-wrap:wrap;align-items:center;") + "border-top:1px solid var(--line2);padding-top:10px",
          hasNav: !declined && (s.nav || []).length > 0,
          nav: (s.nav || []).map(n => ({
            label: n.label, icon: n.icon, aria: n.label + " — leaves the conversation, you can come straight back",
            iconStyle: mIcon(16, "var(--muted)"),
            style: "display:inline-flex;align-items:center;gap:7px;border:1px solid var(--border2);border-radius:20px;background:transparent;color:var(--text2);font-size:12.5px;white-space:nowrap;" +
              (isMobile ? "min-height:44px;padding:0 14px;" : "padding:6px 12px;"),
          })),
          navStyle: isMobile ? "display:flex;flex-direction:column;gap:8px" : "display:flex;gap:8px;flex-wrap:wrap",
        };
      });

      return {
        propCards,
        propSectionStyle: "display:flex;flex-direction:column;gap:14px",
        propGridStyle: isMobile || stacked
          ? "display:grid;grid-template-columns:minmax(0,1fr);gap:16px"
          : "display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px;align-items:start",
        rootRef: this.rootRef,
        themeAttr: theme,
        outerStyle: "min-height:100vh;display:flex;justify-content:center;background:var(--bg);color:var(--text);font-family:'Instrument Sans',system-ui,sans-serif;padding:" + (isMobile ? "0" : "32px 24px"),
        frameStyle: FR
          ? "width:" + FR.w + "px;max-width:100%;background:var(--bg);border:1px solid var(--border2);border-radius:16px;box-shadow:0 20px 60px var(--sh-3);padding:" + (isMobile ? "20px 14px 40px" : "28px 30px 48px") + ";display:flex;flex-direction:column;gap:22px"
          : "width:100%;max-width:1520px;display:flex;flex-direction:column;gap:24px",
        headerRowStyle: isMobile ? "display:flex;flex-direction:column;gap:12px" : "display:flex;align-items:flex-end;justify-content:space-between;gap:16px;flex-wrap:wrap",
        pageTitleStyle: isMobile ? "font-size:21px;font-weight:600;letter-spacing:-0.01em" : "font-size:27px;font-weight:600;letter-spacing:-0.015em",
        badgeRowStyle: "display:flex;gap:8px;flex-wrap:wrap",
        chipStyle: "font-size:12px;background:var(--rail);border:1px solid var(--line);border-radius:16px;padding:5px 12px;color:var(--text2)",
        stanChipStyle: "font-size:12px;background:var(--accent-soft);color:var(--accent-d);border-radius:16px;padding:5px 12px",
        layoutLabel: mode + (FR ? " · " + FR.w + "px" : " · " + w + "px"),
        stanLabel: stan,

        mainGridStyle: stacked ? "display:flex;flex-direction:column;gap:22px" : "display:grid;grid-template-columns:minmax(0,1.25fr) minmax(0,1fr);gap:22px;align-items:start",
        resultColStyle: "display:flex;flex-direction:column;gap:16px;min-width:0",
        evidenceColStyle: stacked ? "display:flex;flex-direction:column;gap:16px;min-width:0" : "display:flex;flex-direction:column;gap:16px;min-width:0;position:sticky;top:24px",
        legendCardStyle: "display:flex;flex-direction:column;gap:12px;background:var(--rail);border:1px solid var(--line);border-radius:9px;padding:16px 18px",

        citeChipStyle: CELL_CITE_STYLE,
        cardStyle, headStyle, labelStyle, metaStyle, noteStyle, noteIconStyle,
        emptyBodyStyle, emptyIconStyle, errorIconStyle, emptyTextStyle, emptyReasonStyle, retryBtnStyle,
        isLoading, bodyReady, isPartial, isEmpty, isError, isOffline,
        skelRows,
        offlineIcon: "cloud_off",
        offlineText: "No connection to the server — this block cannot be loaded.",
        offlineActionLabel: "Try again",

        answerMeta: isPartial ? "processing" : "1 paragraph · 4 citations",
        answerParts,
        answerPartialNote: "The paragraph about the 2027 proposal is missing — still processing.",
        answerEmptyText: "The query does not touch any thread in this scope — there is nothing to build an answer from.",
        answerErrorText: "Could not assemble an answer.",
        answerErrorReason: "The model timed out while synthesising citations.",

        evidenceMeta: (isPartial ? EVIDENCE_FULL.slice(0, 2).length : EVIDENCE_FULL.length) + " items · sorted by relevance",
        evidenceItems, evidencePrivate: Object.assign({}, EVIDENCE_PRIVATE, (() => { const sb = srcBadge(EVIDENCE_PRIVATE); return {
          ariaLabel: "Private evidence: " + EVIDENCE_PRIVATE.source + " — content unavailable",
          badgeStyle: SRC_BADGE_STYLE, badgeIconStyle: SRC_BADGE_ICON_STYLE, badgeIcon: sb.icon, badgeLabel: sb.label, badgeTitle: sb.title,
        }; })()),
        evidencePartialNote: "1 item missing — still being indexed.",
        evidenceEmptyText: "No documents met the relevance threshold for this query.",
        evidenceErrorText: "Could not fetch the source passages.",
        evidenceErrorReason: "The search index did not respond.",

        timelineMeta: timelineItems.length + " events · 2021–2026",
        timelineItems,
        timelineRowStyle: isMobile ? "display:flex;flex-direction:column;gap:0" : "display:flex;gap:0",
        tlItemStyle: isMobile
          ? "display:flex;flex-direction:column;gap:3px;border-top:2px solid var(--border2);padding:9px 0 11px 0"
          : "flex:1;display:flex;flex-direction:column;gap:5px;border-top:2px solid var(--border2);padding:10px 14px 0 0",
        timelinePartialNote: "The 2026 event is missing — its source is still being verified.",
        timelineEmptyText: "No events in the selected date range.",
        timelineErrorText: "Could not build the chronology.",
        timelineErrorReason: "Two sources give conflicting dates for the same event.",

        tableMeta: tableRows.length + " contract versions",
        tableGridStyle: "display:grid;grid-template-columns:1.3fr 1fr 0.8fr 1.4fr;gap:0" + (isMobile ? ";min-width:540px" : ""),
        tableRows,
        tablePartialNote: "2 versions with unconfirmed prices are missing.",
        tableEmptyText: "No column had coverage in the sources — the table stays empty.",
        tableErrorText: "Could not assemble the table.",
        tableErrorReason: "Mismatched column count between source rows.",

        peopleMeta: peopleItems.length + " people · role in the case",
        peopleItems,
        peoplePartialNote: "1 person with an unconfirmed role is missing.",
        peopleEmptyText: "No people linked to this query were found.",
        peopleErrorText: "Could not match people to threads.",
        peopleErrorReason: "Duplicates of the same contact in two mailboxes.",

        threadMeta: "thread: " + THREAD.subject + " · " + THREAD.messageCount + " messages",
        threadParticipants: PARTICIPANTS,
        threadParticipantsLabel: "2 participants",
        threadColsStyle: isMobile || stacked ? "display:flex;flex-direction:column;gap:14px" : "display:grid;grid-template-columns:repeat(3,1fr);gap:14px;align-items:start",
        threadAgreed, threadOpenQuestions, threadCommitments,
        threadPartialNote: "Commitments are still being extracted from the latest message.",
        threadEmptyText: "The thread holds no agreements, questions or commitments yet.",
        threadErrorText: "Could not extract the thread state.",
        threadErrorReason: "The message uses a quoting format the parser does not support.",

        galleryMeta: galleryItems.length + " files · availability checked",
        galleryGridStyle: isMobile ? "display:grid;grid-template-columns:repeat(2,1fr);gap:10px" : "display:grid;grid-template-columns:repeat(3,1fr);gap:10px",
        galleryItems,
        galleryPartialNote: "1 file is missing — still being virus-scanned.",
        galleryEmptyText: "Messages in this scope had no attachments.",
        galleryErrorText: "Could not check file availability.",
        galleryErrorReason: "The attachment store did not respond.",

        draftMeta: "local draft",
        draftStatusLabel: "Local draft — nothing sent",
        draftTo: DRAFT.to,
        draftSubject: DRAFT.subject,
        draftBody: isPartial
          ? DRAFT.partialBody
          : DRAFT.body,
        draftSources: isPartial ? [] : DRAFT.sources.map(d => ({ n: d.n, ariaLabel: "Draft source: " + d.source })),
        draftPartialNote: "The text is still being edited, sources not attached yet.",
        draftEmptyText: "No draft has been generated for this query yet.",
        draftErrorText: "Could not assemble the draft.",
        draftErrorReason: "A source citation points to a deleted message.",

        actionMeta: "1 action proposed",
        actionRowStyle: isMobile
          ? "display:flex;flex-direction:column;align-items:flex-start;gap:12px;background:var(--sub);border:1px solid var(--line);border-radius:9px;padding:14px 16px"
          : "display:flex;align-items:flex-start;gap:14px;background:var(--sub);border:1px solid var(--line);border-radius:9px;padding:14px 18px",
        actionTitle: SUGGESTED_ACTION.title,
        actionReason: SUGGESTED_ACTION.reason,
        actionEffect: isPartial ? SUGGESTED_ACTION.partialEffect : SUGGESTED_ACTION.effect,
        actionConfirmLabel: "needs confirmation",
        actionConfirmStyle: (isMobile ? "" : "margin-left:auto;") + "flex:0 0 auto;font-size:11px;background:var(--warn-soft);color:var(--warn-text);border-radius:11px;padding:4px 10px;white-space:nowrap",
        actionPartialNote: "The effect is undetermined — the simulation is still finishing.",
        actionEmptyText: "No actions worth proposing in this context.",
        actionErrorText: "Could not prepare an action suggestion.",
        actionErrorReason: "A safety rule blocked automatic preparation of the send.",

        crossHeadingStyle: "font-size:13px;font-weight:600;color:var(--text2);margin-top:6px",
        crossRowStyle: isMobile ? "display:flex;flex-direction:column;gap:14px" : "display:flex;gap:14px;align-items:stretch",
        unknownBlockName: UNKNOWN_BLOCK.name,
        schemaPlanVersion: UNKNOWN_BLOCK.planVersion,
        schemaClientVersion: UNKNOWN_BLOCK.clientVersion,
        schemaBannerText: "This result uses a newer plan schema version (" + UNKNOWN_BLOCK.planVersion + ") than this app supports (" + UNKNOWN_BLOCK.clientVersion + "). Some new block types may have been skipped.",

        citationSamples: [
          { label: "default", style: "font-size:11px;color:var(--accent-d);background:var(--accent-soft);border-radius:4px;padding:2px 7px;min-width:20px;text-align:center" },
          { label: "hover", style: "font-size:11px;color:var(--onaccent);background:var(--accent-2);border-radius:4px;padding:2px 7px;min-width:20px;text-align:center" },
          { label: "focus (Tab)", style: "font-size:11px;color:var(--accent-d);background:var(--accent-soft);border-radius:4px;padding:2px 7px;min-width:20px;text-align:center;outline:2px solid var(--accent);outline-offset:2px" },
        ],
        verdictLegend: ["supported", "unsupported", "outdated", "conflicting"].map(v => {
          const vi = verdictInfo(v);
          const descs = {
            supported: "the fact agrees with the source",
            unsupported: "the source does not confirm the fact",
            outdated: "the source was true but is no longer current",
            conflicting: "the source contradicts another source in the result",
          };
          return {
            icon: vi.icon, label: vi.label, desc: descs[v],
            chipStyle: verdictChipStyle(v),
            iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:12px;line-height:1",
          };
        }),
      };
    }
  }

  return Component;
};
