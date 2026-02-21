\# BluesBar Agent Rules



\## 🔧 Build Command



Use this exact command:



```bash

dotnet build BluesBar.csproj --no-restore -clp:ErrorsOnly

```



---



\## 🟢 Default Mode: BACKEND-ONLY



Unless I explicitly say \*\*"UI changes allowed"\*\*, you MUST treat this task as backend-only.



You MAY modify:



\* `Systems/\*\*`

\* `Gambloo/\*\*/\*.cs`

\* `Profile\*.cs`

\* `Level\*.cs`

\* backend helper C# files related to logic, data, and services



---



\## 🟡 UI Changes (ONLY WHEN EXPLICITLY REQUESTED)



You MAY modify XAML only if I explicitly include one of these phrases in the request:



\* \*\*"UI changes allowed"\*\*

\* \*\*"edit XAML"\*\*

\* \*\*"update the UI"\*\*

\* \*\*"XAML is permitted"\*\*



If none of those phrases appear, UI is considered locked.



When UI changes are allowed:



\* Keep edits minimal and targeted

\* Do NOT mass-reformat XAML

\* Do NOT rename bindings, `x:Name`, or resources unless required

\* Prefer additive changes over rearranging existing layout



---



\## 🔴 Always Locked (Never modify unless I explicitly ask AND name the file/folder)



These are sensitive areas. Do not touch unless I explicitly request and specify targets:



\* `Assets/\*\*`

\* `Packages/\*\*/Assets/\*\*`

\* any binary files (`\*.dll`, `\*.exe`, `\*.pdb`, etc.)



---



\## 🧠 Required Task Workflow



For every task:



1\. Identify the exact files you plan to touch (list them)

2\. Briefly explain the current flow

3\. Propose the smallest change that satisfies the request

4\. Make edits

5\. Show `git diff`

6\. Run the build command (unless I say not to)

7\. Commit on a new branch: `agent/<task-name>`



---



\## 📏 Max Edit Scope



\* Do not modify more than \*\*8 files per step\*\*

\* If more are required:



&nbsp; \* STOP

&nbsp; \* Explain why

&nbsp; \* Wait for approval



---



\## 🎯 General Philosophy



\* Prefer minimal, surgical changes

\* Do not refactor unrelated code

\* Do not reformat entire files

\* Preserve existing naming/style unless required for correctness

\* Never invent new architecture unless asked



Stability > cleverness. Follow these rules strictly.



