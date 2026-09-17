const statusEl = document.getElementById("status");
const portInput = document.getElementById("portInput");
const saveBtn = document.getElementById("saveBtn");
const infoEl = document.getElementById("info");

// Load saved port and status
chrome.storage.local.get(["port", "connected"], (data) => {
  const savedPort = Number(data.port);
  portInput.value = Number.isInteger(savedPort) && savedPort >= 1 && savedPort <= 65535
    ? savedPort
    : 6090;
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
  const rawPort = portInput.value.trim();
  const port = Number(rawPort);
  if (!/^\d+$/.test(rawPort) || !Number.isInteger(port) || port < 1 || port > 65535) {
    infoEl.textContent = "ポートは 1〜65535 の整数で入力してください。";
    return;
  }

  chrome.storage.local.set({ port }, () => {
    infoEl.textContent = "ポートを " + port + " に設定しました。AI チャットページをリロードしてください。";
  });
});
