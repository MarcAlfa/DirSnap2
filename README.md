# DirSnap 2

**DirSnap 2** is a fast Windows 11 file browser with a built-in text preview and editor, made for working with folders full of notes, scripts and code (Markdown, PL/SQL, Python, JavaScript...). It is written in **C# / .NET 8 / WinUI 3** (Windows App SDK, unpackaged) and replaces the Python/Tkinter DirSnap v1.

- **Current version**: `0.2.0` (single source: `AppConstants.AppVersion` in `AppConstants.cs`)
- **History**: [CHANGELOG.md](CHANGELOG.md) · **Next ideas**: [TODO.md](TODO.md)
- **UI language**: English
- **License**: [MIT](LICENSE)

---

## Download & install

1. Download `DirSnap2_vX.Y.Z_win-x64.zip` from the [Releases](https://github.com/MarcAlfa/DirSnap2/releases) page.
2. Extract it to a folder **you can write to** (e.g. `Documents\DirSnap2` or `C:\Tools\DirSnap2`, not `C:\Program Files`): settings and logs are saved next to the exe.
3. Start `DirSnap2.exe`.

Requirements: Windows 10 version 2004 or later, or Windows 11 (x64). Nothing else to install: .NET 8 and the Windows App SDK are included. The exe is not signed, so Windows SmartScreen may ask for confirmation the first time (*More info* → *Run anyway*).

To update, replace the folder content with the new version: your `config.json` (tabs, favorites, Snap List, settings) is not in the zip, so it is kept.

---

## Layout

Each **tab** (up to 10, reorderable, restored at startup) has two resizable panels:

- **Left**: the file list of the current folder, with the *Folder contents* panel under it.
- **Right**: the preview / editor of the selected file, with the find bar, the *Outline* panel and a footer (path, line counter, encoding, line endings).

On top: address bar, back / forward / up, drives, favorites, the global **Snap List**, the **AI Assistant** and Settings. Panel sizes, the open file, sort and filter of each tab are saved in `config.json`.

## File list

- Compact list with *Name*, *Size*, *Modified* columns; folders and files mixed, sorted by **last modified first** by default. Click a column to sort by it (dates and sizes start descending, names A-Z).
- **Search bar** (magnifier button or `Ctrl+F`): quick filter (`*.ext` or text), *Show all* (also files without an editor extension, shown dimmed) and *In files*.
- **Folder tree** flyout from the current folder bar, to jump anywhere quickly.
- **Live updates**: the list and the Folder contents panel follow changes made by other programs (polling every ~2 s, no handle kept on the folders).
- **Multiple selection** with `Ctrl` / `Shift` + click.
- **Quick buttons**: `+ TXT` / `+ MD` create a file named with the current date and time and open it in edit mode; `+ folder` creates a folder.

### File operations (right-click or keyboard)

| Action | Keys | Notes |
|---|---|---|
| Cut / Copy / Paste | `Ctrl+X` / `Ctrl+C` / `Ctrl+V` | Works with Explorer both ways; copy/move go through the Windows shell (progress window, "replace or skip" question, undo) |
| Duplicate | | "name - Copy" next to the original |
| Rename | `F2` | Validation inside the dialog; the open file follows the rename |
| Delete | `Del` | Asks twice, then moves to the **Recycle Bin**; refused on drives without one (network, USB) |
| Folder info | | Size, files and subfolders at every level, newest file inside |
| Open in new tab / Explorer / Terminal, Copy full / WSL path | | |

Rename, delete and move are blocked when they would affect a file with unsaved edits.

### Folder contents panel

Under the list: the files and subfolders of the folder **selected** in the list, without entering it. Double-click opens a subfolder, or a file in its folder. Resizable and collapsible.

### Search in files

`Ctrl+Shift+F` or *In files*: finds a text in the files of the current folder (and subfolders, optional) whose extension is one of the editor extensions. Results are grouped by file with line numbers; clicking one opens the file with the find bar on that match. `.git`, `node_modules` and similar folders, binary files and files over 5 MB are skipped.

## Preview & editor

- **Instant read-only preview** of the files whose extension is enabled in Settings (e.g. `.md`, `.txt`, `.json`, `.sql`, `.bat`); other text files can be previewed on request, never modified. Binary and over 5 MB files are not opened.
- **Edit mode** (`Ctrl+E`, or double-click), save with `Ctrl+S`. Unsaved changes are never lost: switching file, tab or closing asks first; a file changed on disk by another program is detected before saving.
- **Encoding and line endings preserved**: UTF-8, UTF-8 BOM, ANSI (Windows-1252); CRLF / LF / CR.
- **Markdown** shown with syntax colors; **JSON** formatter and validator; word wrap; adjustable font size.
- **Find in file** (`Ctrl+F` in the preview, `F3` / `Shift+F3`), also in read-only mode, with match counter and *Match case*.
- **Line counter** in the footer (`Ln 12 / 340`) and **Go to line** (`Ctrl+G` or click on the counter).
- **Outline** panel: functions, procedures, classes and headings of the open file (PL/SQL, also inside `.txt` files, Python, JavaScript/TypeScript, Markdown). Click an entry to jump to it; the entry you are reading is highlighted while scrolling.

## Snap List

A global "basket" of files and folders, shared by all tabs: add items with the `+` button on each row, `Ins` or the right-click menu (multiple selection supported), or all folders/files of the current folder at once. Export with full path or name only, with or without extension: clipboard, Notepad, `.txt`, `.csv`. It can be detached into a floating always-on-top window.

## AI Assistant

A chat about your files (`✨ AI` button or `Ctrl+I`), as a flyout or detached into a floating window that remembers its place. Nothing is sent unless you tick it: the file of the current tab (unsaved edits included), files of other tabs (to compare them), the Snap List files, or only the names of the current folder. An unchanged context is not sent again in the same chat, and a size limit stops oversized requests.

- **Providers**: Google Gemini, OpenAI, Anthropic Claude and any OpenAI-compatible server (Ollama, Groq, OpenRouter, DeepSeek), with streaming replies. Model names are free text.
- **Quick actions**: *Compare Tabs*, *Explain*, *Find Bugs*, *Summarize*.
- **Replies**: copy, save as a timestamped `.md` in the current folder, or *Send to Editor* (replace or append, unsaved until `Ctrl+S`; a reply with one code block sends only the code). The whole chat can be exported as `.md`.
- API keys are stored encrypted for your Windows user (DPAPI). The chat lives in memory only.

## Keyboard shortcuts

`F1` shows the full list (also from Settings → *Keyboard Shortcuts*). The main ones:

| Keys | Action |
|---|---|
| `Alt+Left` / `Alt+Right` / `Alt+Up`, `Backspace` | Back / forward / parent folder |
| `F5` | Refresh |
| `Ctrl+L` | Address bar |
| `Ctrl+F` / `Ctrl+Shift+F` | Quick filter (or find in file, from the preview) / search in files |
| `Ctrl+T` / `Ctrl+W` / `Ctrl+Tab` | New / close / next tab |
| `Ctrl+E` / `Ctrl+S` / `Ctrl+G` | Edit mode / save / go to line |
| `Ctrl+X` / `Ctrl+C` / `Ctrl+V`, `F2`, `Del`, `Ins` | Cut / copy / paste, rename, Recycle Bin, Snap List |
| `Ctrl+I` | AI Assistant |

## Settings

Dark / light theme, accent color, editor extensions (the files that open in the preview, can be edited and are searched), preview font size, Snap List defaults, AI provider / model / API key (with *Test Connection*). Window position (multi-monitor), tabs and favorites are restored at startup. Only one instance runs at a time: launching it again brings the open window to the front.

---

## Tech stack

- **.NET 8** (`net8.0-windows10.0.26100.0`), **WinUI 3** / Windows App SDK `1.6.x`, unpackaged and self-contained
- **MVVM** with `CommunityToolkit.Mvvm`
- Fluent Design with Mica backdrop

## Build & run

```bash
dotnet build -c Debug
```

Then start `Start_DirSnap2.bat` (it runs `bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\DirSnap2.exe`). Close the running app before rebuilding: the single-instance app keeps the exe locked.

- `config.json` (settings, tabs, Snap List) is created next to the exe, with an automatic `config.json.bak`; `config.example.json` is the starting template.
- Logs: `logs\dirsnap2.log` next to the exe, with error details; over 2 MB it is moved to `dirsnap2.old.log` at startup. Unexpected errors also go to `crash.log`.

### Release package

`Publish_DirSnap2.bat` builds the self-contained Release into `publish\DirSnap2\` and zips it as `publish\DirSnap2_vX.Y.Z_win-x64.zip` (version read from `DirSnap2.csproj`), ready to attach to a GitHub Release.

## Project structure

| Folder | Content |
|---|---|
| `Models` | Config (`AppConfig`), list items (`FSItem`), tree nodes, Snap List items, search results |
| `Services` | File system scan, text files (encoding/line endings), config, Snap List, outline parser, search in files, folder totals, Recycle Bin and shell copy/move, AI providers and context, theme, window placement, logging |
| `ViewModels` | `MainViewModel` (tabs, Snap List, address bar), `WorkspaceTabViewModel` (one tab: folder, list, open file) and `AiChatViewModel` (AI chat) |
| `Views` | `WorkspaceTabView` (split in partial files by feature: file actions, find & outline, folder contents, search in files, folder info, line navigation), Settings, About, Snap List window, AI chat panel and window, shortcuts |
