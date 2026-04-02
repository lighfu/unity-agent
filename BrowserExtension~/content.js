// content.js — AI チャットサイト DOM 操作 (background service worker と通信)
(() => {
  "use strict";

  // ─── Site configurations ───

  const SITES = {
    "gemini.google.com": {
      input: [
        ".text-input-field_textarea-wrapper [contenteditable='true']",
        "input-area-v2 [contenteditable='true']",
        "input-container [contenteditable='true']",
        '[contenteditable="true"][role="textbox"]',
        "rich-textarea [contenteditable]",
      ],
      inputFallback(doc) {
        const wrapper = doc.querySelector(".text-input-field_textarea-wrapper");
        if (!wrapper) return null;
        if (wrapper.isContentEditable) return wrapper;
        const child = wrapper.querySelector("div, p");
        return child || wrapper;
      },
      send: [
        'button[aria-label="Send message"]',
        'button[aria-label="メッセージを送信"]',
        'input-container button.send-button',
        'button[data-tooltip="Send message"]',
      ],
      sendFallback(doc) {
        const buttons = doc.querySelectorAll("input-container button, input-area-v2 button");
        for (const btn of buttons) {
          const label = (btn.getAttribute("aria-label") || "").toLowerCase();
          if (label.includes("send") || label.includes("送信")) return btn;
        }
        for (const btn of doc.querySelectorAll("button")) {
          const label = (btn.getAttribute("aria-label") || "").toLowerCase();
          if (label.includes("send") || label.includes("送信")) return btn;
        }
        return null;
      },
      stop: [
        'button[aria-label="Stop streaming"]',
        'button[aria-label="ストリーミングを停止"]',
        'button[aria-label="Stop"]',
        'button[aria-label="停止"]',
        'button[aria-label="Cancel response"]',
        'button[aria-label="応答をキャンセル"]',
      ],
      stopFallback(doc) {
        const btns = doc.querySelectorAll("input-area-v2 button, input-container button");
        for (const btn of btns) {
          const label = (btn.getAttribute("aria-label") || "").toLowerCase();
          if (label.includes("stop") || label.includes("cancel") || label.includes("停止") || label.includes("キャンセル")) return btn;
        }
        return null;
      },
      newChat: [
        "new-chat-button button",
        'button[aria-label="New chat"]',
        'button[aria-label="新しいチャット"]',
        'a[aria-label="New chat"]',
        'a[aria-label="新しいチャット"]',
        'a[href="/app"]',
      ],
      newChatFallback(doc) {
        const links = doc.querySelectorAll("side-navigation-v2 a, nav a");
        for (const a of links) {
          const text = a.textContent.trim();
          if (text.includes("New chat") || text.includes("新しいチャット")) return a;
        }
        return null;
      },
      newChatUrl: "/app",
      response: "model-response",
      responseInner: [".markdown", "message-content"],
      markAttr: "data-ua-seen",
    },
    "chatgpt.com": {
      input: [
        "#prompt-textarea",
        "textarea[placeholder]",
        '[contenteditable="true"]',
      ],
      inputFallback: null,
      send: [
        'button[data-testid="send-button"]',
        'button[aria-label="Send prompt"]',
        'button[aria-label="プロンプトを送信する"]',
        'button[aria-label="プロンプトを送信"]',
        'button[aria-label="Send"]',
        'button[aria-label="送信"]',
      ],
      sendFallback: null,
      stop: [
        'button[data-testid="stop-button"]',
        'button[aria-label="Stop generating"]',
        'button[aria-label="停止"]',
        'button[aria-label="Stop"]',
        'button[aria-label="Stop streaming"]',
      ],
      stopFallback: null,
      newChat: [
        'a[data-testid="create-new-chat-button"]',
        'nav a[href="/"]',
        'a[aria-label="New chat"]',
        'a[aria-label="新しいチャット"]',
      ],
      newChatFallback: null,
      newChatUrl: "/",
      response: '[data-message-author-role="assistant"]',
      responseInner: [".markdown", ".prose", ".whitespace-pre-wrap"],
      markAttr: "data-ua-seen",
    },
    "copilot.microsoft.com": {
      input: [
        'textarea[placeholder*="Copilot"]',
        'textarea[placeholder*="Message"]',
        "textarea",
        '[contenteditable="true"][role="textbox"]',
        '[contenteditable="true"]',
      ],
      inputFallback: null,
      send: [
        'button[aria-label="Submit"]',
        'button[aria-label="送信"]',
        'button[aria-label="Send"]',
        'button[data-testid="send-button"]',
      ],
      sendFallback(doc) {
        // Copilot の送信ボタンはテキスト入力エリア付近の最後のボタン
        for (const btn of doc.querySelectorAll("button")) {
          const label = (btn.getAttribute("aria-label") || "").toLowerCase();
          if (label.includes("submit") || label.includes("send") || label.includes("送信")) return btn;
        }
        return null;
      },
      stop: [
        'button[aria-label="Stop responding"]',
        'button[aria-label="応答を停止"]',
        'button[aria-label="Stop"]',
        'button[aria-label="停止"]',
        'button[data-testid="stop-button"]',
      ],
      stopFallback(doc) {
        for (const btn of doc.querySelectorAll("button")) {
          const label = (btn.getAttribute("aria-label") || "").toLowerCase();
          if (label.includes("stop") || label.includes("停止")) return btn;
        }
        return null;
      },
      newChat: [
        'button[aria-label="Start new chat"]',
        'button[aria-label="新しいチャットを開始"]',
        'button[aria-label="New chat"]',
        'button[aria-label="New topic"]',
        'a[href="/"]',
      ],
      newChatFallback(doc) {
        for (const btn of doc.querySelectorAll("button, a")) {
          const label = (btn.getAttribute("aria-label") || btn.textContent || "").toLowerCase();
          if (label.includes("new chat") || label.includes("new topic") || label.includes("新しいチャット")) return btn;
        }
        return null;
      },
      newChatUrl: "/",
      // markAfterSend: response セレクタがユーザーメッセージにも一致するため、
      // 送信後に再マークしてユーザーの発言をスキップする
      markAfterSend: true,
      response: '[data-testid="chat-message"], [role="article"], .message, .response-message',
      responseInner: [".markdown", ".prose", ".content", ".text-message-content"],
      markAttr: "data-ua-seen",
    },
  };

  const site = SITES[location.hostname];
  if (!site) {
    console.warn("[UnityAgent] Unsupported site:", location.hostname);
    return;
  }

  let bgPort = null;
  let currentRequestId = null;
  let aborted = false;
  let wsConnected = false;
  let keepaliveTimer = null;
  let messageQueue = [];

  // ─── Service worker keepalive ───
  // Chrome MV3 service workers are terminated after ~30s of inactivity.
  // Periodic messages through the port count as activity and prevent this.

  function startKeepalive() {
    if (keepaliveTimer) return;
    keepaliveTimer = setInterval(() => {
      if (bgPort) {
        try { bgPort.postMessage({ type: "keepalive" }); }
        catch (e) { /* port already invalidated, onDisconnect will fire */ }
      }
    }, 25000); // Every 25s — under the 30s idle timeout
  }

  function stopKeepalive() {
    if (keepaliveTimer) { clearInterval(keepaliveTimer); keepaliveTimer = null; }
  }

  // ─── Message queue (safety net for brief disconnections) ───

  function flushQueue() {
    while (messageQueue.length > 0 && bgPort && wsConnected) {
      try { bgPort.postMessage(messageQueue.shift()); }
      catch (e) { break; }
    }
  }

  function send(obj) {
    if (bgPort && wsConnected) {
      try { bgPort.postMessage(obj); return; }
      catch (e) { /* fall through to queue */ }
    }
    // For partial responses, keep only the latest in the queue
    if (obj.type === "partial") {
      const idx = messageQueue.findIndex(m => m.type === "partial");
      if (idx >= 0) { messageQueue[idx] = obj; return; }
    }
    messageQueue.push(obj);
  }

  // ─── Status overlay (Shadow DOM isolated) ───

  let overlayBadge = null, overlayDot = null, overlayLabel = null;
  let overlayCollapseTimer = null;

  function initOverlay() {
    const host = document.createElement("div");
    host.id = "unity-agent-overlay";
    const shadow = host.attachShadow({ mode: "closed" });

    const style = document.createElement("style");
    style.textContent = `
      :host {
        position: fixed;
        bottom: 16px;
        right: 16px;
        z-index: 2147483647;
        pointer-events: none;
      }
      .badge {
        display: inline-flex;
        align-items: center;
        gap: 8px;
        padding: 8px 14px 8px 10px;
        border-radius: 100px;
        background: rgba(24, 24, 27, 0.88);
        backdrop-filter: blur(12px);
        -webkit-backdrop-filter: blur(12px);
        border: 1px solid rgba(255, 255, 255, 0.08);
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
        font-family: system-ui, -apple-system, sans-serif;
        font-size: 12px;
        line-height: 1;
        color: rgba(255, 255, 255, 0.9);
        pointer-events: auto;
        cursor: default;
        user-select: none;
        transition: padding 0.4s cubic-bezier(0.4,0,0.2,1),
                    gap 0.4s cubic-bezier(0.4,0,0.2,1);
        overflow: hidden;
        white-space: nowrap;
      }
      .badge.collapsed {
        padding: 8px;
        gap: 0;
      }
      .badge.collapsed .label {
        max-width: 0;
        opacity: 0;
      }
      .badge:hover {
        padding: 8px 14px 8px 10px;
        gap: 8px;
      }
      .badge:hover .label {
        max-width: 200px;
        opacity: 1;
      }
      .dot {
        width: 10px;
        height: 10px;
        border-radius: 50%;
        flex-shrink: 0;
        transition: all 0.3s ease;
      }
      .dot.disconnected {
        background: #71717a;
      }
      .dot.connected {
        background: #22c55e;
        box-shadow: 0 0 6px rgba(34, 197, 94, 0.4);
      }
      .dot.active {
        background: #3b82f6;
        box-shadow: 0 0 8px rgba(59, 130, 246, 0.5);
        animation: pulse 1.5s ease-in-out infinite;
      }
      @keyframes pulse {
        0%, 100% { transform: scale(1); opacity: 1; }
        50% { transform: scale(0.7); opacity: 0.4; }
      }
      .label {
        max-width: 200px;
        opacity: 1;
        overflow: hidden;
        transition: max-width 0.4s cubic-bezier(0.4,0,0.2,1),
                    opacity 0.3s ease;
      }
    `;

    overlayBadge = document.createElement("div");
    overlayBadge.className = "badge";
    overlayDot = document.createElement("span");
    overlayDot.className = "dot disconnected";
    overlayLabel = document.createElement("span");
    overlayLabel.className = "label";
    overlayLabel.textContent = "Unity 未接続";

    overlayBadge.append(overlayDot, overlayLabel);
    shadow.append(style, overlayBadge);
    document.body.appendChild(host);
  }

  function setOverlay(state, text) {
    if (!overlayDot) return;
    overlayDot.className = "dot " + state;
    overlayLabel.textContent = text;
    overlayBadge.classList.remove("collapsed");
    clearTimeout(overlayCollapseTimer);
    // Auto-collapse: connected after 4s, disconnected after 8s, active stays expanded
    if (state === "connected") {
      overlayCollapseTimer = setTimeout(() => overlayBadge.classList.add("collapsed"), 4000);
    } else if (state === "disconnected") {
      overlayCollapseTimer = setTimeout(() => overlayBadge.classList.add("collapsed"), 8000);
    }
  }

  function resetOverlay() {
    setOverlay(wsConnected ? "connected" : "disconnected",
               wsConnected ? "Unity 接続中" : "Unity 未接続");
  }

  // ─── Background port connection ───

  function connectToBackground() {
    try {
      bgPort = chrome.runtime.connect({ name: "gemini-bridge" });
    } catch (e) {
      console.error("[UnityAgent] Failed to connect to background:", e);
      setTimeout(connectToBackground, 2000);
      return;
    }
    console.log("[UnityAgent] Connected to background service worker");

    bgPort.onMessage.addListener((msg) => {
      switch (msg.type) {
        case "ws_connected":
          console.log("[UnityAgent] WebSocket connected to Unity Editor");
          wsConnected = true;
          bgPort.postMessage({
            type: "ready",
            userAgent: navigator.userAgent,
            geminiUrl: location.href,
          });
          flushQueue();
          setOverlay("connected", "Unity 接続中");
          break;

        case "ws_disconnected":
          console.log("[UnityAgent] WebSocket disconnected from Unity Editor");
          wsConnected = false;
          setOverlay("disconnected", "Unity 未接続");
          break;

        case "prompt":
          console.log(`[UnityAgent] Received prompt (id=${msg.id}, ${msg.text.length} chars, newSession=${msg.newSession})`);
          currentRequestId = msg.id;
          aborted = false;
          handlePrompt(msg.id, msg.text, !!msg.newSession);
          break;

        case "abort":
          if (msg.id === currentRequestId) {
            console.log("[UnityAgent] Abort requested");
            aborted = true;
            clickStopButton();
          }
          break;

        case "ping":
          bgPort.postMessage({ type: "pong" });
          break;
      }
    });

    bgPort.onDisconnect.addListener(() => {
      console.log("[UnityAgent] Background port disconnected, reconnecting...");
      bgPort = null;
      wsConnected = false;
      setOverlay("disconnected", "Unity 未接続");
      setTimeout(connectToBackground, 500);
    });

    startKeepalive();
  }

  // ─── DOM selectors (site-config driven) ───

  function findBySelectors(selectors, fallbackFn) {
    for (const sel of selectors) {
      const el = document.querySelector(sel);
      if (el) return el;
    }
    return fallbackFn ? fallbackFn(document) : null;
  }

  function findInputElement() {
    return findBySelectors(site.input, site.inputFallback);
  }

  function findSendButton() {
    return findBySelectors(site.send, site.sendFallback);
  }

  function findStopButton() {
    return findBySelectors(site.stop, site.stopFallback);
  }

  function clickStopButton() {
    const btn = findStopButton();
    if (btn) btn.click();
  }

  function findNewChatButton() {
    return findBySelectors(site.newChat, site.newChatFallback);
  }

  // ─── New chat ───

  async function startNewChat() {
    const existingResponses = document.querySelectorAll(site.response);
    if (existingResponses.length === 0) {
      console.log("[UnityAgent] Already in a new chat");
      return true;
    }

    console.log("[UnityAgent] Starting new chat...");

    const newChatBtn = findNewChatButton();
    if (newChatBtn) {
      console.log("[UnityAgent] Clicking new chat button:", newChatBtn.tagName, newChatBtn.className);
      newChatBtn.click();
      for (let i = 0; i < 50; i++) {
        await sleep(200);
        const responses = document.querySelectorAll(site.response);
        if (responses.length === 0) {
          console.log("[UnityAgent] New chat ready");
          return true;
        }
      }
      console.warn("[UnityAgent] New chat navigation didn't clear responses, trying URL fallback...");
    } else {
      console.warn("[UnityAgent] New chat button not found, trying URL fallback...");
    }

    // Fallback: URL navigation
    console.log(`[UnityAgent] Navigating to ${site.newChatUrl}`);
    window.location.href = site.newChatUrl;
    for (let i = 0; i < 50; i++) {
      await sleep(200);
      const responses = document.querySelectorAll(site.response);
      if (responses.length === 0) {
        console.log("[UnityAgent] New chat ready (via URL)");
        return true;
      }
    }

    console.error("[UnityAgent] Failed to start new chat");
    return false;
  }

  // ─── Response detection ───

  function markExistingResponses() {
    document.querySelectorAll(site.response).forEach((el) => {
      el.setAttribute(site.markAttr, "true");
    });
  }

  function getNewResponseContainer() {
    return document.querySelector(`${site.response}:not([${site.markAttr}])`);
  }

  function getResponseTextElement(container) {
    if (!container) return null;
    for (const sel of site.responseInner) {
      const el = container.querySelector(sel);
      if (el) return el;
    }
    return container;
  }

  // ─── Prompt handler ───

  async function handlePrompt(id, text, newSession) {
    try {
      setOverlay("active", "処理中...");
      if (newSession) {
        await startNewChat();
        await sleep(500);
      }

      const input = findInputElement();
      if (!input) {
        console.error("[UnityAgent] Input element not found");
        send({ type: "error", id, message: "入力欄が見つかりません。AI チャットサイトの画面を開いてください。" });
        return;
      }
      const isCE = input.isContentEditable;
      console.log("[UnityAgent] Found input:", input.tagName, "CE:", isCE,
        "class:", (input.className || "").substring(0, 60));

      markExistingResponses();

      await setInputText(input, text, isCE);
      await sleep(300);

      const currentText = isCE
        ? (input.innerText || input.textContent || "")
        : (input.value || "");
      console.log(`[UnityAgent] Input length after insert: ${currentText.length} (original: ${text.length})`);
      if (currentText.trim().length === 0) {
        send({ type: "error", id, message: "テキスト入力に失敗しました。" });
        return;
      }

      await sleep(200);
      const sendBtn = findSendButton();
      if (!sendBtn) {
        send({ type: "error", id, message: "送信ボタンが見つかりません。" });
        return;
      }
      console.log("[UnityAgent] Clicking send:", sendBtn.getAttribute("aria-label"));
      sendBtn.click();

      // サイトによっては response セレクタがユーザーメッセージにも一致するため、
      // 送信後にユーザーの発言をマークしてからアシスタント応答を待つ
      if (site.markAfterSend) {
        await sleep(1000);
        markExistingResponses();
        console.log("[UnityAgent] Re-marked after send (markAfterSend)");
      }

      await waitForNewResponse(id);
      if (aborted) resetOverlay();

    } catch (e) {
      console.error("[UnityAgent] handlePrompt error:", e);
      send({ type: "error", id, message: e.message || "Unknown error" });
      resetOverlay();
    }
  }

  async function setInputText(el, text, isContentEditable) {
    text = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n").replace(/\n{3,}/g, "\n\n");
    const expectedLen = text.replace(/\n/g, "").length * 0.8;

    if (isContentEditable) {
      el.focus();
      el.click();
      await sleep(150);

      // Strategy 1: insertText + insertLineBreak (simulates Shift+Enter → <br>
      // instead of Enter → <div>/<p>, avoiding doubled paragraph spacing)
      console.log("[UnityAgent] Strategy 1 (insertText+LineBreak)...");
      document.execCommand("selectAll", false, null);
      document.execCommand("delete", false, null);
      const lines = text.split("\n");
      for (let i = 0; i < lines.length; i++) {
        if (i > 0) document.execCommand("insertLineBreak", false, null);
        if (lines[i]) document.execCommand("insertText", false, lines[i]);
      }
      await sleep(100);
      const len1 = (el.innerText || "").length;
      console.log(`[UnityAgent] Strategy 1 (insertText): len=${len1}, expected=${expectedLen}`);
      if (len1 >= expectedLen) return;

      // Strategy 2: ClipboardEvent paste (text/plain)
      console.log("[UnityAgent] Strategy 2 (paste)...");
      el.focus();
      document.execCommand("selectAll", false, null);
      document.execCommand("delete", false, null);
      const sel = window.getSelection();
      const range = document.createRange();
      range.selectNodeContents(el);
      sel.removeAllRanges();
      sel.addRange(range);
      const dt = new DataTransfer();
      dt.setData("text/plain", text);
      el.dispatchEvent(new ClipboardEvent("paste", {
        clipboardData: dt, bubbles: true, cancelable: true,
      }));
      await sleep(200);
      const len2 = (el.innerText || "").length;
      console.log(`[UnityAgent] Strategy 2 (paste): len=${len2}`);
      if (len2 >= expectedLen) return;

      // Strategy 3: execCommand insertHTML
      console.log("[UnityAgent] Strategy 3 (insertHTML)...");
      el.focus();
      document.execCommand("selectAll", false, null);
      document.execCommand("delete", false, null);
      const html = escapeHtml(text).replace(/\n/g, "<br>");
      const ok = document.execCommand("insertHTML", false, html);
      await sleep(100);
      const len3 = (el.innerText || "").length;
      console.log(`[UnityAgent] Strategy 3 (insertHTML): ok=${ok}, len=${len3}`);
      if (len3 >= expectedLen) return;

      // Strategy 4: Direct DOM manipulation
      console.log("[UnityAgent] Strategy 4 (direct DOM)...");
      while (el.firstChild) el.removeChild(el.firstChild);
      for (const line of text.split("\n")) {
        const p = document.createElement("p");
        p.textContent = line || "\u200B";
        el.appendChild(p);
      }
      el.dispatchEvent(new Event("input", { bubbles: true, composed: true }));
      el.dispatchEvent(new InputEvent("input", {
        inputType: "insertText", data: text, bubbles: true, composed: true,
      }));
      return;
    }

    // textarea/input
    el.focus();
    const nativeSetter = Object.getOwnPropertyDescriptor(
      HTMLTextAreaElement.prototype, "value"
    )?.set || Object.getOwnPropertyDescriptor(
      HTMLInputElement.prototype, "value"
    )?.set;
    if (nativeSetter) nativeSetter.call(el, text);
    else el.value = text;
    el.dispatchEvent(new Event("input", { bubbles: true }));
  }

  // ─── Response monitoring ───

  async function waitForNewResponse(id) {
    // Wait for the response container to appear (max 30s)
    let container = null;
    for (let i = 0; i < 300; i++) {
      if (aborted) return;
      container = getNewResponseContainer();
      if (container) break;
      await sleep(100);
    }

    if (!container) {
      console.error("[UnityAgent] No new response appeared");
      send({ type: "error", id, message: "応答が開始されませんでした。" });
      resetOverlay();
      return;
    }

    console.log("[UnityAgent] New response found, monitoring...");

    let lastText = "";
    let stableCount = 0;
    let stopBtnEverSeen = false;
    let totalIterations = 0;

    while (true) {
      if (aborted) return;
      totalIterations++;

      // Re-query container and text element each iteration (React may replace DOM nodes)
      container = getNewResponseContainer() || container;
      const textEl = getResponseTextElement(container);
      const text = (textEl ? (textEl.innerText || textEl.textContent) : "") || "";

      if (text !== lastText) {
        lastText = text;
        stableCount = 0;
        if (text.length > 0) {
          send({ type: "partial", id, text });
          setOverlay("active", `応答受信中 (${text.length}字)`);
        }
      } else {
        stableCount++;
      }

      const stopBtn = findStopButton();
      if (stopBtn) stopBtnEverSeen = true;

      // A) Stop button appeared then disappeared + text non-empty + stable 0.5s → complete
      if (stopBtnEverSeen && !stopBtn && stableCount >= 5 && lastText.length > 0) {
        console.log("[UnityAgent] Stop button gone + stable 0.5s → complete");
        break;
      }

      // B) Stop button never seen + text non-empty + stable 1.5s (after 2s wait) → complete
      if (!stopBtnEverSeen && stableCount >= 15 && totalIterations >= 20 && lastText.length > 0) {
        console.log("[UnityAgent] No stop button + stable 1.5s → complete");
        break;
      }

      // C) Safety timeout: 5 min
      if (totalIterations >= 3000) {
        send({ type: "error", id, message: "応答のタイムアウト (5分)" });
        resetOverlay();
        return;
      }

      await sleep(100);
    }

    // Final text: re-query one more time for the latest DOM state
    await sleep(300);
    container = getNewResponseContainer() || container;
    const finalEl = getResponseTextElement(container);
    const finalText = (finalEl ? (finalEl.innerText || finalEl.textContent) : "") || "";
    console.log(`[UnityAgent] Response complete (${finalText.length} chars, stopBtnSeen=${stopBtnEverSeen})`);
    send({ type: "complete", id, text: finalText });
    currentRequestId = null;
    setOverlay("connected", "Unity 接続中");
  }

  // ─── Utility ───

  function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  function escapeHtml(str) {
    return str
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  // ─── Initialize ───

  initOverlay();
  connectToBackground();
  console.log("[UnityAgent] Content script loaded on", location.href);
})();
