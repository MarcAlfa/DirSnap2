# DirSnap 2

**DirSnap 2** is a native, modern Windows 11 directory management and snapshot tool built with **C# / .NET 8 / WinUI 3 (Windows App SDK unpackaged)**. It replaces and modernizes the Python/Tkinter DirSnap v1, adopting Fluent Design, Mica material, dark/light themes, and an overhauled workflow designed for maximum efficiency.

---

## 📌 Versioning
- **Current Version**: `0.0.4`
- **Single Source of Truth**: Defined strictly in `AppConstants.cs` (`AppConstants.AppVersion`). All components, UI titles, export headers, and metadata reference this single constant.

---

## 🌐 UI Language
- **Strictly English**: All UI labels, context menus, tooltips, dialogs, error messages, and exports are in English (e.g., *Copy*, *Open in New Tab*, *Add to Snap List*).

---

## 🚀 Key Architectural Changes & Features

### 1. Unified Editor View (Single Main Workspace)
- Eliminates the previous separation between "Lists" and "Editor" views.
- Every workspace tab (up to 10 tabs, full save/restore in `config.json`, drag-and-drop reordering) features a resizable two-panel layout:
  - **Left Panel**: Navigation and file listing (with `Flat` / `Tree` switch).
  - **Right Panel**: Text editor for `.md`, `.txt`, `.json` (and unlockable read-only text files).

### 2. Left Panel: Permanent Flat View & Quick Folder Tree Dropdown
- **Permanent Flat Table View**:
  - The left panel always keeps the clean, fast tabular list view (`Name`, `Size`, `Modified` columns, sortable and filterable).
  - Eliminates the disruptive full-panel switch between Flat and Tree.
- **Quick Folder Tree Dropdown (`📁 CurrentPath  Tree ▾`)**:
  - The current folder bar acts as an interactive button.
  - Clicking it opens the **Folder Tree Flyout**, already expanded to the active folder.
  - Users can browse drives and subfolders with lazy-loading chevrons, click any folder to navigate immediately in the background, or double-click to navigate and close the flyout.

### 3. Global "📋 Snap List" Widget
Replaces the old Lists tab with an intuitive "shopping cart" paradigm:
- **Global & Persistent**: Shared across all open tabs.
- **Adding Items**:
  - Inline quick `[+]` button on each row in list/tree.
  - Context menu (*Add to Snap List*, supports multi-selection).
  - Keyboard shortcut (`Insert`).
  - Batch action buttons in the navigation bar (`+ N folders` / `+ N files`).
- **Header Flyout & Detachable Window (`Detach`)**:
  - Main header widget: `📋 Snap List (N)` with real-time item badge.
  - Clicking opens a modern Flyout:
    - View items, remove individual items (`Del`, double click, or trash icon), or `Clear All`.
    - Live formatting toggles: `Full Path` and `With Extension`.
    - Export actions:
      - `📝 Send to Notepad`
      - `📋 Copy to Clipboard`
      - `💾 Export to .txt`
      - `📊 Export to .csv`
    - `📌 Detach` button: Detaches the Snap List into a standalone floating, always-on-top window for cross-tab multi-monitor workflows, dockable back into the main window at any time.

### 4. Text Editor & File Handling
- Native support for viewing and editing `.md`, `.txt`, `.json`.
- Unlock mode to view/edit arbitrary text files.
- JSON auto-formatting / beautifier.
- Strict preservation of file encoding (UTF-8, UTF-8 BOM, ASCII, etc.) and original newline style (`CRLF` vs `LF`).
- Word wrap toggle and split-pane ratio persistence.

---

