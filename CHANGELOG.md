# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [0.5.0] - 2026-04-02

### Changed
- VPM distribution switched from compiled DLL to **source code**
- Removed Obfuscar obfuscation — full source transparency
- Repository open-sourced under MIT license

### Added
- Update notification banner in main window
- Post-update changelog dialog (shown once per version)
- Claude CLI activity panel with live thinking/tool display
- Expressive loading animation during AI processing

### Fixed
- Claude CLI provider now correctly streams real-time output
- Inactivity-based timeout replaces fixed timeout (prevents false timeouts during active responses)
