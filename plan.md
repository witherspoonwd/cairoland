# Cairoland: Turn Cairo shell-replacement mode into a tiling window manager

## Context

Cairo Desktop (C#/WPF, `Cairo Desktop/` in this repo) is a Windows shell replacement: a menu bar, a taskbar, a desktop, and a tray. We want a lean shell that is also an **awesomewm-style tiling window manager**:

- **Keys:** HJKL for directions (Win+L is taken over from Windows; locking moves to Win+P).
- **Tags:** 10 per monitor. Win+N views tag N (Win+0 is tag 10). Win+Shift+N moves the focused window to tag N.
- **Tiling:** windows on a tag are tiled as a **binary space partition (BSP) tree** and can be rearranged and resized from the keyboard.
- **Top bar layout:** `[Cairo menu][tag bar][window list for the current tag, focused window highlighted][tray/volume/clock/search]`.
- **Focus follows mouse.**
- **Removed:** the desktop (icons, overlay, dynamic desktop), Stacks, the Places menu, the Programs menu, the Taskbar project, and Cairo Explorer.
- **Program launcher:** a new keyboard-driven fuzzy launcher that searches **installed programs only** (Start Menu and Store apps).
- **Kept:** the Cairo (top-left) menu, which also gets the new window-manager settings, and the Search menu extra.

Decisions already made with the user:

| Topic | Decision |
|---|---|
| Keybindings | Awesome-style: Win+N views, Win+Shift+N moves |
| Hiding windows on other tags | `ShowWindow(SW_HIDE)`, plus a journal that restores every window on exit, crash, or startup |
| Programs menu, Taskbar | Removed. Search extra stays |
| When tiling runs | Automatically when Cairo is the shell. Setting `EnableTilingOutsideShell` forces it on under Explorer for development |

## Architecture overview

```
Cairo Desktop.sln
├─ CairoDesktop.Tiling.Core   NEW  netstandard2.0;net10.0, no WPF/Win32. BSP tree, tags, geometry, fuzzy matcher
├─ CairoDesktop.Tiling        NEW  net480;net6.0-windows;net10.0-windows (WPF). Win32 window tracking, arranging,
│                                  hotkeys, focus-follows-mouse, per-screen view models
├─ CairoDesktop.Interop       EXTEND  User32.cs, Dwmapi.cs P/Invokes
├─ CairoDesktop.MenuBar       MODIFY  TagBar + WindowList controls, launcher window, Programs/Places/Stacks removed
├─ Tests/CairoDesktop.Tiling.Core.Tests   NEW  xunit, net10.0. Runs on Linux with `dotnet test`
└─ removed: CairoDesktop.DynamicDesktop, CairoDesktop.Taskbar, Cairo Explorer
```

`Tiling.Core` is kept free of Windows dependencies on purpose. The dev machine is Linux, so the layout, tag, and fuzzy logic can be unit-tested there. Everything else needs Windows to build and run; with `EnableWindowsTargeting=true` it may still compile on Linux.

**Naming:** `CairoDesktop.Infrastructure/Services/WindowManager.cs` / `IWindowManager` already exist and handle *screens and appbar windows*. Do not reuse that name. The new subsystem is called **Tiling** (`ITilingManager`, `TilingManager`).

---

## Phase 1: Removal and cleanup

Goal: the app builds and runs as a shell showing only the menu bar (Cairo menu, extras) and wallpaper. Commit after each sub-step.

### 1a. Keep the shell-window behaviour that DesktopManager provides, then delete DynamicDesktop

`CairoDesktop.DynamicDesktop/Services/DesktopManager.cs` does two things we still need in shell mode:

1. **Shell window registration.** `CreateShellWindow()` / `DestroyShellWindow()` (lines ~320–346) create `ManagedShell.Common.SupportingClasses.ShellWindow` (SetShellWindow, plus `WallpaperChanged`/`WorkAreaChanged` events).
2. **Wallpaper.** The `Desktop` window paints it (`Desktop.xaml.cs setBackground()`).

Steps:

- Create `CairoDesktop.Infrastructure/Services/ShellWindowService.cs` (or put it in the main app under `Cairo Desktop/Services/`).
  - `Initialize()`: if `EnvironmentHelper.IsAppRunningAsShell`, create the `ShellWindow` and subscribe to its events. Copy the logic from DesktopManager.
  - Owns a new `WallpaperWindow`: one borderless WPF window per screen, bottom-most, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, and a child of the ShellWindow using the same SetParent pattern as `DesktopManager.ConfigureDesktop`.
  - The window shows the system wallpaper, read from `HKCU\Control Panel\Desktop\Wallpaper`, with WallpaperStyle mapped to WPF `Stretch`. It falls back to a solid colour.
  - Reloads on `ShellWindow.WallpaperChanged` and `IWindowManager.ScreensChanged`.
  - Only exists in shell mode.
- Replace the call in `Cairo Desktop/CairoApplicationInitializationService.InitializationRoutines.cs:100` (`IDesktopManager.Initialize()`) with `ShellWindowService.Initialize()`. Dispose it on exit.
- Delete the `CairoDesktop.DynamicDesktop/` project:
  - its entry in `Cairo Desktop.sln` (project line ~39, configs ~183–194, nesting ~212)
  - the ProjectReference in `Cairo Desktop/Cairo Desktop.csproj:88`
  - in `Program.cs`: lines 17–18 (usings), 75 (`IDesktopManager`), 112 (`ToggleDesktopOverlayCommand`)
- Delete `CairoDesktop.Infrastructure/ObjectModel/IDesktopManager.cs` and fix its consumers:
  - `Cairo Desktop/Commands/OpenLocationCommand.cs`: always open in a window. Simplest is to make it delegate to the `OpenLocationInWindow` behaviour.
  - `OpenLocationInWindowCommand.cs`: drop the `IsOverlayOpen = false`.
  - `OpenControlPanelCommand.cs:26`: remove the `FoldersOpenDesktopOverlay` usage.
- `Cairo Desktop/Services/ShellHotKeyService.cs`:
  - Remove Win+D and Win+Comma.
  - Win+E invokes `OpenLocationInWindow`.
  - Win+R is reassigned in Phase 6.
- `CairoDesktop.Common/Settings.cs`: delete `#region Desktop` (lines ~133–224) and `FoldersOpenDesktopOverlay`.
- Delete Common classes used only by the desktop or Stacks: `NavigationManager.cs`, `NavigationItem.cs`, `FolderView.cs`, `Icon.xaml(.cs)`, `DragDropHandler.cs`, `CairoWallpaperStyle.cs`. Check each with grep before deleting.
- `Cairo Desktop/SettingsUI.xaml(.cs)`: remove
  - the Desktop tab (XAML ~513–694)
  - "Files and folders" / `chkFoldersOpenDesktopOverlay` (~314–319)
  - in code-behind: `loadDesktopBackgroundSettings`, `#region Desktop Background`, the DesktopLabelPosition/IconSize radios, the DesktopOverlayHotKey combo, `btnDesktopHomeSelect_Click`, the SETDESKWALLPAPER WndProc reload
  - the `"desktop"` case in `Services/SettingsUIService.cs`. Have `OpenPersonalizeControlPanelCommand` open the Windows personalization control panel instead.
- `Cairo Desktop/Welcome.xaml(.cs)`: remove the desktop page (`bdrDesktop`, ~68–87) and the Places/Stacks sections (~52–57), and re-link the page flow (`Welcome.xaml.cs` ~94/99).
- Themes (`Cairo Desktop/Themes/*.xaml`): remove Desktop*/DesktopNavToolbar*/DesktopOverlay*/`CairoDesktopButtonStyle`/`DesktopFolderViewStyle`/`DesktopNavToolbar`/`CairoDesktopNavItemStyle`. Remove the matching resources from `Cairo Desktop.csproj` (`desktopOverlayIcon.png`, `controls*.png`, `Welcome\desktop.jpg`).
- Localization (`CairoDesktop.Common/Localization/DisplayString.cs` + `Language.cs`): remove `sDesktop_*`, `sSettings_Desktop*`, `sWelcome_Desktop*`, `sWelcome_DynamicDesktop`, `sCommand_Open/CloseDesktopOverlay`, `sTaskbar_DesktopOverlayToolTip`.
  - **Keep** `sDesktop_DeleteTitle` (DeleteFileCommand), `sDesktop_DisplaySettings` and `sDesktop_Personalize` (control panel commands), `sStacks_OpenInNewWindow` (OpenLocationInWindow label), and `sSettings_General_FilesAndFolders` (Advanced tab).
  - Grep before deleting each key.

### 1b. Remove Stacks and the Places menu

- Delete `CairoDesktop.MenuBar/StacksContainer.xaml(.cs)`, `StacksScroller.xaml(.cs)`, `CairoDesktop.Common/StacksManager.cs`, and `Cairo Desktop/Commands/AddStackCommand.cs` / `RemoveStackCommand.cs` (registered in `Program.cs` ~115–116).
- `CairoDesktop.MenuBar/MenuBar.xaml`: remove `PlacesMenu` and `<control:StacksContainer>`. Phase 5 puts the window list in the fill slot.
- `CairoDesktop.MenuBar/MenuBar.xaml.cs`: remove `isPlacesMenuInitialized`, `setupPlacesMenu`, `PlacesMenu_Opened`, `stacksContainer.MenuBar = this`, the `EnablePlacesMenu` case, and the `_host` field if nothing else uses it.
- Remove `Places` from `CairoDesktop.Application/Interfaces/ICairoApplication.cs` and `Cairo Desktop/CairoApplication.xaml.cs`.
- Settings to remove: `EnablePlacesMenu`, `ShowStacksRemovableDrives`, and their SettingsUI checkboxes (~373–376, ~438–441).
- Remove Stacks theme styles (`StacksIconText`, `CairoStacksStyle`, `StackScrollButton`, `StackTopButton`, `StackScrollViewer`, `sStacks_Empty` template) and the `sStacks_*` (except `OpenInNewWindow`), `sPlacesMenu`, and `sInterface_Add/RemoveFromStacks` strings.

### 1c. Remove the Programs menu

- Delete `CairoDesktop.MenuBar/ProgramsMenu.xaml(.cs)`, `Converters/ProgramsMenuHeightConverter.cs`, and `Commands/ToggleProgramsMenuCommand.cs` (and its registration).
- `MenuBar.xaml(.cs)`: remove the `ProgramsMenu` item and the `#region Programs menu`.
- `CairoDesktop.MenuBar/Services/MenuBarHotKeyService.cs`: remove `programsMenuHotKeys`, `registerWinKey`, and `unregisterWinKey`. Keep the Cairo-menu hotkey.
- Settings to remove: `EnableProgramsMenu`, `ProgramsMenuLayout`, `DefaultProgramsCategory`, `EnableWinKey`, plus their SettingsUI rows.
- Remove `OpenAppGrabber` from the Cairo menu list in `MenuBar.xaml.cs CairoMenu_Opened`. Keep the `CairoDesktop.AppGrabber` project: `AppGrabberService.GetApps()` is the launcher's data source in Phase 6.

### 1d. Remove the Taskbar

- Delete `CairoDesktop.Taskbar/`: its sln entries, ProjectReference, and `services.AddSingleton<IWindowService, TaskbarWindowService>()` in `Program.cs`.
- Delete `CairoDesktop.AppGrabber/AppGrabberTaskCategoryProvider.cs`. Its only user was the Taskbar.
- Settings to remove: all `Taskbar*` and `EnableTaskbar*` settings, `ShowTaskbar*`, `FullWidthTaskBar`, and the Taskbar tab in SettingsUI (~696–862).
- Remove `CairoTaskbar*` theme styles and `sTaskbar_*`/`sSettings_Taskbar*` strings, grepping first.
- `TaskbarWindowService` used to call `ShellManager.Tasks.Initialize(...)`. The tiling engine does **not** depend on ManagedShell's TasksService (Phase 3 explains why), so nothing replaces that call.
- `TaskViewCommand` was referenced only by the taskbar menu. Delete it.

### 1e. Delete Cairo Explorer

- Delete `Cairo Desktop/Cairo Explorer/` and its sln lines (~15, ~87–92, ~203). Nothing references it.

**Phase 1 check:** `dotnet build "Cairo Desktop/Cairo Desktop.sln"` succeeds for net10.0-windows. A grep for `DynamicDesktop|StacksManager|IDesktopManager|Taskbar|ProgramsMenu|PlacesMenu` returns nothing outside git history.

---

## Phase 2: Interop additions (`CairoDesktop.Interop`)

ManagedShell is a NuGet package (0.0.358), and its source isn't available locally. Add our own P/Invokes rather than relying on whatever its `NativeMethods` happens to include.

- **`CairoDesktop.Interop/User32.cs`:**
  - `EnumWindows`, `GetWindowRect`, `SetWindowPos`, `BeginDeferWindowPos` / `DeferWindowPos` / `EndDeferWindowPos`
  - `ShowWindow` / `ShowWindowAsync`, `IsWindow`, `IsWindowVisible`, `IsIconic`, `IsZoomed`
  - `GetWindowLongPtr` (32/64 shim), `GetWindow` (GW_OWNER), `GetAncestor`, `GetWindowThreadProcessId`
  - `GetClassName`, `GetWindowText(Length)`
  - `GetForegroundWindow`, `SetForegroundWindow`, `AllowSetForegroundWindow`
  - `MonitorFromWindow`, `MonitorFromPoint`, `GetMonitorInfo` (rcWork), `GetCursorPos`, `WindowFromPoint`
  - `PostMessage` (WM_CLOSE), `SendMessageTimeout` (WM_GETICON), `GetClassLongPtr` (GCLP_HICON/HICONSM)
  - `SetWinEventHook` / `UnhookWinEvent` and the `WinEventDelegate`
  - `SystemParametersInfo` overloads for `SPI_GET/SETACTIVEWINDOWTRACKING`, `SPI_GET/SETACTIVEWNDTRKZORDER`, `SPI_GET/SETACTIVEWNDTRKTIMEOUT`
  - Constants: `EVENT_OBJECT_SHOW/HIDE/DESTROY/NAMECHANGE/LOCATIONCHANGE/CLOAKED/UNCLOAKED`, `EVENT_SYSTEM_FOREGROUND/MOVESIZESTART/MOVESIZEEND/MINIMIZESTART/MINIMIZEEND`, `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`, `OBJID_WINDOW`
  - `keybd_event` or `SendInput`, for the Alt-tap trick that lets `SetForegroundWindow` succeed
  - `LockWorkStation`, used by the Win+P lock (Phase 4, 10a)
- **`CairoDesktop.Interop/Dwmapi.cs`:** `DwmGetWindowAttribute` with `DWMWA_EXTENDED_FRAME_BOUNDS` (9) and `DWMWA_CLOAKED` (14).
- Multi-targeting: the projects target `net480`, so use classic `[DllImport]`, not `LibraryImport`, and C# 7.3-compatible syntax unless the project sets a higher `LangVersion`. Check each csproj.

---

## Phase 3: `CairoDesktop.Tiling.Core` (pure logic) and tests

New SDK-style project targeting `netstandard2.0;net10.0` with `LangVersion` latest. It has **no** references to WPF, ManagedShell, or Win32. Window identity is an opaque `long Id`; the runtime uses the hwnd value.

### Geometry

- `RectI` (X, Y, W, H) with `Inset(int)`, `SplitH(ratio, gap)`, `SplitV(ratio, gap)`, `Center`, and `Contains`.

### BSP tree: `BspTree`

- Nodes: `BspNode` is abstract, with `Parent`. `BspLeaf` holds a `WindowId`. `BspSplit` holds `Orientation` (Horizontal = side-by-side, Vertical = stacked), `Ratio` (0.1–0.9, default 0.5), `First`, and `Second`.
- `Insert(windowId, targetLeafId?, RectI area)`:
  - No root: the window becomes the root leaf.
  - Otherwise: split the target leaf, or the most recently focused leaf if none is given. Fall back to the deepest right-most leaf (dwindle-like).
  - The new split's orientation follows the target leaf's current rect: width ≥ height gives Horizontal, else Vertical.
  - The new window goes in `Second` (right or bottom).
- `Remove(windowId)`: the sibling replaces the parent split. Removing the root leaf empties the tree.
- `Layout(RectI area, int gap) → Dictionary<long, RectI>`: recursive. Apply an outer gap, then split each node with an inner gap.
- `FindNeighbor(windowId, Direction, layout)`: use the computed rects. Candidates lie strictly in the given direction and overlap the source on the perpendicular axis. Pick the smallest edge distance, then the largest overlap. Direction is Left/Right/Up/Down.
- `Swap(a, b)`: swap the window ids in two leaves.
- `Move(windowId, Direction)`: if a neighbour exists, remove the window and re-insert it splitting the neighbour on the side facing the direction. This is "move into", distinct from swap.
- `Resize(windowId, Direction, delta)`: find the nearest ancestor split whose orientation matches the axis and where the window is on the side that edge belongs to. Adjust `Ratio` by ±delta and clamp.
- `ToggleOrientation(windowId)`: flip the orientation of the leaf's parent split.
- `Equalize()`: set every ratio to 0.5.
- `SetRatioFromRect(windowId, RectI newRect, layout)`: after a mouse resize, compute the new ratio for the relevant ancestor or ancestors.
- `Leaves()` in in-order traversal, used for next/prev focus cycling.

### Tags: `TagSet` and `Tag`

- `TagSet` is created per monitor with 10 `Tag`s, indices 0–9, displayed as "1"–"9" and "0" (labels configurable later).
- `Tag`:
  - `BspTree Tree`
  - `List<long> Floating`
  - `List<long> Minimized`, kept out of the tree until restored
  - `long? FocusedWindow`, the last-focused window, restored on view
  - `long? FullscreenWindow`
- `TagSet`:
  - `int SelectedIndex`, plus `PreviousIndex` for Win+Escape to go back
  - `Dictionary<long, int> windowToTag`
  - `AddWindow(id, tagIndex, floating)`, `RemoveWindow(id)`, `MoveWindow(id, toTag)` (append to the target tree next to its last-focused leaf)
  - `View(index)` returns a `TagViewDelta { ToHide, ToShow }`
  - `IsOccupied(index)`
- Single-select view only for v1. Leave a TODO for toggle-view (Win+Ctrl+N), which awesome supports.

### Fuzzy matcher: `FuzzyMatcher` (used by the launcher in Phase 6)

- `int? Score(string query, string candidate)`: fzf-style, case-insensitive subsequence match. Returns `null` when the query is not a subsequence.
- Bonuses: match at index 0, match at a word boundary (after space, `-`, `_`, `.`, or a camelCase hump), and consecutive-character runs. Penalties: gaps between matches and leading unmatched characters.
- `IReadOnlyList<int> MatchPositions(query, candidate)`, for bold highlighting in the UI.
- `Rank<T>(query, items, Func<T, string> primary, Func<T, string?> secondary, Func<T, int> frecency)`:
  - Score is `max(primaryScore, secondaryScore - 10)`.
  - Sort by score descending, then frecency descending, then name length ascending.
  - An empty query sorts by frecency, then alphabetically.

### Tests: `Cairo Desktop/Tests/CairoDesktop.Tiling.Core.Tests`

New SDK xunit project, `net10.0`. Do not reuse the legacy `Cairo Desktop.Tests` (net4.8, old-style csproj). Cover:

- Insert/remove: tree shape, and the sibling promoted on removal.
- Layout rects: gaps, no overlaps, union equals the area.
- Orientation picked from the aspect ratio.
- Neighbour finding in a 2×2 grid and in asymmetric layouts.
- Swap, move, and resize, including ratio clamping.
- TagSet view deltas and moving a window to another tag.
- FuzzyMatcher ordering: `"vsc"` → "Visual Studio Code" ranks above "Microsoft Visual C++ Redist", `"ff"` → "Firefox", a non-subsequence query gives null, and match positions are correct.

Add both projects to the sln. Run with `dotnet test "Cairo Desktop/Tests/CairoDesktop.Tiling.Core.Tests"`. This works on Linux.

---

## Phase 4: `CairoDesktop.Tiling` (Win32 runtime)

New WPF class library with the same TFMs as the other projects (`net480;net6.0-windows;net10.0-windows`). It references `Tiling.Core`, `Interop`, `Common`, `Infrastructure`, `Application`, and the ManagedShell package.

**Why not ManagedShell's `TasksService`?** It decides what counts as a window from taskbar visibility (`ShowInTaskbar`, which considers `IsWindowVisible`). Our own `SW_HIDE` calls would make windows "disappear" from it. We own the registry instead, driven by `SetWinEventHook`.

### Components (folder `CairoDesktop.Tiling/`)

**1. `ManagedWindow`**
- Fields: `IntPtr Hwnd`, `uint Pid`, `string ClassName`, `string Title` (INotifyPropertyChanged), `ImageSource Icon` (lazy), `bool IsFloating`, `bool IsHiddenByUs`, `IntPtr HMonitor`, and `RectI FloatingRect` (remembered so floating windows return to their spot).

**2. `WindowFilter.ShouldManage(hwnd) → Manage | Float | Ignore`**
- Ignore when any of these hold:
  - not `IsWindowVisible`
  - the window is cloaked (`DWMWA_CLOAKED != 0`)
  - it has `WS_CHILD`
  - it has `WS_EX_TOOLWINDOW` without `WS_EX_APPWINDOW`
  - it has `WS_EX_NOACTIVATE`
  - it belongs to Cairo's own process
  - its class is `Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, `Windows.UI.Core.CoreWindow`, or `ApplicationFrameInputSinkWindow`
  - its title is empty and it has no `WS_CAPTION`
  - its rect is zero-sized
- Float when any of these hold:
  - it has an owner (`GetWindow(GW_OWNER) != 0`), i.e. dialogs
  - its class is `#32770`
  - it lacks `WS_THICKFRAME` (fixed size)
  - it has `WS_POPUP` without `WS_CAPTION`
  - its class or exe is in `Settings.TilingFloatClasses` / `TilingFloatExes`
- Otherwise: Manage.

**3. `WinEventListener`**
- Installs `SetWinEventHook` ranges on the WPF dispatcher thread with `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`. Keep the delegate in a field so it is not garbage-collected.
- Filters to `idObject == OBJID_WINDOW && idChild == 0`, and to top-level windows (`GetAncestor(GA_ROOT) == hwnd`).
- Raises typed events: `Shown`, `Hidden`, `Destroyed`, `Foreground`, `MoveSizeStart`, `MoveSizeEnd`, `MinimizeStart`, `MinimizeEnd`, `NameChanged`, `LocationChanged` (only used to detect maximize/fullscreen), `Cloaked`, and `Uncloaked`.

**4. `HiddenWindowJournal`**
- Stores the hwnds we hid in `%LOCALAPPDATA%\Cairo Desktop\tiling-hidden.json`. It is rewritten on every hide/show, with a small debounce.
- `RestoreAll()` calls `ShowWindow(hwnd, SW_SHOWNA)` for every journaled hwnd that `IsWindow` still reports as valid, then clears the journal.
- `RestoreAll()` is called:
  - on TilingManager start, which recovers from a previous crash
  - on `Stop()`
  - from `CairoApplication.OnExit`
  - from `AppDomain.CurrentDomain.UnhandledException` and `Dispatcher.UnhandledException` (hook these in `CairoApplication.xaml.cs`)
  - when tiling is toggled off
- The journal also records the original `DisableLockWorkstation` value (see 10a), so every one of these restore paths puts the lock policy back too.

**5. `WindowArranger`**
- `Apply(IEnumerable<(IntPtr hwnd, RectI rect)>)`:
  - For each window: if `IsZoomed`, `ShowWindow(SW_RESTORE)` first.
  - Compensate for invisible DWM borders: `delta = GetWindowRect - DWMWA_EXTENDED_FRAME_BOUNDS`, so the target outer rect is the visible rect expanded by that delta.
  - Batch all windows in one `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos` with `SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_FRAMECHANGED`.
  - If the batch fails (`EndDeferWindowPos` returns false), fall back to per-window `SetWindowPos`.
  - A per-window `ERROR_ACCESS_DENIED` means an elevated window under UIPI. Mark it `IsFloating` and log it.
- `Hide(hwnd)` calls `ShowWindow(SW_HIDE)` and `Show(hwnd)` calls `ShowWindow(SW_SHOWNA)`. Both update the journal and add the hwnd to `_expectedVisibilityChanges`, so the resulting EVENT_OBJECT_HIDE/SHOW is ignored rather than treated as the window closing or opening.
- `Focus(hwnd)`:
  - Call `SetForegroundWindow`.
  - If that fails, send a synthetic Alt key tap (SendInput) and retry. This is the standard foreground-lock workaround.
  - Raise the window with `SetWindowPos(HWND_TOP, NOMOVE|NOSIZE|NOACTIVATE)`.

**6. `MonitorState`** (one per screen)
- Holds the `AppBarScreen` (from `IWindowManager.ScreenState`), `TagSet`, and the work area (`GetMonitorInfo(hmon).rcWork`, which already excludes the menu-bar appbar).
- Exposes a `ScreenTagsViewModel` for the UI (component 9).

**7. `TilingManager : ITilingManager`** (DI singleton, plus a `CairoBackgroundService` wrapper that starts and stops it)
- **Enable condition:** `Settings.EnableTiling && (EnvironmentHelper.IsAppRunningAsShell || Settings.EnableTilingOutsideShell)`. Re-evaluate when either setting changes.
- **Start:**
  - `journal.RestoreAll()`
  - build a `MonitorState` per screen
  - `EnumWindows` and classify each window. Already-minimized windows go to the selected tag's `Minimized` list.
  - assign every window to the selected tag of its monitor (`MonitorFromWindow`)
  - start the WinEventListener, register hotkeys, enable focus-follows-mouse, relayout everything
- **Stop:** unregister hotkeys, unhook, `journal.RestoreAll()`, restore the original SPI focus settings, and clear state.
- **Event handling:**

| Event | Handling |
|---|---|
| `Shown` | Ignore if expected or already managed. Otherwise classify and add to the **selected tag of the monitor under the window** (or under the cursor if the window is offscreen). Insert next to the tag's focused leaf, focus it, relayout. |
| `Destroyed`, or `Hidden` when not expected | Remove the window from its tag. If it was focused, focus a neighbour or the next leaf. Relayout. |
| `Cloaked` | Treat as hidden (UWP suspend). `Uncloaked` is treated as shown. |
| `MinimizeStart` | Move to `Minimized`, remove from the tree, relayout. |
| `MinimizeEnd` | Re-insert and relayout. |
| `Foreground` | If the window is managed, set `MonitorState.Tag.FocusedWindow` and update `ScreenTagsViewModel.FocusedWindow`. If the window lives on a hidden tag (an app activated itself), switch that monitor to its tag, as awesome does with "urgent jump". Make that switch a setting (`TilingFollowActivation`, default true). Otherwise mark the tag urgent. |
| `MoveSizeEnd` on a tiled window | If the size changed, `Tree.SetRatioFromRect`. If only the position changed, find the leaf under the cursor (`GetCursorPos` against the layout rects), `Swap`, then relayout. If the cursor is on another monitor, move the window to that monitor's selected tag. |
| `LocationChanged` on a tiled window | If it has become zoomed (app maximize), `SW_RESTORE` and relayout, unless it is the tag's `FullscreenWindow`. Throttle this event because it is very chatty. |
| `NameChanged` | Update `ManagedWindow.Title`. |

- **Relayout:** coalesce requests with `Dispatcher.BeginInvoke(DispatcherPriority.Background)` and a dirty flag per monitor. Layout = `Tag.Tree.Layout(workArea, Settings.TilingGap)`. A fullscreen window gets the full monitor bounds with other windows untouched underneath. Floating windows are not moved.
- **Screens:** subscribe to `IWindowManager.ScreensChanged`.
  - Added monitor: new `MonitorState`.
  - Removed monitor: move each of its tags' windows into the same tag index on the primary monitor.
  - Work-area or DPI change: relayout all.
- **Public API**, called by hotkeys, commands, and UI:
  - `ViewTag(screen, i)`, `ViewPreviousTag(screen)`, `MoveFocusedToTag(i)`, `MoveFocusedToScreen(next)`
  - `FocusDirection(dir)`, `FocusNext()`, `FocusPrev()`, `FocusScreen(next)`
  - `SwapDirection(dir)`, `MoveDirection(dir)`, `Resize(dir)`, `ToggleOrientation()`, `Equalize()`
  - `ToggleFloating()`, `ToggleFullscreen()`, `CloseFocused()` (`PostMessage WM_CLOSE`)
  - `FocusWindow(hwnd)`, `GetScreenModel(AppBarScreen)`
- "The focused screen" means the monitor of the foreground window, falling back to the cursor's monitor.

**8. `FocusFollowsMouse`**
- On enable: save the current values of `SPI_GETACTIVEWINDOWTRACKING`, `SPI_GETACTIVEWNDTRKZORDER`, and `SPI_GETACTIVEWNDTRKTIMEOUT`.
- Set tracking on, z-order raise per `Settings.FocusFollowsMouseRaise` (default false, as in awesome), and timeout per `Settings.FocusFollowsMouseDelayMs` (default 100).
- Always call with `fWinIni = 0` (do not persist to the user profile), and restore the saved values on disable or exit.
- This is Windows' built-in "X-mouse". It activates the window under the cursor, and the resulting EVENT_SYSTEM_FOREGROUND updates our focus state.
- Fallback, only if testing shows problems: a 50 ms `DispatcherTimer` that polls `GetCursorPos` → `WindowFromPoint` → `GetAncestor(GA_ROOT)`. If that window is managed and not the foreground, and no mouse button is down (`GetAsyncKeyState`), call `Focus` without raising. Put the fallback behind the setting `FocusFollowsMouseMode = Native | Polling`.
- Do not steal focus while a Cairo menu or popup is open. Check whether the foreground window belongs to our process.

**9. `ScreenTagsViewModel`** (INotifyPropertyChanged, one per screen, all access on the UI thread)
- `ObservableCollection<TagViewModel> Tags`, 10 items. Each `TagViewModel` has `Index`, `Label`, `IsSelected`, `IsOccupied`, `IsUrgent`, and `ICommand ViewCommand` / `MoveHereCommand`.
- `ObservableCollection<ManagedWindowViewModel> Windows`: the windows on the selected tag, tiled in tree in-order, then floating. Each item has `Title`, `Icon`, `IsFocused`, `IsFloating`, `FocusCommand`, and `CloseCommand`.
- `ManagedWindowViewModel FocusedWindow`.
- **Icons:** `WindowIconHelper.GetIcon(hwnd)`:
  - `SendMessageTimeout(WM_GETICON, ICON_SMALL2/ICON_SMALL/ICON_BIG, 100 ms, SMTO_ABORTIFHUNG)`
  - falls back to `GetClassLongPtr(GCLP_HICONSM/HICON)`
  - converts with `Imaging.CreateBitmapSourceFromHIcon` and freezes the result
  - for UWP `ApplicationFrameWindow`, falls back to a generic icon in v1
  - fetched lazily and cached per hwnd

**10. `TilingHotKeyService : CairoBackgroundService`**
- Registers through the existing `CairoDesktop.Common/HotKey.cs` (RegisterHotKey on the UI thread via `_cairoApplication.Dispatch`, the same pattern as `MenuBarHotKeyService`).
- Each binding invokes an `ICairoCommand` through `ICommandService`, so the Cairo menu can reuse the commands.
- Directional bindings use **HJKL**, with the arrow keys as aliases. That requires taking Win+L away from Windows; see **10a**.
- Under Explorer (dev mode), Explorer owns Win+1..0, Win+Arrows, Win+H/K/P and others, so `RegisterHotKey` fails. Log the failures. Add `Settings.TilingModifier` (`"Win"` default, `"Alt+Win"` option) so dev mode can use a different modifier. Optionally, Explorer's own Win shortcuts can be turned off with `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` → `DisabledHotkeys` (REG_SZ of letters, e.g. `"HJKLP"`), which takes effect after Explorer restarts. Document this in the Tiling settings tab; don't write it automatically.

**10a. Win+L → HJKL, Win+P → lock**

Win+L can't be caught with `RegisterHotKey` or blocked by a low-level keyboard hook while locking is enabled; Windows handles it below user mode. The known workaround (the same one AutoHotkey uses) is the per-user policy value **`HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System` → `DisableLockWorkstation` (DWORD)**:

- With `DisableLockWorkstation = 1`, Win+L stops locking and becomes an ordinary key combo we can bind.
- The catch: that same policy also blocks `LockWorkStation()` and hides "Lock" on the Ctrl+Alt+Del screen. So to lock, we briefly switch the policy off, lock, then switch it back on.

Implementation, in a new `CairoDesktop.Tiling/WorkstationLockManager.cs`:
- `Settings.RemapWinL` (default **true**). It only applies when Cairo runs as the shell, or when `EnableTilingOutsideShell` is on.
- **Enable**, at TilingManager start:
  - Read and remember the existing value. It may be missing, or an admin may have set it to 1.
  - Write `DisableLockWorkstation = 1`.
  - Save the original value in the crash journal (component 4), using the same file or a sibling `tiling-state.json`, so it can be put back later.
- **Lock** (`Lock()`):
  1. Write `DisableLockWorkstation = 0`.
  2. Call `LockWorkStation()` (user32 P/Invoke; add it to `User32.cs`).
  3. After ~1 s (a `DispatcherTimer`, so the lock has taken effect), write `1` again.
  - If writing fails, still call `LockWorkStation()`. Never leave the user unable to lock.
- **Disable**, on Stop, exit, crash handlers, and `RestoreAll()` at startup: write back the original value, or delete the value if it didn't exist before. This is critical. If Cairo dies and the value stays at 1, the user can't lock with Win+L even under normal Explorer until Cairo restarts or they fix it. The startup restore covers that.
- **If an administrator already set the policy to 1:** don't fight it. Treat the remap as already active, and in `Lock()` skip writing 0 (LockWorkStation will just fail, matching what the admin wanted). Log it.
- **Permissions:** `HKCU\...\Policies\System` is normally writable by the user, but some locked-down machines deny it. On `UnauthorizedAccessException`:
  - log it
  - leave Win+L as the Windows lock key
  - fall back to arrow keys only for the Win+L binding; H/J/K still work
  - show a one-line note in the Tiling settings tab
- **Binding Win+L after disabling:** register it with `RegisterHotKey` first. If that fails (Windows may still keep the combo reserved even with the policy set), fall back to the existing `CairoDesktop.Common/LowLevelKeyboardListener.cs` for this one combo. Its "stuck key" problem (see the comment in `MenuBarHotKeyService`) has to be fixed first, by tracking key-ups for the Win key and swallowing only the L down/up. **Test both paths in the VM before building on either.**
- **Wire up the existing lock command:** change `Cairo Desktop/Commands/LockCommand.cs` (it currently calls `ShellHelper.Lock()`) to call `WorkstationLockManager.Lock()` when the remap is active. The Cairo menu's "Lock" item and Win+P then both work.
- **Win+P:**
  - Under Explorer it opens the "Project" display switcher. In shell mode Explorer isn't running, so `RegisterHotKey(Win+P)` should succeed; confirm this in testing.
  - Bind Win+P → `Lock` only in shell mode, or when `RemapWinL` is active.
  - The display-projection switcher can still be opened from Cairo menu → Window Manager ▸ "Display projection…" (runs `DisplaySwitch.exe`).
- **Ctrl+Alt+Del:** it is not affected and can't be (it's the secure attention sequence). Only its "Lock" option is hidden while the policy is 1, which is acceptable because Win+P covers it.

