# Cursor 2.0 for Windows

Native Windows port of `JayChauhan2/Cursor-2.0`.

The goal of this repository is feature parity with the original macOS SwiftUI/AppKit app, not new behavior.

## Current Parity Scope

- Global cursor wiggle detection opens/closes the assistant overlay.
- Transparent always-on-top shadow cursor overlay follows the real cursor.
- Voice prompt recording with live audio level feedback.
- Groq Whisper transcription.
- Groq chat responses with the same concise system prompt.
- Tavily web search context when the Groq search-query step decides it is needed.
- Local memory and pending follow-up state in `%APPDATA%\Cursor`.
- Typed overlay input while active.
- Enter submits, Shift+Enter inserts a newline, Backspace edits, Escape closes.
- Screen capture, labeled grid targeting, fine crop refinement, and `SendInput` clicking for click/tap/press/select commands.
- Mini Tetris overlay launched by saying or typing `tetris`.

## Requirements

- Windows 10 or later.
- .NET 7 SDK or later.
- `GROQ_API_KEY` environment variable.
- `TAVILY_API_KEY` environment variable for search-backed answers.
- Microphone access allowed for the app.

## Run

```powershell
dotnet restore
dotnet run
```

Wiggle the cursor left-right a few times to open the assistant.
