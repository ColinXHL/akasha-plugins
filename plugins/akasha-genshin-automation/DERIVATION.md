# Source derivation

Parts of the future automatic pickup and automatic dialogue implementation will be derived from BetterGI, licensed under GPL-3.0.

The initial source snapshot is pinned for provenance and future selective synchronization:

- Upstream repository: `https://github.com/babalae/better-genshin-impact.git`
- Upstream branch: `origin/main`
- Source commit: `0eb90304c4e4fa1f5cee2a4cbf68de6c8200ec94`
- BetterGI version: `0.62.1-alpha.2`
- Intended extraction scope: capture, recognition, OCR, input, automatic pickup, and automatic dialogue dependencies
- Runtime asset baseline: BetterGI `0.62.0`, release commit `92b8beab53da3a1f86d625914c10d180fb05b0cd`
- Runtime artifact: `BetterGI_v0.62.0.7z`, official release URL and SHA-256 `11ccb62b7580dfdf15950300415cbde57181e5352dd817040bef2f9bc58bbb89`
- Upstream synchronization policy: selective, release-gated synchronization of AutoPick, AutoSkip, required recognition infrastructure, templates, configuration data, and models

## Imported runtime configuration

The following files were copied byte-for-byte from an installed BetterGI `0.62.0` distribution on 2026-07-14. Their source and target relative paths are identical below `Assets`:

| BetterGI path | SHA-256 | Entries | Unique entries |
|---|---|---:|---:|
| `Assets/Config/Pick/default_pick_black_lists.json` | `1129650653eed1ec7e81676b3f616895feb9433ab616efc98ac360232c7e7ea9` | 4914 | 4891 |
| `Assets/Config/Skip/default_pause_options.json` | `212962f57e0bb0c04d9c3af062be53ddd929573f0399bc29b4476ec646f2ef65` | 66 | 61 |
| `Assets/Config/Skip/pause_options.json` | `fcc7d1e985862f0e3b0cc59cad7312642f7e96a318a73fc7646c093701a08b5b` | 5 | 5 |
| `Assets/Config/Skip/select_options.json` | `8585ca3368566a6efe15ef52a816494ac2469470d7ac3b806d3d329cb4b36e88` | 1 | 1 |

The authoritative machine-readable mapping is `upstream/bettergi/manifest.json`; `upstream/bettergi/hashes.json` is the package integrity inventory. No content changes were made to these four files. On 2026-07-14, the official release archive size and SHA-256 were verified, then all four declared files were selectively extracted from its `BetterGI/` archive root and matched the committed files byte-for-byte.

## Imported PaddleOCR V4 runtime

On 2026-07-15, six additional files were selectively extracted byte-for-byte from the same pinned BetterGI `0.62.0` release archive. They form the smallest verified PaddleOCR V4 set required by Phase 3:

| BetterGI path | Size | SHA-256 |
|---|---:|---|
| `Assets/Model/PaddleOCR/README.md` | 271 | `195c0939e6ec90e99e10153c22778d4e8f18574cfbc776937796e3adfb950981` |
| `Assets/Model/PaddleOCR/test_pp_ocr.png` | 126994 | `583caa82c158da88cbeb0bdb209ada6a7658fd43df306c2a2aa846700a4de376` |
| `Assets/Model/PaddleOCR/Det/V4/PP-OCRv4_mobile_det_infer/inference.yml` | 956 | `7a71be98abcc1038fb0d10fad3efb58407fcd5ac4ac3fb45a5544c143bc4763e` |
| `Assets/Model/PaddleOCR/Det/V4/PP-OCRv4_mobile_det_infer/slim.onnx` | 4764885 | `c0f2e256776e81d9e38f49e7cc2a37864a326ee8097e84adf30a8e0ebcc0b24b` |
| `Assets/Model/PaddleOCR/Rec/V4/PP-OCRv4_mobile_rec_infer/inference.yml` | 60209 | `018c94645678dc492754754291705c4999f35c6e5be854a42b4f918fefd06ab4` |
| `Assets/Model/PaddleOCR/Rec/V4/PP-OCRv4_mobile_rec_infer/slim.onnx` | 10826716 | `df79157f86aa181ee0daa43364203cfc892f98e2a1b425614a1c98e0b96d7393` |

`PaddleOnnxOcrSessionFactory` is a local compatibility translation of the pinned BetterGI PP-OCRv4 preprocessing, DB text detection, and CTC decoding behavior. It deliberately depends on Core contracts rather than BetterGI static application state. The pinned preheat image is executed in tests through the real ONNX runtime, and session disposal is asserted.

## Imported AutoPick behavior and templates

Phase 4 translates the AutoPick behavior from BetterGI source commit `0eb90304c4e4fa1f5cee2a4cbf68de6c8200ec94`. The reviewed source paths are recorded in `upstream/bettergi/manifest.json`. The local split is intentional:

- `BetterGiPort/Upstream/AutoPick` preserves OCR cleanup, text projection, hard-coded `DoNotPick` conditions, default/user list merging, and rule priority.
- `BetterGiPort/Compatibility/AutoPick` translates BetterGI recognition assets, 1080p ROI constants and resolution scaling to Core capture/template contracts.
- `Features/AutoPick` owns Akasha configuration, diagnostics and conversion of a successful decision to one `AutomationIntent`; it never calls `SendInput` directly.