**Other Explorer Win shortcuts in shell mode:** Win+H (dictation), Win+K (Cast), Win+J, Win+O, Win+T, and Win+P are Explorer or shell-experience hotkeys. With Cairo as the shell they should be free for `RegisterHotKey`. Log every registration failure at Warning level with the key combo, so conflicts are obvious.

Default keymap:

| Keys | Action | Command id |
|---|---|---|
| Win+1..9, Win+0 | View tag 1..10 on the focused screen | `TilingViewTag` (param `Index`) |
| Win+Shift+1..9, 0 | Move the focused window to tag N; focus stays on the current tag | `TilingMoveToTag` |
| Win+Escape | View the previous tag | `TilingViewPreviousTag` |
| Win+H/J/K/L (and Win+←↓↑→) | Focus in direction (left/down/up/right) | `TilingFocusDirection` |
| Win+Shift+H/J/K/L (and arrows) | Swap with neighbour in direction | `TilingSwapDirection` |
| Win+Ctrl+Shift+H/J/K/L (and arrows) | Move into neighbour's split | `TilingMoveDirection` |
| Win+Ctrl+H/J/K/L (and arrows) | Resize by moving the split edge (5% step) | `TilingResize` |
| Win+Tab / Win+Shift+Tab | Focus next / previous window (in-order) | `TilingFocusNext` / `TilingFocusPrev` |
| Win+P | Lock the workstation (shell mode or `RemapWinL`; see 10a) | `Lock` |
| Win+T | Toggle the orientation of the focused window's parent split | `TilingToggleOrientation` |
| Win+Shift+E | Equalize splits on the tag | `TilingEqualize` |
| Win+Ctrl+Space | Toggle floating | `TilingToggleFloating` |
| Win+F | Toggle fullscreen | `TilingToggleFullscreen` |
| Win+Shift+C | Close the focused window | `TilingCloseWindow` |
| Win+Period / Win+Comma | Focus next / previous screen | `TilingFocusScreen` |
| Win+O | Move the focused window to the next screen | `TilingMoveToScreen` |
| Win+Enter | Launch the terminal (`Settings.TerminalCommand`, default `wt.exe`, falling back to `cmd.exe`) | `TilingLaunchTerminal` |
| Win+R | Program launcher (Phase 6) | `ToggleProgramLauncher` |
| Win+Ctrl+R | Restart Cairo | existing restart path (`CairoApplication.RestartCairo`) |

