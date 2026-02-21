\# BACKEND\_MAP.md (BluesBar)



This file is the quick map of BluesBar’s backend systems.

Read this before making non-trivial changes.



---



\## 🗺️ Repo Layout (high-level)



\### Systems (core logic)



\* `Systems/`

&nbsp; Contains core logic: profile persistence, syncing, level progression, formatting utilities, and launchers.



Key files (observed):



\* `Systems/Profile.cs`

\* `Systems/ProfileManager.cs`

\* `Systems/ProfileSync.cs`

\* `Systems/LevelCalculator.cs`

\* `Systems/LevelProgression.cs`

\* `Systems/NumberFormat.cs`

\* `Systems/AimTrainerLauncher.cs`



\### Gambloo (gambling / games)



\* `Gambloo/`

&nbsp; Game logic + scene hosting.

\* `Gambloo/Cards/`

&nbsp; Card-related logic (deck, theme, factories, extensions).

\* `Gambloo/Scenes/`

&nbsp; Mostly UI (XAML) scenes for gambling games.



Key files (observed):



\* `Gambloo/GamblooHost.cs`

\* `Gambloo/IGamblooScene.cs`

\* `Gambloo/Cards/Card\*.cs`

\* `Gambloo/Scenes/\*.xaml` (UI)



\### Rooms (main windows)



\* `Rooms/\*.xaml`

&nbsp; UI windows for major areas: aim room, backpack, gambloo, store.



\### Assets



\* `Assets/`

&nbsp; Images used by UI (locked by default).



\### Packages



\* `Packages/BluesAimTrain/...`

&nbsp; Contains shipped/external artifacts for AimTrain integration (treat as locked unless explicitly requested).



---



\## 🧠 Data \& State (where truth lives)



\### Profile + Persistence



\* The profile schema is defined in `Systems/Profile.cs`.

\* Profile reading/writing and lifecycle logic is managed by `Systems/ProfileManager.cs`.

\* Cross-app or cross-component syncing behavior is managed by `Systems/ProfileSync.cs`.



Rules:



\* Prefer adding fields in one place (Profile schema) and updating all readers/writers accordingly.

\* Backwards compatibility matters: default missing fields safely.



\### Leveling



\* `Systems/LevelCalculator.cs` and `Systems/LevelProgression.cs` drive XP/level logic.

\* Keep leveling logic deterministic and unit-testable where possible.



\### Currency / Wallet / Inventory (fill in as you confirm)



\* Wallet/currency logic likely touches Profile + Systems.

\* Inventory/backpack should ideally be driven by backend models and rendered in Rooms UI.



---



\## 🔍 How to find things (standard agent search recipe)



When implementing a feature:



1\. Locate where the state is stored (Profile? in-memory?).

2\. Locate who updates it (Managers/Systems).

3\. Locate who reads it (UI bindings, scene hosts, windows).

4\. Implement backend changes first.

5\. Only do UI if user explicitly allows it.



---



\## ✅ Definition of Done for backend work



\* Build passes via:



&nbsp; \* `dotnet build BluesBar.csproj --no-restore -clp:ErrorsOnly`

\* Minimal file changes

\* No unrelated refactors

\* Any schema change includes safe defaults



