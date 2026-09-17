// background.js — WebSocket ブリッジ (service worker)
// Content script ↔ background (chrome.runtime port) ↔ Unity (WebSocket)

let ws = null;
let port = 6090;
let connectAttempts = 0;
let contentPort = null; // chrome.runtime port to content script
let reconnectTimer = null;

function isValidPort(value) {
  const candidate = Number(value);
  return Number.isInteger(candidate) && candidate >= 1 && candidate <= 65535;
}

// ─── Service worker keepalive ───
// chrome.alarms fires even if the service worker was suspended, waking it back up.
// This ensures WebSocket reconnection happens even without a content script connection.

function startAlarmKeepalive() {
  chrome.alarms.create("keepalive", { periodInMinutes: 0.5 }); // 30s (MV3 minimum)
}

function stopAlarmKeepalive() {
  chrome.alarms.clear("keepalive");
}

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === "keepalive") {
    // Waking up is enough to keep the service worker alive.
    // Also check WebSocket health.
    if (contentPort && (!ws || ws.readyState > 1)) {
      connectWS();
    }
  }
});

// ─── WebSocket connection to Unity ───

function connectWS() {
  if (ws && ws.readyState <= 1) return;

  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }

  connectAttempts++;
  const url = `ws://127.0.0.1:${port}`;

  try {
    ws = new WebSocket(url);
  } catch (e) {
    console.error("[UnityAgent BG] WebSocket creation failed:", e);
    scheduleReconnect();
    return;
  }

  if (connectAttempts <= 3 || connectAttempts % 10 === 0) {
    console.log(`[UnityAgent BG] Connecting to ${url} (attempt ${connectAttempts})...`);
  }

  const socket = ws;

  ws.onopen = () => {
    // A delayed callback from a previous socket must not make a newer
    // connection appear connected.
    if (ws !== socket) return;
    connectAttempts = 0;
    console.log(`[UnityAgent BG] Connected to Unity Editor on port ${port}`);
    chrome.storage.local.set({ connected: true });
    startAlarmKeepalive();

    // Send ready from content script info if available
    if (contentPort) {
      contentPort.postMessage({ type: "ws_connected" });
    }
  };

  ws.onmessage = (event) => {
    if (ws !== socket) return;
    try {
      const msg = JSON.parse(event.data);
      // Forward to content script
      if (contentPort) {
        contentPort.postMessage(msg);
      }
    } catch (e) {
      console.error("[UnityAgent BG] Parse error:", e);
    }
  };

  ws.onclose = () => {
    if (ws !== socket) return;
    if (connectAttempts === 0) {
      console.log("[UnityAgent BG] Disconnected from Unity Editor");
    }
    chrome.storage.local.set({ connected: false });
    ws = null;
    stopAlarmKeepalive();
    if (contentPort) {
      contentPort.postMessage({ type: "ws_disconnected" });
    }
    scheduleReconnect();
  };

  ws.onerror = () => {};
}

function scheduleReconnect() {
  if (reconnectTimer || !contentPort) return;
  const delay = connectAttempts <= 3 ? 2000 : 5000;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connectWS();
  }, delay);
}

function sendToUnity(obj) {
  if (ws && ws.readyState === WebSocket.OPEN) {
    try {
      ws.send(JSON.stringify(obj));
    } catch (e) {
      console.error("[UnityAgent BG] WebSocket send failed:", e);
    }
  }
}

// ─── Content script port ───

chrome.runtime.onConnect.addListener((p) => {
  if (p.name !== "gemini-bridge") return;

  console.log("[UnityAgent BG] Content script connected");
  contentPort = p;

  // Start WebSocket if not already connected
  connectWS();

  // Tell content script current status
  const isConnected = ws && ws.readyState === WebSocket.OPEN;
  p.postMessage({ type: isConnected ? "ws_connected" : "ws_disconnected" });

  p.onMessage.addListener((msg) => {
    if (contentPort !== p || !msg || typeof msg !== "object") return;
    // "keepalive" — just receiving it keeps the service worker alive, no forwarding needed
    if (msg.type === "keepalive") return;

    // Messages from content script → forward to Unity
    if (msg.type === "ready" || msg.type === "partial" || msg.type === "complete" || msg.type === "error" || msg.type === "pong") {
      sendToUnity(msg);
    }
  });

  p.onDisconnect.addListener(() => {
    console.log("[UnityAgent BG] Content script disconnected");
    // A new tab can connect before the old port's disconnect event arrives.
    // Do not tear down the replacement port in that case.
    if (contentPort === p) {
      contentPort = null;
      if (reconnectTimer) {
        clearTimeout(reconnectTimer);
        reconnectTimer = null;
      }
      if (ws) ws.close();
    }
  });
});

// ─── Initialization ───

chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.local.set({ port: 6090, connected: false });
  console.log("[UnityAgent BG] Extension installed, default port: 6090");
});

chrome.storage.local.get(["port"], (data) => {
  if (isValidPort(data.port)) {
    const storedPort = Number(data.port);
    if (storedPort !== port) {
      port = storedPort;
      if (ws) ws.close();
    }
  }
  // Don't connect immediately — wait for content script
});

chrome.storage.onChanged.addListener((changes) => {
  if (changes.port) {
    if (!isValidPort(changes.port.newValue)) {
      console.warn("[UnityAgent BG] Ignoring invalid port:", changes.port.newValue);
      return;
    }
    const nextPort = Number(changes.port.newValue);
    port = nextPort;
    console.log(`[UnityAgent BG] Port changed to ${port}`);
    if (ws) {
      ws.close();
    }
  }
});