Keep a single `TilingKeymap` table class so changing a binding means editing one place. Making the keymap user-configurable is out of scope for v1.

**11. DI registration** in `Cairo Desktop/Program.cs`:
- `services.AddSingleton<ITilingManager, TilingManager>()`
- `services.AddHostedService<TilingHotKeyService>()` and the start/stop service
- one `services.AddSingleton<ICairoCommand, ...>` per command class, in `CairoDesktop.Tiling/Commands/`, following the pattern of `CairoDesktop.MenuBar/Commands/ToggleProgramsMenuCommand.cs`: an `ICairoCommand` with an `Info` class that has an `Identifier`, `Label` (localized), and `IsAvailable` bound to tiling being enabled
- add a ProjectReference to `CairoDesktop.Tiling` from `Cairo Desktop.csproj` and from `CairoDesktop.MenuBar.csproj` (MenuBar needs `ITilingManager` and the view models)

**12. New settings** in `CairoDesktop.Common/Settings.cs`, each with a field, a property using `Set(ref ...)`, and a default:
- `EnableTiling` = true, `EnableTilingOutsideShell` = false
- `TilingGap` = 6, `TilingFollowActivation` = true
- `TilingFloatClasses` = `[]`, `TilingFloatExes` = `[]`
- `TilingModifier` = "Win"
- `EnableFocusFollowsMouse` = true, `FocusFollowsMouseRaise` = false, `FocusFollowsMouseDelayMs` = 100, `FocusFollowsMouseMode` = "Native"
- `TerminalCommand` = "wt.exe"
- `RemapWinL` = true: take Win+L away from Windows so it can be bound to tiling, and move locking to Win+P (see 10a)
- Change the default of `EnableMenuBarMultiMon` to true, because tags are per screen and each screen needs its own bar.

