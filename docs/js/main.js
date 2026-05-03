/* UnityAgent — Site main
 * Theme toggle, install tabs, dynamic data rendering.
 */
(function () {
  "use strict";

  const STORAGE_THEME = "ua-theme";

  // -------- Theme --------
  function detectTheme() {
    const saved = localStorage.getItem(STORAGE_THEME);
    if (saved === "dark" || saved === "light") return saved;
    return window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark";
  }
  function applyTheme(t) {
    document.documentElement.setAttribute("data-theme", t);
    document.querySelector('meta[name="theme-color"]').setAttribute("content", t === "dark" ? "#0E0E12" : "#FAFAF7");
  }
  function toggleTheme() {
    const next = document.documentElement.getAttribute("data-theme") === "dark" ? "light" : "dark";
    localStorage.setItem(STORAGE_THEME, next);
    applyTheme(next);
  }

  // -------- Install tabs --------
  function initTabs() {
    const tabs = document.querySelectorAll(".tab-btn");
    tabs.forEach(btn => {
      btn.addEventListener("click", () => {
        const target = btn.dataset.tab;
        tabs.forEach(b => {
          const active = b.dataset.tab === target;
          b.classList.toggle("is-active", active);
          b.setAttribute("aria-selected", String(active));
        });
        document.querySelectorAll(".install-panel").forEach(p => {
          p.classList.toggle("is-active", p.dataset.panel === target);
        });
      });
    });
  }

  // -------- Tool grid --------
  function renderTools() {
    const grid = document.getElementById("tool-grid");
    if (!grid || !window.UA_DATA) return;
    const lang = window.UA_I18N ? window.UA_I18N.lang() : "ja";
    grid.innerHTML = window.UA_DATA.TOOL_CATEGORIES.map(cat => {
      const t = cat[lang] || cat.ja;
      return `
        <div class="tool-card">
          <div class="tool-card-head">
            <span class="tool-card-name">${escapeHtml(t.name)}</span>
            <span class="tool-card-count">${cat.count}</span>
          </div>
          <p class="tool-card-desc">${escapeHtml(t.desc)}</p>
        </div>
      `;
    }).join("");
  }

  // -------- Provider table --------
  function renderProviders() {
    const tbody = document.getElementById("provider-tbody");
    if (!tbody || !window.UA_DATA) return;
    const lang = window.UA_I18N ? window.UA_I18N.lang() : "ja";
    const kindLabel = (k) => {
      const map = {
        cloud: window.UA_I18N.get("providers.kind.cloud"),
        local: window.UA_I18N.get("providers.kind.local"),
        cli: window.UA_I18N.get("providers.kind.cli"),
        bridge: window.UA_I18N.get("providers.kind.bridge"),
      };
      return map[k] || k;
    };
    tbody.innerHTML = window.UA_DATA.PROVIDERS.map(p => `
      <tr>
        <td><div class="provider-name">${escapeHtml(p.name)}</div></td>
        <td><span class="provider-kind-badge kind-${p.kind}">${escapeHtml(kindLabel(p.kind))}</span></td>
        <td>${escapeHtml(p.auth)}</td>
        <td>${escapeHtml(p[lang] || p.ja)}</td>
      </tr>
    `).join("");
  }

  // -------- Changelog --------
  function renderChangelog() {
    const el = document.getElementById("changelog-list");
    if (!el || !window.UA_DATA) return;
    const lang = window.UA_I18N ? window.UA_I18N.lang() : "ja";
    const groupLabel = (label) => {
      const k = `changelog.${label}`;
      return window.UA_I18N.get(k);
    };
    el.innerHTML = window.UA_DATA.CHANGELOG.map(entry => {
      const versionText = entry.isUnreleased ? window.UA_I18N.get("changelog.unreleased") : entry.version;
      const dateHtml = entry.date ? `<span class="changelog-date">${escapeHtml(entry.date)}</span>` : "";
      const groups = entry.groups.map(g => {
        const list = (g.items[lang] || g.items.ja || []).map(li => `<li>${escapeHtml(li)}</li>`).join("");
        return `
          <div class="changelog-group">
            <span class="changelog-group-label label-${g.label}">${escapeHtml(groupLabel(g.label))}</span>
            <ul>${list}</ul>
          </div>
        `;
      }).join("");
      return `
        <article class="changelog-entry">
          <header class="changelog-head">
            <span class="changelog-version ${entry.isUnreleased ? "is-unreleased" : ""}">${escapeHtml(entry.isUnreleased ? versionText : "v" + entry.version)}</span>
            ${dateHtml}
          </header>
          ${groups}
        </article>
      `;
    }).join("");
  }

  function escapeHtml(s) {
    if (s == null) return "";
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  function renderAll() {
    renderTools();
    renderProviders();
    renderChangelog();
  }

  // -------- Boot --------
  document.addEventListener("DOMContentLoaded", () => {
    applyTheme(detectTheme());
    document.getElementById("theme-toggle").addEventListener("click", toggleTheme);

    initTabs();
    renderAll();
  });

  // Re-render localized content when language changes.
  document.addEventListener("ua:langchange", renderAll);
})();
