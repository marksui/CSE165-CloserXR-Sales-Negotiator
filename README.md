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
- VR status panel with red recording indicator and live Speaking/Ready/Gemini status
- Push-to-talk microphone input (WAV sent directly to Gemini for transcription + response)
- Gemini REST API integration with multi-turn conversation history (up to 10 turns)
- Android TTS voice output — the agent speaks aloud on device with procedural lip variation
- Local canned dialogue fallback when no API key is available
- Life insurance role-play lines for premiums, coverage, term-vs-whole questions, family protection, and closing

## Running The Demo

1. Open `CloserXR--Sales_Negotiator` in Unity.
2. Open `Assets/Scenes/SampleScene.unity`.
3. Connect a Meta Quest 2.
4. Build and run the scene to the headset.
5. Use the Quest controller inputs below to run the sales negotiation.

The floating VR panel shows the available controls, Gemini/microphone/room status, and the latest user and agent lines.

Without a Gemini key, use the preset Quest inputs:

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

1. **Inspector override** — paste directly into `GeminiSalesClient` on the prefab (quickest for a one-off test, clear it before committing)
2. **Environment variable** — recommended for Mac development
3. **StreamingAssets file** — required for Quest device builds (Android cannot read env vars)

Without any key, the demo runs on local canned dialogue automatically.

### Mac Development (env var)

```bash
cp .env.template .env
# open .env and replace the placeholder with your real key
source .env
# now open Unity from this same terminal session
open -a "Unity Hub"
```

The `.env` file is gitignored and stays on your machine only.

### Quest Device Builds (StreamingAssets file)

```bash
cp CloserXR--Sales_Negotiator/Assets/StreamingAssets/gemini_key.txt.template \
   CloserXR--Sales_Negotiator/Assets/StreamingAssets/gemini_key.txt
# open gemini_key.txt and replace the placeholder with your real key
```

`gemini_key.txt` is gitignored. Build the APK normally — the key is bundled into the build but never committed.

### Getting a Gemini API Key

Visit [Google AI Studio](https://aistudio.google.com/app/apikey) and create a free key for `gemini-2.5-flash`.

## Controls

- Quest index trigger: hold to record microphone input on Meta Quest 2
- Quest headset movement: controls the player view/head direction
- `A`: ask what kind of life insurance this is
- `B`: object to the premium
- `X`: ask how it protects your family
- `Y`: move forward
- Right thumbstick up: ask how much coverage is needed
- Right thumbstick down: reject the pitch
- Right thumbstick left: ask term vs whole life
- Right thumbstick right: say maybe / think about it

## Important Files

- `Assets/Scenes/SampleScene.unity`
- `Assets/Prefabs/SalesAgent.prefab`
- `Assets/Animations/SalesAgent.controller`
- `Assets/Scripts/SalesAgent/`
- `Assets/Scripts/SalesAgent/SalesAgentTTS.cs` — Android TTS wrapper with procedural lip variation
- `Assets/Scripts/SalesAgent/GeminiSalesClient.cs` — Gemini REST client with multi-turn history
- `Assets/Scripts/SalesAgent/SalesConversationManager.cs` — central conversation hub
- `Assets/Scripts/SalesAgent/SpatialRoomMapDemo.cs`
- `Assets/Scripts/SalesAgent/SalesAgentVRStatusPanel.cs`
- `Assets/Mixamo/`

## Architecture: LLM Pipeline

```
User speaks (trigger)
  └─► PushToTalkSpeechInput records WAV
        └─► GeminiSalesClient.GenerateFromAudio()
              ├─ sends WAV + conversation history to Gemini
              └─ receives response text
                    ├─► SalesIntentClassifier → intent
                    ├─► SalesDialogueGestureRouter → gesture (0.2 s delay)
                    ├─► SalesAgentPacer → distance update
                    └─► SalesAgentTTS.Speak()
                          ├─ Android TTS speaks the text aloud
                          └─ VariateTalkingSpeed coroutine → organic mouth movement
```

Conversation history is maintained across up to 10 turns so Gemini remembers what was already said. The opening pitch is seeded into history as the first model turn.

## Proposal Coverage

- Passthrough: bootstrapped at runtime through Meta XR components
- Spatial anchors: added on Android device builds
- Room mapping demo: `SpatialRoomMapDemo` reads Quest Guardian play-area geometry when available
- VR UI: `SalesAgentVRStatusPanel` shows Quest controls, Gemini mode, mic state (pulsing red dot while recording), room source, and speaking/ready status
- Conversation-aware gestures: user and agent text are classified into price pushback, rejection, agreement, uncertainty, and closing intents; gestures are delayed 0.2 s to sync with TTS startup
- Spatial proximity: the agent backs off for objections and moves closer when closing the sale
- Voice output: `SalesAgentTTS` drives Android TTS on device; `SetTalkingSpeed()` varies animator speed procedurally for lip rhythm