---

## Phase 5: Top bar

### Layout

The target is `CairoDesktop.MenuBar/MenuBar.xaml`, a DockPanel `CairoMenuBarContainer`:

```
[Menu: CairoMenu] [control:TagBar] ............ [MenuExtrasHost (Dock=Right)]
                                  [control:WindowList  ← last child, fills]
```

- The left StackPanel `CairoMenuBar` keeps the `Menu` with only `CairoMenu`, then `<control:TagBar x:Name="tagBar"/>`.
- `MenuExtrasHost` stays unchanged: tray, volume, clock, search. Delete the ActionCenter extra, which is only shown outside shell mode, or leave it.
- `<control:WindowList x:Name="windowList"/>` replaces `StacksContainer` as the last child.

### Wiring

- `CairoDesktop.MenuBar/Services/MenuBarWindowService.cs` `OpenWindow`: inject `ITilingManager` and pass it into the `MenuBar` constructor.
- In `MenuBar` `setupChildren()`:
  - `var model = _tilingManager.GetScreenModel(Screen)`
  - `tagBar.DataContext = model; windowList.DataContext = model;`
  - `Visibility = Collapsed` when tiling is disabled
- Subscribe to an `ITilingManager.EnabledChanged` event and re-bind when it fires.
- Screens are matched by `AppBarScreen.DeviceName`, because `AppBarScreen` instances are recreated on display changes.

