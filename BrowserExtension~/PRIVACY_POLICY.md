# Privacy Policy — Unity Agent - Web Browser Bridge

**Last updated:** 2026-02-20

## Overview

Unity Agent - Web Browser Bridge is a Chrome extension that connects web-based AI chat services (Google Gemini, ChatGPT, Microsoft Copilot) to a locally running Unity Editor instance via WebSocket. This policy explains what data the extension accesses and how it is handled.

## Data Collection

**This extension does not collect, store, or transmit any personal data.**

### What the extension accesses

- **DOM of supported AI chat sites** — The extension reads and writes to the DOM of Google Gemini, ChatGPT, and Microsoft Copilot pages to send prompts and retrieve AI responses on behalf of the local Unity Editor agent. It does **not** record, store, or transmit conversation content to any external server.

- **Local WebSocket connection** — The extension communicates exclusively with `ws://127.0.0.1` (localhost) to exchange messages with the Unity Editor running on the same machine. No data is sent to any remote server.

- **chrome.storage.local** — The extension stores the following settings locally on your device:
  - WebSocket port number
  - Connection state (connected / disconnected)

  These values never leave your device.

## Data Sharing

This extension does **not** share any data with third parties. All communication occurs between the browser and the locally running Unity Editor on the same machine.

## Network Communication

The only network communication performed by this extension is:
- **WebSocket to localhost (`ws://127.0.0.1:<port>`)** — bidirectional messaging with the Unity Editor process running on your computer.

No external servers, analytics services, or tracking endpoints are contacted.

## Permissions Justification

| Permission | Reason |
|---|---|
| `storage` | Save port number and connection state locally |
| `alarms` | Schedule periodic WebSocket reconnection attempts |
| Host permissions for AI chat sites | Inject content script to interact with AI chat UI |

## Changes to This Policy

If this privacy policy is updated, the changes will be reflected in this document with an updated date.

## Contact

For questions or concerns about this privacy policy, please open an issue at the project's GitHub repository.
