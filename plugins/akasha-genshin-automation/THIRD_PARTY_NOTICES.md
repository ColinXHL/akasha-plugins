# Third-party notices

This inventory must be completed before the first distributable plugin package is produced.

## BetterGI

Akasha Automation currently includes four unmodified configuration list files, the hangout option configuration, the minimal PP-OCRv4 and Silero VAD model sets, six AutoPick templates and twenty AutoSkip templates from BetterGI `0.62.0`:

- `Assets/Config/Pick/default_pick_black_lists.json`
- `Assets/Config/Skip/default_pause_options.json`
- `Assets/Config/Skip/pause_options.json`
- `Assets/Config/Skip/select_options.json`
- `Assets/Model/PaddleOCR/README.md`
- `Assets/Model/PaddleOCR/test_pp_ocr.png`
- PP-OCRv4 mobile detection `inference.yml` and `slim.onnx`
- PP-OCRv4 mobile recognition `inference.yml` and `slim.onnx`
- `Assets/Recognition/AutoPick/1920x1080/E.png`
- `Assets/Recognition/AutoPick/1920x1080/F.png`
- `Assets/Recognition/AutoPick/1920x1080/G.png`
- `Assets/Recognition/AutoPick/1920x1080/L.png`
- `Assets/Recognition/AutoPick/1920x1080/icon_settings.png`
- `Assets/Recognition/AutoPick/1920x1080/icon_option.png`
- `Assets/Config/Skip/hangout.json`
- all declared files below `Assets/Recognition/AutoSkip/1920x1080`
- `Assets/Model/Vad/LICENSE`, `README.md`, and `silero_vad.onnx`

Phase 4 and Phase 5 include translated AutoPick and AutoSkip behavior derived from the source files declared in the BetterGI manifest. BetterGI is copyright its contributors and licensed under GPL-3.0. The upstream repository is `https://github.com/babalae/better-genshin-impact`. Source and release pins, file mappings, hashes, local translation decisions, and list statistics are recorded in `DERIVATION.md` and `upstream/bettergi/`.

The model README identifies PaddleOCR inference models converted through Paddle2ONNX. Their upstream license texts and the exact conversion provenance must be included in the Phase 7 release license review; the BetterGI archive and every copied file are already pinned by SHA-256.

## Runtime libraries introduced in Phase 3

- OpenCvSharp4 `4.11.0.20250507` — Apache-2.0 as declared by the NuGet package; native OpenCV notices must accompany the release.
- Microsoft.ML.OnnxRuntime `1.21.0` — MIT, copyright Microsoft Corporation.
- YamlDotNet `16.3.0` — MIT.
- Vanara.PInvoke.CoreAudio `4.1.3` — MIT; used by process-specific loopback capture.
- SharpDX.Direct3D11 `4.2.0` and its SharpDX dependencies — used only by the Windows Graphics Capture adapter; include their upstream license notice in the release package.

## Silero VAD

The packaged `silero_vad.onnx` is from `https://github.com/snakers4/silero-vad` and is licensed under MIT, copyright 2020-present Silero Team. The unmodified upstream license is packaged beside the model and must remain in the Phase 7 release.

Remaining expected categories include:

- Fischless.GameCapture.
- Fischless.WindowsInput.
- Windows interop libraries.

No Yap model has been copied. If it is introduced later, its source, license and exact hash must be added before packaging.