### `TagBar.xaml(.cs)` (UserControl in `CairoDesktop.MenuBar/`)

- An `ItemsControl` over `Tags` with a horizontal `StackPanel` items panel. Each tag is a ~22px-wide button/border showing `Label`.
- Visual states, via theme brushes added to `Themes/Cairo.xaml` and given fallbacks in the other themes:
  - **selected:** accent background, bold
  - **occupied:** small dot or underline
  - **urgent:** warning colour
  - **empty and unselected:** dimmed text
- Mouse:
  - left-click: `ViewCommand`
  - right-click: `MoveHereCommand` (move the focused window here)
  - scroll wheel: view the next or previous tag, as in awesome
- Before choosing brush names, check how themes merge (does `Cairo.xaml` always load as the base, with other themes overriding brushes?). Look at `Cairo Desktop/Services/ThemeService` or `CairoApplication.SetTheme`.

### `WindowList.xaml(.cs)` (UserControl)

- An `ItemsControl` over `Windows` with a horizontal panel. Items share the available width (a `UniformGrid Rows=1`, or a custom panel with a max item width of ~200px) and the text is ellipsized.
- Each item shows a 16px `Icon` and `Title`, plus a small marker when floating.
- `IsFocused`: highlighted background, the same style family as the old `CairoTaskbarButtonActiveStyle` but restyled for the menu bar height.
- Mouse: left-click focuses, middle-click closes, and right-click opens a context menu with Float toggle, Move to tag ▸ 1–10, and Close.

