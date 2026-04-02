const statusEl = document.getElementById("status");
const portInput = document.getElementById("portInput");
const saveBtn = document.getElementById("saveBtn");
const infoEl = document.getElementById("info");

// Load saved port and status
chrome.storage.local.get(["port", "connected"], (data) => {
  portInput.value = data.port || 6090;
  updateStatus(data.connected || false);
});

// Listen for status updates
chrome.storage.onChanged.addListener((changes) => {
  if (changes.connected) {
    updateStatus(changes.connected.newValue);
  }
});

function updateStatus(connected) {
  if (connected) {
    statusEl.textContent = "Unity Editor に接続中";
    statusEl.className = "status connected";
  } else {
    statusEl.textContent = "未接続 (Unity でサーバーを起動してください)";
    statusEl.className = "status disconnected";
  }
}

saveBtn.addEventListener("click", () => {
  const port = parseInt(portInput.value, 10);
  if (port > 0 && port <= 65535) {
    chrome.storage.local.set({ port }, () => {
      infoEl.textContent = "ポートを " + port + " に設定しました。AI チャットページをリロードしてください。";
    });
  }
});
