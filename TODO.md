# DirSnap2 - TODO

Ideas for the next versions. Pick, then move to the README roadmap when done.

## Priority
- [ ] **Paste (`Ctrl+V`) into the current folder**: paste files/folders copied in Explorer, mail, desktop. Together with **Cut (`Ctrl+X`)** and **Duplicate**.
- [ ] **+ Folder** button next to `+ TXT` / `+ MD`: creates a new folder and asks for its name right away.
- [ ] **Search in folder files** ("grep"): type a text, get the list of files that contain it with the matching line; clicking a result opens the file with the find already on that match. Very useful for PL/SQL in `.txt` files.

## Useful, smaller
- [ ] **Go to line (`Ctrl+G`)** and **line numbers** in the editor (for "error at line 340").
- [ ] **Outline follows scrolling**: highlight the function you are in while scrolling the file.
- [ ] **Open with** in the right-click menu: default program, or VS Code / Notepad++.
- [ ] **Multiple selection** in the list, to copy or delete several files at once.

## Later, if needed
- [ ] **Compare two files** with the differences highlighted (two versions of a package).
- [ ] **Folder sizes**, computed on request.
- [ ] **More Outline languages**: C#, PowerShell, `.bat`.

## Known gaps
- [ ] Copy / Rename / Delete are only in the main list: not in the folder tree flyout nor in the Folder Contents panel.
- [ ] The Folder Contents panel does not refresh by itself when the selected folder's contents change.
- [ ] Rename / Delete do not update other tabs that have the same file open.