## 🛠️ Tech Stack & Setup
- **Framework**: .NET 8 (`net8.0-windows10.0.26100.0`)
- **UI Platform**: WinUI 3 / Windows App SDK (`1.6.x`), unpackaged mode (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`)
- **Styling**: Windows 11 Fluent Design, Mica backdrop, Dark/Light theme switching
- **Architecture**: MVVM with `CommunityToolkit.Mvvm`

---

## 📋 Implementation Roadmap & Checklist

- [x] **Phase 1: Project Setup & Foundation**
  - [x] Solution & `.csproj` configuration (WinUI 3 unpackaged, .NET 8)
  - [x] Single-source versioning (`0.0.1` in `AppConstants.cs`)
  - [x] `app.manifest` (PerMonitorV2 DPI + long paths)
  - [x] `.gitignore` & `config.example.json`
  - [x] Initial build & restore verification
  - [x] `README.md` tracking
- [x] **Phase 2: Core Models & Configuration Service**
  - [x] App settings & workspace tab state persistence (`config.json`, `ConfigService`)
  - [x] File system models (`FSItem`, `DriveInfoModel`, `TreeNodeItem`)
  - [x] File system service (`FileSystemService` for drives, scanning, tree lazy loading, sorting, filtering, explorer, terminal)
  - [x] Text file handler (`TextFileService` with 5MB limit, binary detection, encoding/BOM/line endings preservation, JSON formatter)
  - [x] Snap List global manager & export pipeline (`SnapListService` for cart management, live toggles, clipboard, Notepad, .txt/.csv)
- [x] **Phase 3: Unified Workspace UI & Navigation**
  - [x] TabView supporting up to 10 tabs, close/add, drag-reorder
  - [x] Resizable two-panel splitter layout with persistent width
  - [x] Left panel: `Flat` view (columns, sorting, filtering, quick counters)
  - [x] Left panel: `Tree` view (hierarchical lazy loading, file/folder nodes)
  - [x] Address bar, back/forward/up navigation, drives menu & favorites
- [x] **Phase 4: Global Snap List Widget & Detachable Window**
  - [x] Header badge widget `📋 Snap List (N)`
  - [x] Pop-up flyout with live preview, deletion & formatting toggles
  - [x] Export actions (Clipboard, Notepad, .txt, .csv)
  - [x] Detachable floating window (`📌 Detach`) with always-on-top and live sync
- [x] **Phase 5: Text Editor, Preview & File Classification**
  - [x] Instant read-only preview on single-click selection for configured extensions (`.md`, `.txt`, `.json`)
  - [x] Non-configured file protection with placeholder and "Unlock Preview" gate (text <= 5MB)
  - [x] Syntax-colored Markdown viewer reproducing v1 palette with crisp white text in dark mode
  - [x] Zebra striping: subtle alternating row backgrounds in files/folders list
  - [x] Tree flyout auto-selection: current directory node automatically selected and highlighted
  - [x] Full accent color styling on action buttons (`Go`, `Snap List`, `Unlock to Edit`, `Save`, `Unlock Preview`)
  - [x] "Unlock to Edit" mode for safe file modification and non-locking file inspection
  - [x] Text editor panel with monospace typography, line ending and encoding detection, JSON beautifier
- [x] **Phase 6: Visual Polish, Tree Sync & Settings Dialog**
  - [x] Interchangeable Flat / Tree synchronization (expanding tree hierarchy down to active folder)
  - [x] Real-time Accent Color palette (`blue`, `green`, `purple`, `teal`, `orange`) applied to controls and active pills
  - [x] Differentiated Fluent icons and distinctive color palettes for folders and file types (matching v1)
  - [x] Modern Settings & About Dialog (Theme, Accents, Quick-Preview Extensions chip manager, Snap List defaults, About v0.0.1)
  - [x] Format JSON action & validation
  - [x] Encoding & newline preservation (`CRLF` / `LF`, UTF-8, UTF-8 BOM, ANSI)
  - [x] Read-only unlock for non-standard text files
- [x] **Phase 8: Version 0.0.2 Polish & Animated About Tribute**
  - [x] Version bump to `0.0.2` in `AppConstants.cs`
  - [x] Settings dialog dark/light theme alignment and widened layout (520px width)
  - [x] Dedicated Animated About window with bouncing Alpha Studio logo (`assets/as_logo_trasp.jpg`) paying tribute to v1 and PrinT-fILes
  - [x] Prominent accent color highlighting on active workspace tab header
  - [x] Accent color row selection highlight (`AppAccentSubtleBrush` and `AppAccentBrush` stroke) on selected file/folder
  - [x] Warm yellow foreground (`#FFD166`) on current opened file name in editor header
  - [x] Neutral uncolored toggle styling for Word Wrap button
  - [x] Per-tab selected file memory: switching tabs immediately restores the previously selected file, editor/preview state, and scroll position.
  - [x] Per-tab file persistence across restarts: each tab's active file is saved in `config.json` and restored on launch.
  - [x] V1 compact typography: monospaced font (`Consolas, Cascadia Code, Courier New`) at 11.5pt across Name, Size, and Modified columns.
  - [x] V1 high-density row layout: ultra-compact row height (22px with `ItemContainerStyle` MinHeight 22px) displaying ~25+ items simultaneously on screen.
  - [x] Tree flyout automatic scroll & bold/accent highlight: automatically scrolls and highlights the active folder node in bold with subtle accent background tint.
  - [x] Restricted `Unlock to Edit` visibility: visible exclusively for file extensions enabled in Settings; non-configured files unlock for read-only inspection only.
  - [x] Active tab header accent styling: current tab in `MainTabView` is highlighted with the user's accent color (subtle background tint, accent icon and stroke).
  - [x] Smart `Save` button visibility: clean UI where `Save` button appears only when the file has unsaved edits (`IsEditorDirty`).
  - [x] Markdown syntax highlighting in preview mode replicating v1 theme palette (Headings cyan, bold yellow, code green, lists orange, links blue)
  - [x] Tree view text & icon rendering fix (`Content.Name`, `Content.Glyph`, `Content.GlyphBrush`)
  - [x] Dedicated Current Folder header bar above file list/tree: displays clean current folder name (e.g. `MusicAtlas2  Tree ▾`) with full path tooltip, eliminating duplication with the top global address bar.
  - [x] Top Address Bar synchronization with active tab navigation + `[ Go ]` action button
  - [x] Unified sorting by Modified time (`mtime`) and Size (`size`) mixing folders and files like v1
  - [x] Real-time and AppWindow.Closing tab state persistence (`Path`, `SortCol`, `SortDesc`, `Mode`, `SelectedFile`)
  - [x] Multi-monitor geometry save/restore with monitor index detection (`WindowPlacementService`)
  - [x] Dark / Light theme switching with live runtime updates (`ThemeService`)
  - [x] Accent color palette with Blue default, Green, Purple, Teal, Orange
  - [x] Keyboard shortcuts (`Ins`, `F5`, `Ctrl+S`, `Enter`, `Del`, `Alt+Up/Left/Right`)
  - [x] Windows context menu actions (Open in Explorer, Open in Terminal, Copy Full/WSL Path)
- [x] **Phase 9: Version 0.0.4 - Favorites Star, Tree Navigation & Show-All Dimmed Styling**
  - [x] Version bump to `0.0.4` across `AppConstants.cs`, `DirSnap2.csproj`, and `app.manifest`
  - [x] Dynamic Favorite Star Icon: Filled star (`\uE735`) when the active folder is among favorites, outline star (`\uE734`) when not
  - [x] Immediate state update when navigating via the address bar `[ Go ]` button or pressing Enter
  - [x] Bidirectional updates across tab switching, inline navigation (table, tree, drives, places), and favorite toggle
  - [x] Robust path normalization in `ConfigService` (`NormalizePath`) handling trailing slashes, drive roots (`C:\` vs `C:`), and case insensitivity
  - [x] Dynamic tooltip on favorite button ("Add current folder to Favorites" / "Remove current folder from Favorites")
  - [x] Thread-safe file logging system (`AppLogger`) writing to `logs/dirsnap2.log` and `crash.log` with global unhandled exception tracking
  - [x] Hierarchical Tree auto-expansion down to the active nested folder (`SyncTreeToCurrentPath`) with accent highlighting and automatic viewport scrolling (`StartBringIntoView`)
  - [x] "Show All" Dimmed Items Styling: Files and folders visible only when "Show all" is checked (non-editor extensions and hidden/system items) are automatically styled in soft gray ("grigetto", `#8E95A0` in Dark theme, `#6C727E` in Light theme) for both text and icons, keeping primary editor files bright and prominent.
  - [x] Editor Dirty & Save Button Visibility Fix: Fixed bug where the "Save" button and asterisk `*` prematurely appeared upon selecting or previewing locked/unmodified files due to internal TextBox data binding changes triggering `TextChanged`. Added `OriginalText` diff tracking, `IsLoadingFile` guard, and strict `IsEditorLocked` checks so the Save button strictly only appears when actual user edits diverge from saved content.
  - [x] Pure Folder Tree Navigation & Auto-Close: Filtered out files from the "Folder Tree" flyout (`SyncTreeToCurrentPath` and `HierarchicalTreeView_Expanding`), showing exclusively directories for quick and distraction-free folder jumping. Double-clicking any folder or pressing `Enter` now navigates directly to the target folder and automatically closes the dropdown flyout.
  - [x] Contextual Toolbar Controls Visibility: Word Wrap Toggle, Format JSON, and the Read-Only badge are now strictly displayed only when an active text file is being previewed or edited (`IsPreviewUnlocked`). They automatically hide when non-text files (images, audio .wav, videos, binaries, oversized files) or no files are selected.