### Cairo menu (top-left) settings

The user wants settings to live here. Today `MenuBar.xaml.cs CairoMenu_Opened` builds a flat list of command ids (`cairoMenuItems`).

- Add a **"Window Manager" submenu** after `OpenCairoSettings`:
  - checkable items bound to settings: Tiling enabled, Focus follows mouse, Raise on focus, Follow app activation
  - a "Gaps" submenu with 0 / 4 / 6 / 10 / 16 as radio-style items
  - Equalize current tag
  - "Window manager settings…", which opens SettingsUI on a new "Tiling" tab
- Implementation: allow a `string[]` entry to name a submenu builder, or build this submenu in a dedicated helper `BuildWindowManagerMenu()` that inserts after `OpenCairoSettings`. Keep the existing pattern of `INotifyPropertyChanged` for live updates. Use `Settings.PropertyChanged` for the IsChecked state.
- Final top-level list:
  - AboutCairo, CheckForUpdates
  - | OpenCairoSettings, **Window Manager ▸**, OpenControlPanel, OpenWindowsSettings
  - | ShowRunDialog, StartTaskManager
  - | ExitCairo
  - | Sleep, Hibernate, Restart, ShutDown
  - | Lock, LogOff

### SettingsUI "Tiling" tab

Add it to `Cairo Desktop/SettingsUI.xaml` in place of the removed Desktop/Taskbar tabs. Bind to the Phase 4 settings: checkboxes, a gap slider or combo, the modifier combo, a terminal command textbox, and float class/exe lists as comma-separated textboxes. Add the `"tiling"` tab id to `SettingsUIService.Show`. Add localization keys `sSettings_Tiling*` (English, at minimum; other languages fall back to English, so check how `Language.cs` falls back).

