# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-10-05

- Added AI guidance for setup, audio preview and selection, non-destructive processing and WAV export.
- Improved audio-preview recovery and editor cleanup on newer Unity versions while retaining Unity 2022.3 support.
- Simplified Tools and Project-window context-menu labels.

## [1.1.1] - 2026-08-07

- The welcome guide now opens once for standalone installations, stays quiet when bundled inside another Wetzold tool, and remains available from the Tools menu.

## [1.1.0] - 2026-07-30

- Rebuilt the Audio Editor with a polished, responsive UI Toolkit workspace.
- Reduced scripting clutter by keeping the audio editor, menus, and context-menu implementation internal.
- Added waveform zooming, precise selection timing, keyboard controls, and undo support.
- Restored automatic looping preview, responsive selection timing beneath the waveform, intuitive outside-click deselection, and streamlined zoom, source, and export controls.
- Improved empty and selection guidance while preserving the editor's warm audio-focused identity.
- Added clearer source, processing, clipping, and export feedback with safer WAV replacement.
- Added drag-and-drop opening, a guided welcome experience, and improved silence selection controls.
- Updated Audio Tools to require Unity 2022.3 or newer.

## [1.0.0] - 2026-01-23

- Initial release
- Select & Trim
- Auto-detect silence
- Fade in and out via curve
- Normalization & Volume controls
- Save back as .wav
