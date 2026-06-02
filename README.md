# CloserXR - Life Insurance Sales Negotiator

CloserXR is a CSE 165 mixed reality prototype where a charismatic life insurance sales agent practices a pitch with the user. The project focuses on conversation-aware body language: the avatar talks, points, paces, argues, dismisses objections, and celebrates based on the user's response.

## Team

- Hayden Kwok
- Mark Sui
- Tommy Tran
- Tsering Wangyal

## Project Folder

Open this Unity project:

```text
CloserXR--Sales_Negotiator
```

Unity version:

```text
2022.3.62f1
```

## Features

- Meta Quest passthrough setup for MR
- Project 3 style head-tracked Quest view using `OVRCameraRig` / `CenterEyeAnchor`
- Guardian-backed room outline for the Quest play area
- Sales agent avatar using Mixamo animations
- Animator state machine for talking, pacing, pointing, arguing, dismissing, and celebrating
- Basic spatial anchor support on device builds
- Proximity-aware pacing that stays inside the visible room bounds
- VR status panel with live mic state (recording / listening / muted), speaking status, and Gemini connection
- Three speech input modes selectable in the Inspector on `PushToTalkSpeechInput`:
  - **AutoVAD** (default) — mic always open; amplitude threshold starts capture; 1.2 s silence auto-submits
  - **AndroidSpeechRecognizer** — Android STT returns text directly, lower latency (~1.5–2.5 s); falls back to AutoVAD in editor
  - **PushToTalk** — hold trigger / Space to record, release to submit
- Trigger / Space works as a force-start / force-submit override in all auto modes
- Mic muted automatically while agent TTS is playing (0.5 s tail) to prevent echo pickup
- Gemini REST API integration with multi-turn conversation history (up to 10 turns)
- Android TTS voice output - the agent speaks aloud on device with procedural lip variation
- Local canned dialogue fallback when no API key is available
- Life insurance role-play lines for premiums, coverage, term-vs-whole questions, family protection, and closing

## Running The Demo

1. Open `CloserXR--Sales_Negotiator` in Unity.
2. Open `Assets/Scenes/SampleScene.unity`.
3. Connect a Meta Quest 2.
4. Build and run the scene to the headset.
5. Just speak — the mic listens automatically in the default AutoVAD mode.

The floating VR panel shows mic state (`Listening` / `● REC` / `Muted`), Gemini connection, room source, and the latest dialogue exchange.

Without a Gemini key, or as quick shortcuts, use the preset Quest inputs:

- `A`: ask what kind of life insurance this is
- `B`: object to the premium
- `X`: ask how it protects your family
- `Y`: move forward

## Final Demo Flow

1. Start the headset build and point out the passthrough/room outline setup.
2. Let the agent deliver the opening pitch.
3. Press `B` to trigger a premium objection and extra distance.
4. Press `X` to show family-protection dialogue.
5. Press right thumbstick right to show uncertainty/body-language response.
6. Press `Y` to trigger the closing/celebration moment.

## Gemini API Key Setup

The API key is never committed to git. Three sources are checked in priority order:

1. **Inspector override** - paste directly into `GeminiSalesClient` on the prefab (quickest for a one-off test, clear it before committing)
2. **Environment variable** - recommended for desktop development
3. **StreamingAssets file** - required for Quest device builds (Android cannot read env vars)

Without any key, the demo runs on local canned dialogue automatically.

### Mac Development (env var)

```bash
cp .env.template .env
# open .env and replace the placeholder with your real key
source .env
# open Unity from this same terminal session so it inherits the env var
open -a "Unity Hub"
```

The `.env` file is gitignored and stays on your machine only.

### Windows Development (env var)

```powershell
cp .env.template .env
# open .env and replace the placeholder with your real key
# then load it into the current PowerShell session:
Get-Content .env | ForEach-Object {
    if ($_ -match '^([^#][^=]+)=(.+)$') {
        Set-Item "env:$($matches[1].Trim())" $matches[2].Trim()
    }
}
# open Unity Hub from this same session so it inherits the env var
Start-Process "C:\Program Files\Unity Hub\Unity Hub.exe"
```

The `Get-Content` block is the Windows equivalent of `source .env` on Mac — it reads the file and loads the variables into the current session. Unity Hub inherits them because it's launched from the same session.

To make the key permanent across sessions (so you never have to source it again), run this once:

```powershell
[System.Environment]::SetEnvironmentVariable("GEMINI_API_KEY", "your_gemini_api_key_here", "User")
```

Then relaunch Unity Hub normally.

### Quest Device Builds (StreamingAssets file)

```bash
cp CloserXR--Sales_Negotiator/Assets/StreamingAssets/gemini_key.txt.template \
   CloserXR--Sales_Negotiator/Assets/StreamingAssets/gemini_key.txt
# open gemini_key.txt and replace the placeholder with your real key
```