---

## Phase 6: Fuzzy program launcher (installed programs only)

**Location:** `CairoDesktop.MenuBar/Launcher/`. MenuBar already references AppGrabber.

**1. `ProgramIndex`** (singleton, registered in DI)
- Source: `IAppGrabber.GetApps()` (`CairoDesktop.AppGrabber/AppGrabberService.cs:128`). It already enumerates the user and common Start Menu folders (`searchLocations`) plus Store apps, excludes `\Startup`, and returns `ApplicationInfo` (Name, Path, Target, IsStoreApp, Icon). This is exactly "installed programs only". **Do not** include the files or Windows Search results that the Search extra provides.
- Build the index on a background thread at startup and dedupe by `(Name, Target ?? Path)`.
- Refresh in the background when the launcher opens if the index is older than 10 minutes. Also refresh from a debounced (2 s) `FileSystemWatcher` on the Start Menu folders.
- Expose an immutable snapshot list.
- Load icons lazily. Check how `ApplicationInfo.Icon` loads; it is probably already lazy/async. Use it if so.

**2. `LaunchHistory`**
- `%LOCALAPPDATA%\Cairo Desktop\launcher-history.json`, a map of `key → { count, lastUsedUtc }`.
- `Frecency = count * decay(lastUsed)`.

**3. `ProgramLauncherWindow.xaml(.cs)`**
- A borderless, topmost, `ShowInTaskbar=false` WPF window. It is ~600px wide, centred horizontally on the focused screen, and placed just below that screen's menu bar. Style it with the menu theme brushes.
- Contents: a search `TextBox` and a `ListBox` of the top 10 `FuzzyMatcher.Rank(...)` results. Each result shows its icon, its name with the matched characters in bold (using `MatchPositions`), and a dim secondary line with the exe name.
- Primary key is `Name`; the secondary key is the file name of `Target` (or `Path`) without extension.
- Keys:
  - typing filters results live (synchronously; the list is small, ~hundreds of items)
  - ↑/↓, Ctrl+J/K, and Tab/Shift+Tab move the selection
  - Enter launches via `IAppGrabber.LaunchProgram(app)` and records history
  - Ctrl+Enter runs as admin via `LaunchProgramAdmin`
  - Escape closes
  - losing activation (`Deactivated`) closes
- On show: call `Activate()` and focus the TextBox. Use the `AllowSetForegroundWindow`/Alt-tap helper from `WindowArranger.Focus`, since the window is opened from a hotkey.
- The tiling engine must ignore this window. It already does, because it belongs to Cairo's process.

**4. `ToggleProgramLauncherCommand`**
- Identifier `ToggleProgramLauncher`. Opens the launcher on the focused screen, or closes it if open.
- Bind it to Win+R in `TilingHotKeyService` (remove Win+R → ShowRunDialog from `ShellHotKeyService`). It also works when tiling is off: register Win+R in `ShellHotKeyService` itself if tiling is disabled.
- The classic Run dialog stays reachable from the Cairo menu.

