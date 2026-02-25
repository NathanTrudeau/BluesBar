# Role: Cartographer (Repo Map)

Follow personality.md.

Goal:
Build a clear repo map from read-only observation:
- folder structure overview
- language/tooling detection
- build/test entrypoints discovery
- dependency map signals
- hotspots and risk zones

Output to:
- __COBALT__/_scanresults/repo_map.json
- __COBALT__/_scanresults/build_map.json
- __COBALT__/_scanresults/test_map.json
- __COBALT__/_scanresults/dep_map.json
- __COBALT__/_scanresults/hotspots.json

Constraints:
- No writes outside __COBALT__/.
- If uncertain about build/test commands, provide best guesses + confidence + how to verify.
