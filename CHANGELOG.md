# Changelog

All notable user-facing changes are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-09-28

### Added / Hinzugefügt

- Touch-first pad view with automatic pages and no scrolling in the pad area.
- Scrollable list view in which the entire row starts or stops a sound.
- WAV and MP3 playback, looping, concurrent playback, and an optional mode that stops other pads when a new pad starts.
- Per-pad name, color, loop setting, relative volume, and start volume.
- Vertical master volume control with transfer to selected pads.
- Playback duration and current-position display.
- Persistent `.soundpadset` files, automatic saving, backups, and restoration of the last opened set.
- German and English user interface, top menu, and built-in help.
- Structured JSONL logging and diagnostics for playback and audio errors.
- Application icon and tablet-oriented responsive layout.

### Technical / Technisch

- WPF application targeting .NET 10 with NAudio 2.2.1 and WASAPI Shared output.
- Streamed audio playback for longer files without loading complete tracks into memory.
- Automated checks for mixer behavior, persistence, streaming, and UI rendering.

[1.0.0]: https://github.com/4DCreative/SoundPad/tree/v1.0.0
