const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const extensionRoot = path.resolve(__dirname, "..");

function eventTarget() {
  const listeners = [];
  return {
    addListener(listener) {
      listeners.push(listener);
    },
    emit(...args) {
      for (const listener of [...listeners]) listener(...args);
    },
  };
}

function createPort() {
  const port = {
    name: "gemini-bridge",
    messages: [],
    onMessage: eventTarget(),
    onDisconnect: eventTarget(),
    postMessage(message) {
      this.messages.push(message);
    },
  };
  return port;
}

class MockWebSocket {
  static OPEN = 1;
  static instances = [];

  constructor(url) {
    this.url = url;
    this.readyState = 0;
    this.sent = [];
    MockWebSocket.instances.push(this);
  }

  send(message) {
    if (this.readyState !== MockWebSocket.OPEN) {
      throw new Error("socket is not open");
    }
    this.sent.push(message);
  }

  close() {
    // Keep close and the close event separate so tests can deliver a stale
    // callback after a replacement socket has already been installed.
    this.readyState = 3;
  }

  emitOpen() {
    this.readyState = MockWebSocket.OPEN;
    if (this.onopen) this.onopen();
  }

  emitClose() {
    this.readyState = 3;
    if (this.onclose) this.onclose();
  }
}

function loadBackground() {
  MockWebSocket.instances = [];
  const runtime = { onConnect: eventTarget(), onInstalled: eventTarget() };
  const alarms = {
    created: [],
    cleared: [],
    onAlarm: eventTarget(),
    create(name, options) {
      this.created.push({ name, options });
    },
    clear(name) {
      this.cleared.push(name);
    },
  };
  const storageState = { port: 6090, connected: false };
  const storage = {
    local: {
      get(keys, callback) {
        callback({ port: storageState.port });
      },
      set(values) {
        Object.assign(storageState, values);
      },
    },
    onChanged: eventTarget(),
  };
  const chrome = {
    alarms,
    runtime,
    storage,
  };
  const sandbox = {
    chrome,
    WebSocket: MockWebSocket,
    console: { log() {}, warn() {}, error() {} },
    setTimeout,
    clearTimeout,
  };

  const source = fs.readFileSync(path.join(extensionRoot, "background.js"), "utf8");
  vm.runInNewContext(source, sandbox, { filename: "background.js" });
  return { alarms, chrome, runtime, storage, storageState };
}

test("background ignores a delayed close from a replaced WebSocket", () => {
  const { alarms, runtime, storageState } = loadBackground();
  const port = createPort();
  runtime.onConnect.emit(port);

  const firstSocket = MockWebSocket.instances[0];
  firstSocket.emitOpen();
  const disconnectedBeforeReplacement = port.messages.filter(
    (message) => message.type === "ws_disconnected"
  ).length;

  // Chrome may ask for a reconnect while the old socket is still CLOSING.
  firstSocket.readyState = 2;
  alarms.onAlarm.emit({ name: "keepalive" });
  const replacementSocket = MockWebSocket.instances[1];
  assert.ok(replacementSocket, "a replacement socket should be created");

  firstSocket.emitClose();
  assert.equal(storageState.connected, true, "the stale close must not clear connection state");
  assert.equal(
    port.messages.filter((message) => message.type === "ws_disconnected").length,
    disconnectedBeforeReplacement
  );

  replacementSocket.emitOpen();
  assert.equal(port.messages.at(-1).type, "ws_connected");
});

test("background keeps the replacement content port after the old tab disconnects", () => {
  const { runtime } = loadBackground();
  const oldPort = createPort();
  const replacementPort = createPort();
  runtime.onConnect.emit(oldPort);
  const socket = MockWebSocket.instances[0];

  runtime.onConnect.emit(replacementPort);
  oldPort.onDisconnect.emit();
  socket.emitOpen();

  assert.equal(
    replacementPort.messages.at(-1).type,
    "ws_connected",
    "the active tab should still receive WebSocket status updates"
  );
});

