# Game version

The game build SpireParty is developed and tested against. Re-check `release_info.json`, `sts2.runtimeconfig.json` and `sts2.dll` before every release.

| Field | Value |
| --- | --- |
| Checked on | 2026-11-16, re-checked 2026-11-17 before the M1 gate and 2026-11-18 for M3, and 2026-11-19 with `tools/check-update.ps1` (all steps PASS, 17/17 adapters; unchanged; `sts2.dll` SHA-256 starts 62ecbe91093e1fa8) |
| Steam branch (beta key) | `public-beta` |
| Steam build ID | 26660232 |
| `release_info.json` version | v0.113.1 |
| `release_info.json` commit | d09d48e5 |
| `release_info.json` date | 2026-11-14T02:31:11+00:00 |
| `release_info.json` branch | v0.113.1 |
| `release_info.json` main_assembly_hash | 358281912 |
| .NET runtime (`sts2.runtimeconfig.json`) | Microsoft.NETCore.App 9.0.7, tfm net9.0 |
| Engine | MegaDot v4.5.1.m.14.mono.custom_build |
| `sts2.dll` size (Windows) | 10,648,064 bytes |
| Save versions (log) | PrefsSave v2; ProfileSave v2; SerializableProgress v24; RunHistory v11; SerializableRun v21; SettingsSave v8 |
| ModelIdSerializationCache hash (vanilla, log) | 1748280997 |

## Machines

| Machine | OS | Build verified | SpireParty loads |
| --- | --- | --- | --- |
| Windows VM | Windows 11 Pro 10.0.26200 | v0.113.1 / Steam 26660232 | Yes (0.0.1, 2026-11-16; 0.1.0 release zip with chat, 2026-11-19: 4-player autopilot party to Act 2 with chat tests on host and c2, checksums match, 30/30 chat lines everywhere) |
| Linux laptop | Fedora 44 (kernel 7.2.13) | v0.113.1 / Steam 26660232, `public-beta`; Linux `main_assembly_hash` 1531542858 (differs from Windows, expected) | Yes (0.0.1, 2026-11-17) |