The default pickup blacklist is the pinned asset included in each plugin Release.
Updating it therefore requires the same reviewed source pin, test, package, Release,
and catalog transaction as the Worker code. User-configured exact blacklist entries
continue to merge with the packaged default.

The live host uses BetterGI's default 50 ms dispatcher cadence and subtracts frame processing time before delaying. After BetterGI's text-rectangle refinement succeeds, AutoPick calls the recognition model directly without the detection model, matching BetterGI's `OcrWithoutDetector` fast path; multi-option AutoDialogue continues to use detection plus recognition.

Six templates were selectively extracted byte-for-byte from the pinned BetterGI `0.62.0` release artifact on 2026-07-15:

| BetterGI path | Packaged target below `Assets/Recognition/AutoPick/1920x1080` | Size | SHA-256 |
|---|---|---:|---|
| `GameTask/AutoPick/Assets/1920x1080/E.png` | `E.png` | 547 | `09cc25ef17a7aab56f147f40f4a1373ae3bce06fc966929cc8d34ef85e61cd55` |
| `GameTask/AutoPick/Assets/1920x1080/F.png` | `F.png` | 515 | `ce0100ebf90a4c98e6b34b5ee3777d973c9ae05322972d552fc718817e66271b` |
| `GameTask/AutoPick/Assets/1920x1080/G.png` | `G.png` | 914 | `724edac6d0da519ac44d7a973db51990a95cf9b80005d1973caaaabb684543d7` |
| `GameTask/AutoPick/Assets/1920x1080/L.png` | `L.png` | 308 | `51008048871d25dbb5713de10cadfa3516c11047eb0675994b997cdc18910e87` |
| `GameTask/AutoPick/Assets/1920x1080/icon_settings.png` | `icon_settings.png` | 960 | `3bc1a9010f337e6990aae0b88ef81b0ea5f4621465bdca45fb90dc0190b07537` |
| `GameTask/AutoSkip/Assets/1920x1080/icon_option.png` | `icon_option.png` | 480 | `b4f03c5641447fc30f2a3a92ab189e0fbc55444985c9baeedd68d3d506e68505` |

The translation intentionally omits BetterGI UI/ViewModel state, `TaskContext`, message boxes, mouse-wheel fallback and direct input. The Phase 4 Worker registers `DisabledInputService`; real input remains opt-in work for release validation.

The separate LiveTestHost exposed a real-game compatibility gap in the original local `WindowsSendInputService`: Windows accepted virtual-key-only input, but Genshin did not react. The corrected keyboard descriptor follows pinned BetterGI commit `0eb90304c4e4fa1f5cee2a4cbf68de6c8200ec94`, `Fischless.WindowsInput/InputBuilder.cs`: populate both `wVk` and the `MapVirtualKey` scan code, preserve extended-key flags, and submit key-down/key-up as one `SendInput` batch. Dialogue clicks still target the BetterGI OCR/template region center, while the local adapter additionally scales capture coordinates to the current game client and normalizes screen coordinates across the complete virtual desktop. The Akasha foreground checks, Input Arbiter and emergency stop remain local safety constraints.

## Imported AutoDialogue behavior, templates and VAD

Phase 5 translates BetterGI `AutoSkipTrigger`, `AutoSkipConfig`, the AutoSkip recognition declarations, hangout configuration and the four audio/VAD classes from source commit `0eb90304c4e4fa1f5cee2a4cbf68de6c8200ec94`. Direct clicks, `Thread.Sleep`, global `TaskContext`, UI state and process lifetime calls were replaced by `AutomationIntent`, `IClock`, explicit Feature state and Worker runtime resources.

From the pinned BetterGI `0.62.0` release archive, Phase 5 selectively imports:

- 20 AutoSkip PNG templates under `GameTask/AutoSkip/Assets/1920x1080`, packaged below `Assets/Recognition/AutoSkip/1920x1080`;
- `GameTask/AutoSkip/Assets/hangout.json`, packaged as `Assets/Config/Skip/hangout.json`;
- `Assets/Model/Vad/LICENSE`, `README.md` and `silero_vad.onnx`.

The exact source/target mapping, file sizes and every SHA-256 are authoritative in `upstream/bettergi/manifest.json` and independently mirrored in `upstream/bettergi/hashes.json`. The Silero model hash is `1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3`; its bundled MIT license hash is `840a2b8a9e6091a4edc7531318b9392b1d57dd9a587c83ca3f022731c0b0e858`.

The local split is intentional:

- `BetterGiPort/Upstream/AutoSkip` preserves option cleanup and priority rules.
- `BetterGiPort/Compatibility/AutoSkip` preserves templates, 1080p ROI scaling, color/contour thresholds and special-scene recognition.
- `BetterGiPort/Compatibility/Audio` preserves Silero inference and process-loopback sampling.
- `Features/AutoDialogue` owns configuration, scheduler-driven waits, independent scene handlers and conversion to one action intent per frame.

The Worker and DevHost continue to register only disabled/observe-only input services. Audio capture is released when AutoDialogue is disabled and before Worker shutdown acknowledgement.

Future extraction work must add exact copied source files, models, copyright notices, material changes, and synchronization decisions here.