test("background forwards an answer that arrives on a replaced content port", () => {
  const { runtime } = loadBackground();
  const streamingPort = createPort();
  runtime.onConnect.emit(streamingPort);
  const socket = MockWebSocket.instances[0];
  socket.emitOpen();

  // A second tab connects while the first one is still producing the answer.
  const newTabPort = createPort();
  runtime.onConnect.emit(newTabPort);

  streamingPort.onMessage.emit({ type: "partial", id: "req-1", text: "half" });
  streamingPort.onMessage.emit({ type: "complete", id: "req-1", text: "done" });

  const sent = socket.sent.map((raw) => JSON.parse(raw));
  assert.ok(
    sent.some((message) => message.type === "complete" && message.id === "req-1"),
    "Unity waits on the request id with no deadline, so the answer must still be delivered"
  );
  assert.ok(
    !sent.some((message) => message.type === "partial"),
    "streaming updates from the replaced port are still dropped"
  );
});

class FakeElement {
  constructor(tagName = "div") {
    this.tagName = tagName.toUpperCase();
    this.children = [];
    this.attributes = new Map();
    this.className = "";
    this.classList = {
      add: (...names) => names.forEach((name) => this._addClass(name)),
      remove: (...names) => names.forEach((name) => this._removeClass(name)),
    };
    this._value = "";
    this.innerText = "";
    this.textContent = "";
    this.isContentEditable = false;
  }

  _addClass(name) {
    this.className = `${this.className} ${name}`.trim();
  }

  _removeClass(name) {
    this.className = this.className.split(/\s+/).filter((item) => item && item !== name).join(" ");
  }

  get value() {
    return this._value;
  }

  set value(value) {
    this._value = value;
  }

  append(...children) {
    this.children.push(...children);
  }

  appendChild(child) {
    this.children.push(child);
    return child;
  }

  attachShadow() {
    return { append() {} };
  }

  setAttribute(name, value) {
    this.attributes.set(name, String(value));
  }

  getAttribute(name) {
    return this.attributes.get(name) || null;
  }

  querySelector() {
    return null;
  }

  focus() {}

  click() {
    if (this.onclick) this.onclick();
  }

  dispatchEvent() {
    return true;
  }
}

function loadContent(options = {}) {
  const port = createPort();
  const input = new FakeElement(options.contentEditable ? "div" : "textarea");
  input.isContentEditable = !!options.contentEditable;
  const sendButton = new FakeElement("button");
  const responses = [];
  const execCommands = [];
  let responseQueries = 0;
  const body = new FakeElement("body");
  const newChatButton = new FakeElement("button");
  if (options.initialResponse) {
    const existingResponse = new FakeElement("div");
    existingResponse.innerText = "existing response";
    responses.push(existingResponse);
    newChatButton.click = () => responses.splice(0);
  }
  sendButton.click = () => {
    const response = new FakeElement("div");
    response.innerText = `response-${responses.length + 1}`;
    responses.push(response);
  };

  const document = {
    body,
    createElement(tagName) {
      return new FakeElement(tagName);
    },
    execCommand(command, _showUi, value) {
      execCommands.push(command);
      if (!input.isContentEditable) return true;
      if (command === "delete") {
        input.innerText = "";
      } else if (command === "insertLineBreak") {
        input.innerText += "\n";
      } else if (command === "insertText") {
        input.innerText += value || "";
      } else if (command === "insertHTML") {
        input.innerText = String(value || "").replace(/<br>/g, "\n").replace(/<[^>]+>/g, "");
      }
      input.textContent = input.innerText;
      return true;
    },
    querySelector(selector) {
      if (selector.includes("data-message-author-role")) {
        return responses.find((response) => !response.attributes.has("data-ua-seen")) || null;
      }
      if (options.initialResponse && selector.includes("create-new-chat-button")) return newChatButton;
      if (selector.includes("prompt-textarea") || selector.includes("textarea")) return input;
      if (selector.includes("send-button")) return sendButton;
      return null;
    },
    querySelectorAll(selector) {
      responseQueries++;
      if (selector.includes("data-message-author-role")) return responses;
      return [];
    },
  };
  const runtime = {
    connect() {
      return port;
    },
  };
  const sandbox = {
    chrome: { runtime },
    document,
    location: { hostname: "chatgpt.com", href: "https://chatgpt.com/" },
    navigator: { userAgent: "test-agent" },
    window: {},
    HTMLTextAreaElement: function HTMLTextAreaElement() {},
    HTMLInputElement: function HTMLInputElement() {},
    Event: class Event {},
    InputEvent: class InputEvent {},
    console: { log() {}, warn() {}, error() {} },
    setTimeout(callback) {
      return setTimeout(callback, 1);
    },
    clearTimeout,
    setInterval() {
      return 1;
    },
    clearInterval() {},
  };
  Object.defineProperty(sandbox.HTMLTextAreaElement.prototype, "value", {
    configurable: true,
    get() {
      return this._value;
    },
    set(value) {
      this._value = value;
    },
  });
  Object.defineProperty(sandbox.HTMLInputElement.prototype, "value", {
    configurable: true,
    get() {
      return this._value;
    },
    set(value) {
      this._value = value;
    },
  });

  const source = fs.readFileSync(path.join(extensionRoot, "content.js"), "utf8");
  vm.runInNewContext(source, sandbox, { filename: "content.js" });
  return { execCommands, input, port, responseQueries: () => responseQueries, responses };
}

