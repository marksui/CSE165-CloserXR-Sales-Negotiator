# CloserXR Sales Negotiator

CloserXR is a Meta Quest / Unity VR role-play demo with an AI life-insurance sales agent. The scene includes a positioned sales agent, Quest controller shortcuts, a left-hand VR choice menu, push-to-talk voice input, Gemini-backed responses, passthrough support, and Quest-side text-to-speech diagnostics.

## Project Setup

- Unity version: `2022.3.62f1`
- Main scene: `Assets/Scenes/SampleScene.unity`
- Main prefab: `Assets/Prefabs/SalesAgent.prefab`
- Core runtime scripts: `Assets/Scripts/SalesAgent`
- XR packages: Meta XR SDK, Unity XR Management, OpenXR

Open the project in Unity, let packages import, then open `SampleScene`.

## Gemini API Key

Gemini is optional for local fallback text, but required for real AI replies, microphone-audio understanding, and the primary English agent voice.

Supported key locations:

- Inspector: paste a key into `GeminiSalesClient.apiKeyOverride`.
- Environment variable: `GEMINI_API_KEY`.
- Quest-friendly file: create `Assets/StreamingAssets/gemini_key.txt` with only the key text inside.

`Assets/StreamingAssets/gemini_key.txt.template` is a placeholder. Do not commit a real API key.

## Build And Run On Quest

1. In Unity, switch platform to Android.
2. Confirm `SampleScene` is in Build Settings.
3. Confirm OpenXR and Meta Quest support are enabled.
4. Connect the Quest by USB and allow device permissions.
5. Use `Build And Run`.

The Android XR loader is configured to load and run automatically. If the headset opens the app flat or does not enter VR, check `Project Settings > XR Plug-in Management > Android` and make sure OpenXR / Meta Quest support is active.

## VR Controls

| Control | Behavior |
| --- | --- |
| Right trigger | Start recording voice |
| Right trigger again | Stop recording and submit audio |
| Left grip or left thumbstick click | Toggle the VR choice menu |
| Left grip / left thumbstick click again | Cancel and close the menu |
| Left trigger while menu is open | Select the highlighted menu row |
| A | Ask what product is being sold |
| B | Say the premium is too expensive |
| X | Ask how it protects family |
| Y | Say you want to move forward |
| Right stick up | Ask about coverage amount |
| Right stick down | Reject / not interested |
| Right stick left | Ask term vs whole life |
| Right stick right | Say maybe / need time |

When the menu is open, a thin blue ray appears from the left controller. The ray is hidden when the menu is closed. While the menu is open, the left trigger is reserved for menu selection and the right trigger remains the recording control.

## Choice Menu Actions

The left-hand menu includes both conversation lines and agent actions:

- `Idle`: return the agent to idle.
- `Walk`: start manual pacing.
- `Dance`: play the full dance/celebration action.
- Conversation rows: submit predefined sales negotiation lines to the agent.

The agent defaults to idle. Walking is a menu action, not an automatic startup behavior.

## Voice Input

Voice input is push-to-talk:

1. Press the right trigger to start recording.
2. Speak.
3. Press the right trigger again to stop recording and submit.

If Gemini is not configured, the app can record audio but cannot understand it; it will use local fallback responses. For Quest builds, prefer `Assets/StreamingAssets/gemini_key.txt` because Android builds do not reliably inherit desktop environment variables.

## Agent Voice / TTS

The agent uses `SalesAgentTTS`.

The primary voice path is Gemini native English TTS. `SalesAgentTTS` sends the agent's reply to `gemini-2.5-flash-preview-tts`, decodes the returned 24 kHz PCM audio, and plays it through a Unity `AudioSource` on the agent.

On Quest, if Gemini TTS is unavailable, the script falls back to Android English TTS. It first tries to synthesize a WAV file and play it spatially through Unity, then tries direct Android TTS playback.

The old procedural sound-wave/tone fallback is disabled by default because it is not English speech. If all English TTS routes fail, the agent stays silent and the VR status panel shows the failure state. Useful status values include:

- `Initializing...`
- `Preparing English voice`
- `Generating English voice`
- `Speaking English (Gemini)`
- `Synthesizing voice`
- `Playing voice`
- `Speaking English`
- `Missing Gemini key for English TTS`
- `No English TTS available`
- `No English TTS voice data`

For reliable English speech on Quest, put your Gemini API key in `Assets/StreamingAssets/gemini_key.txt` before building. If Gemini is missing or blocked, Android TTS may still work, but it depends on the Quest TTS engine and installed English voice data.

## Passthrough

Passthrough is enabled at runtime through the Meta/Oculus SDK bridge when the required SDK classes are available. The passthrough layer is configured as an underlay so scene content renders over the real-world background.

If passthrough does not appear:

- Make sure the build is running on Quest, not only in desktop Play Mode.
- Confirm Meta XR SDK is installed.
- Confirm OpenXR Meta Quest support and passthrough-related features are enabled.
- Check the Unity console/logcat for messages from `QuestRuntimeBridge`.

## Troubleshooting

### Build fails before launching

Fix all script compiler errors first. Unity cannot build/run while C# errors exist.

### App launches but does not enter VR

Check Android XR Plug-in Management, OpenXR loader setup, and Meta Quest support. Reopen Unity after changing active XR or input settings.

### Controller buttons do not respond

This project reads Quest input through OVRInput reflection and Unity XR `InputDevices`. Make sure the Meta XR SDK is installed and the app is running in the headset. The right trigger is intentionally reserved for recording; the left trigger selects menu choices only while the left menu is open.

### Agent has no sound

Watch the VR status panel `TTS:` line. `Speaking English (Gemini)` means Gemini generated real English audio and Unity is playing it from the agent. `Playing voice` means Android synthesized a WAV that Unity is playing. `Speaking English` means direct Android TTS is being used. `Missing Gemini key for English TTS` means the app cannot use the primary voice path. `No English TTS voice data` points to the Quest Android TTS engine/voice data.

### Microphone records but responses are wrong

Gemini must be configured for audio understanding. Without a key, the app records but falls back to local scripted replies.

## Editor Utilities

Use the Unity menu item `CloserXR > Build Sales Agent Starter` to rebuild the starter agent prefab/controller setup if needed.
