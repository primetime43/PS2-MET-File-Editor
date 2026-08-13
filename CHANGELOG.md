# Changelog

All notable changes to Backyard Baseball PS2 Editor are documented here.

## [1.0.0] - 2026-08-12

First stable release of the game-focused editor.

### Added

- Dedicated editors for player records, biographies, portraits, animated player images, and textured 3D appearances.
- Live stadium editing for field settings, cameras, collision tags, ambient objects, splines, animations, and home-run boundaries.
- Gameplay and physics tuning with validated values and 27 presets.
- Team/league and season-schedule editors.
- RenderWare DFF/RWS, ANM, EVT facial-event, PS2 audio, texture, and model preview/export tools.
- Version-checked USA executable patches for content unlocks, home-run diagnostics, and dormant developer modes.
- Extracted-folder ISO rebuilding with validation and timestamped backup handling.
- GitHub Actions development builds and tag-triggered, self-contained Windows x64 releases.

### Safety and validation

- Structured DATA.MET edits preserve unknown content and rebuild archives when entries grow.
- Asset replacement validates texture, VAG, and PSS compatibility before saving.
- Executable patches verify original or editor-produced instruction signatures before writing.
- Material changes create timestamped backups.

Full installation notes, compatibility details, and highlights are in the
[v1.0.0 release notes](docs/releases/v1.0.0.md).