---

## Phase 7: Polish and edge cases (v1 checklist)

- **Restore safety:** on any startup, call `HiddenWindowJournal.RestoreAll()` before anything else. Also do a sanity sweep: any top-level window that is hidden and journaled gets shown.
- **UWP apps** (`ApplicationFrameWindow`): moving them works, but SW_HIDE on frame-hosted windows can misbehave. If testing shows problems, hide these by moving them offscreen to `(-32000, -32000)` instead, behind a flag on `ManagedWindow`.
- **Elevated windows:** we can't move them (UIPI). They float automatically after `ERROR_ACCESS_DENIED`. Document this in the settings tab.
- **Fullscreen apps and games:** if a window covers its monitor and has no caption (`FullScreenHelper` in ManagedShell detects this), do not tile it. Treat it as fullscreen.
- **Never hide windows we don't manage.**
- **Logging:** use `ShellLogger` for every hide, show, and arrange failure, at Debug level.
- **Out of scope for v1:** saving layouts across restarts, toggle-view (several tags visible at once), per-tag layouts other than BSP, a configurable keymap, and Win+drag to move or resize floating windows.

---

## Critical files

**Modified**
- `Cairo Desktop/Cairo Desktop/Program.cs`
- `Cairo Desktop/Cairo Desktop/CairoApplication.xaml.cs`
- `Cairo Desktop/Cairo Desktop/CairoApplicationInitializationService.InitializationRoutines.cs`
- `Cairo Desktop/Cairo Desktop/Services/ShellHotKeyService.cs`
- `Cairo Desktop/Cairo Desktop/Services/SettingsUIService.cs`
- `Cairo Desktop/Cairo Desktop/SettingsUI.xaml(.cs)`
- `Cairo Desktop/Cairo Desktop/Welcome.xaml(.cs)`
- `Cairo Desktop/Cairo Desktop/Themes/*.xaml`
- `Cairo Desktop/CairoDesktop.MenuBar/MenuBar.xaml(.cs)`
- `Cairo Desktop/CairoDesktop.MenuBar/Services/MenuBarWindowService.cs`
- `Cairo Desktop/CairoDesktop.MenuBar/Services/MenuBarHotKeyService.cs`
- `Cairo Desktop/CairoDesktop.Common/Settings.cs`
- `Cairo Desktop/CairoDesktop.Common/Localization/{DisplayString,Language}.cs`
- `Cairo Desktop/CairoDesktop.Application/Interfaces/ICairoApplication.cs`
- `Cairo Desktop/CairoDesktop.Interop/` (new `User32.cs`, `Dwmapi.cs`)
- `Cairo Desktop/Cairo Desktop.sln`

**New**
- `Cairo Desktop/CairoDesktop.Tiling.Core/`
- `Cairo Desktop/CairoDesktop.Tiling/`
- `Cairo Desktop/Tests/CairoDesktop.Tiling.Core.Tests/`
- `CairoDesktop.MenuBar/{TagBar,WindowList}.xaml`
- `CairoDesktop.MenuBar/Launcher/*`
- `ShellWindowService` and `WallpaperWindow`

**Deleted**
- `CairoDesktop.DynamicDesktop/`, `CairoDesktop.Taskbar/`, `Cairo Explorer/`
- Stacks*, ProgramsMenu*, StacksManager, IDesktopManager, AppGrabberTaskCategoryProvider, TaskViewCommand, AddStack/RemoveStack commands

## Existing code to reuse

- `CairoDesktop.Common/HotKey.cs`, `HotKeyManager.cs`: global hotkeys (RegisterHotKey plus ComponentDispatcher).
- `CairoDesktop.Infrastructure/DependencyInjection/CairoBackgroundService`: the hosted-service pattern used by `MenuBarHotKeyService` / `ShellHotKeyService`.
- `CairoDesktop.Infrastructure/Services/CommandService.cs` and `ICairoCommand`: all actions are commands.
- `CairoDesktop.Infrastructure/Services/WindowManager.cs` (`ScreenState`, `ScreensChanged`): monitor list and change events.
- `CairoDesktop.Infrastructure/ObjectModel/AppBarWindowService.cs` / `MenuBarWindowService.cs`: per-screen menu bars.
- `CairoDesktop.Common/Helpers/CursorHelper.IsCursorOnScreen`: picking the "focused screen" for commands.
- `CairoDesktop.AppGrabber/AppGrabberService.GetApps()`, `LaunchProgram`, `LaunchProgramAdmin`: launcher data and launch.
- `DesktopManager.CreateShellWindow` logic: moved into `ShellWindowService`.

## Verification

1. **Unit tests (Linux, any phase after 3):** `dotnet test "Cairo Desktop/Tests/CairoDesktop.Tiling.Core.Tests"` passes.
2. **Build (Windows, or Linux with `-p:EnableWindowsTargeting=true`):** `dotnet build "Cairo Desktop/Cairo Desktop.sln" -c Debug` succeeds for all TFMs.
3. **Manual test in a Windows VM:**
   - **Dev mode:** run under Explorer with `EnableTilingOutsideShell=true` and `TilingModifier=Alt+Win`. Open 3–4 Notepad and Terminal windows. Check that they tile as a BSP, that directional focus, swap, and resize work, that tags hide and show windows, and that the tag bar and window list update with the focused item highlighted.
   - **Crash recovery:** kill Cairo with Task Manager while windows sit on hidden tags. Relaunch, and confirm every hidden window reappears. Also confirm the windows reappear on a normal exit.
   - **Shell mode:** run with `/shell=true` (or as the configured shell). Confirm there is no desktop, taskbar, Stacks, Places, or Programs menu. Confirm the wallpaper is visible, every monitor has a menu bar with its own 10 tags, focus follows the mouse, Win+1..0 and Win+Shift+1..0 work, and the clock, tray, and search extras are present.
   - **Launcher:** Win+R, type "vsc" or "note", press Enter, and the app launches. The next time, it ranks higher (frecency). Esc and clicking away both close the launcher.
   - **Multi-monitor:** move a window across monitors with the mouse and with Win+O. Unplug a monitor (or change the VM display count) and confirm its windows move to the primary monitor's tags.
   - **Exit:** confirm focus-follows-mouse SPI values are restored afterwards (`SystemParametersInfo` gets show the original values).
   - **Win+L / Win+P:**
     - In shell mode, Win+L focuses right and does not lock.
     - Win+P locks. After unlocking, Win+L still doesn't lock.
     - The Cairo menu's "Lock" item still works.
     - Exit Cairo normally: `DisableLockWorkstation` is back to its original value (absent), and Win+L locks under Explorer again.
     - Kill Cairo with Task Manager, then relaunch and exit: the value is restored then too.
     - Record which path made Win+L bindable (`RegisterHotKey` or the low-level hook).