`gemini_key.txt` is gitignored. Build the APK normally - the key is bundled into the build but never committed.

### Getting a Gemini API Key

Visit [Google AI Studio](https://aistudio.google.com/app/apikey) and create a free key for `gemini-2.5-flash`.

## Controls

### Speaking (primary)
In the default AutoVAD mode, just speak naturally — the mic detects your voice and submits automatically when you pause. The panel shows `◌ Listening` when waiting and `● REC` while capturing.

The trigger (or Space in editor) works as a manual override in any mode: press to force-start, release to force-submit immediately without waiting for silence.

### Preset buttons (shortcuts / no Gemini key)
- `A`: ask what kind of life insurance this is
- `B`: object to the premium
- `X`: ask how it protects your family
- `Y`: move forward
- Right thumbstick up: ask how much coverage is needed
- Right thumbstick down: reject the pitch
- Right thumbstick left: ask term vs whole life
- Right thumbstick right: say maybe / think about it

### Changing speech input mode
Select the `SalesAgent` object in the scene (or open `Assets/Prefabs/SalesAgent.prefab`), find the `PushToTalkSpeechInput` component, and set **Mode** in the Inspector:

| Mode | Description |
|---|---|
| `AutoVAD` | Amplitude-triggered, silence auto-submits WAV to Gemini (~3–5 s latency) |
| `AndroidSpeechRecognizer` | Android STT → text → Gemini (~1.5–2.5 s latency), device only |
| `PushToTalk` | Manual hold-to-record |

**AutoVAD tuning knobs (Inspector):**
- `Vad Threshold` — raise if background noise triggers false starts (default 0.02)
- `Silence Seconds` — how long silence waits before submitting (default 1.2 s)
- `Post Agent Mute Seconds` — silence after agent TTS before listening resumes (default 0.5 s)

## Important Files

- `Assets/Scenes/SampleScene.unity`
- `Assets/Prefabs/SalesAgent.prefab`
- `Assets/Animations/SalesAgent.controller`
- `Assets/Scripts/SalesAgent/`
- `Assets/Scripts/SalesAgent/PushToTalkSpeechInput.cs` — AutoVAD / AndroidSTT / PTT with feature flag
- `Assets/Scripts/SalesAgent/SalesAgentTTS.cs` — Android TTS wrapper with procedural lip variation
- `Assets/Scripts/SalesAgent/GeminiSalesClient.cs` — Gemini REST client with multi-turn history
- `Assets/Scripts/SalesAgent/SalesConversationManager.cs` — central conversation hub
- `Assets/Scripts/SalesAgent/SpatialRoomMapDemo.cs`
- `Assets/Scripts/SalesAgent/SalesAgentVRStatusPanel.cs`
- `Assets/Mixamo/`

## Architecture: LLM Pipeline

```
User speaks naturally
  └─► PushToTalkSpeechInput
        ├─ AutoVAD: amplitude → loop buffer → silence timeout → WAV
        │     └─► GeminiSalesClient.GenerateFromAudio()    (~3–5 s)
        │               sends WAV + history → receives response text
        └─ AndroidSpeechRecognizer: Android STT → text
              └─► GeminiSalesClient.GenerateFromText()     (~1.5–2.5 s)
                          sends text + history → receives response text

  response text
    ├─► SalesIntentClassifier  → intent enum
    ├─► SalesDialogueGestureRouter → gesture trigger (0.2 s delay)
    ├─► SalesAgentPacer        → proximity distance update
    └─► SalesAgentTTS.Speak()
          ├─ Android TTS speaks aloud on device
          └─ VariateTalkingSpeed coroutine → organic mouth movement
             (mic automatically muted while agent speaks + 0.5 s tail)
```

Conversation history is maintained across up to 10 turns so Gemini remembers what was already said. The opening pitch is seeded into history as the first model turn.

## Proposal Coverage

- Passthrough: bootstrapped at runtime through Meta XR components
- Spatial anchors: added on Android device builds
- Room mapping demo: `SpatialRoomMapDemo` reads Quest Guardian play-area geometry when available
- VR UI: `SalesAgentVRStatusPanel` shows mic state (pulsing `● REC`, `◌ Listening`, `Muted`), Gemini mode, room source, and speaking/ready status
- Conversation-aware gestures: user and agent text classified into 5 intents; gestures delayed 0.2 s to sync with TTS startup
- Spatial proximity: the agent backs off for objections and moves closer when closing
- Voice output: `SalesAgentTTS` drives Android TTS on device; `SetTalkingSpeed()` varies animator speed procedurally for lip rhythm
- Auto speech input: `PushToTalkSpeechInput` supports AutoVAD, AndroidSpeechRecognizer, and PushToTalk — switchable via Inspector `Mode` field with no code changes