function waitFor(predicate, timeout = 5000) {
  const started = Date.now();
  return new Promise((resolve, reject) => {
    const check = () => {
      if (predicate()) return resolve();
      if (Date.now() - started >= timeout) {
        return reject(new Error("Timed out waiting for test condition"));
      }
      setTimeout(check, 2);
    };
    check();
  });
}

test("content script does not complete an aborted request after a replacement prompt", async () => {
  const { port, responses } = loadContent();
  port.onMessage.emit({ type: "ws_connected" });
  port.onMessage.emit({ type: "prompt", id: "first", text: "first prompt" });
  await waitFor(() => responses.length === 1);

  port.onMessage.emit({ type: "abort", id: "first" });
  port.onMessage.emit({ type: "prompt", id: "second", text: "second prompt" });
  await waitFor(() => responses.length === 2);
  await waitFor(() => port.messages.some((message) => message.type === "complete" && message.id === "second"));

  const completions = port.messages.filter((message) => message.type === "complete");
  assert.deepEqual(completions.map((message) => message.id), ["second"]);
});

test("content script answers a request that a newer prompt replaced", async () => {
  const { port, responses } = loadContent();
  port.onMessage.emit({ type: "ws_connected" });
  port.onMessage.emit({ type: "prompt", id: "first", text: "first prompt" });
  port.onMessage.emit({ type: "prompt", id: "second", text: "second prompt" });

  await waitFor(() => responses.length === 1);
  await waitFor(() => port.messages.some((message) => message.type === "complete" && message.id === "second"));

  const firstAnswers = port.messages.filter((message) => message.id === "first");
  assert.deepEqual(
    firstAnswers.map((message) => message.type),
    ["error"],
    "the superseded request must be answered exactly once, not left hanging"
  );
});

test("content script stops contenteditable input work after request replacement", async () => {
  const { execCommands, input, port, responses } = loadContent({ contentEditable: true });
  port.onMessage.emit({ type: "ws_connected" });
  port.onMessage.emit({ type: "prompt", id: "first", text: "first prompt" });
  port.onMessage.emit({ type: "prompt", id: "second", text: "second prompt" });

  await waitFor(() => responses.length === 1);
  await waitFor(() => port.messages.some((message) => message.type === "complete" && message.id === "second"));

  assert.deepEqual(execCommands, ["selectAll", "delete", "insertText"]);
  assert.equal(input.innerText, "second prompt");
});

test("content script skips stale new-chat polling after request replacement", async () => {
  const { port, responseQueries, responses } = loadContent({ initialResponse: true });
  port.onMessage.emit({ type: "ws_connected" });
  port.onMessage.emit({ type: "prompt", id: "first", text: "first prompt", newSession: true });
  port.onMessage.emit({ type: "prompt", id: "second", text: "second prompt" });
  const queriesAfterReplacement = responseQueries();

  await waitFor(() => responses.length === 1);
  await waitFor(() => port.messages.some((message) => message.type === "complete" && message.id === "second"));

  assert.equal(
    responseQueries(),
    queriesAfterReplacement,
    "the canceled request must not continue polling the old chat"
  );
});
