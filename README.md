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
- VR status panel showing controls, system state, microphone state, room source, and recent dialogue
- Push-to-talk microphone input
- Gemini REST API integration
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

## Gemini API Key

For the Quest demo, select `SalesAgent` in the scene or open `Assets/Prefabs/SalesAgent.prefab`, find the `GeminiSalesClient` component in the Inspector, and paste the key into `Api Key Override`.

For a cleaner setup, set an environment variable before opening Unity:

```powershell
$env:GEMINI_API_KEY="your_api_key_here"
```

Do not commit a real API key into the Unity prefab or project files.

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
- `Assets/Scripts/SalesAgent/SpatialRoomMapDemo.cs`
- `Assets/Scripts/SalesAgent/SalesAgentVRStatusPanel.cs`
- `Assets/Mixamo/`

## Proposal Coverage

- Passthrough: bootstrapped at runtime through Meta XR components
- Spatial anchors: added on Android device builds
- Room mapping demo: `SpatialRoomMapDemo` reads Quest Guardian play-area geometry when available
- VR UI: `SalesAgentVRStatusPanel` shows Quest controls, Gemini mode, mic state, room source, and recent dialogue
- Conversation-aware gestures: user and agent text are classified into price pushback, rejection, agreement, uncertainty, and closing intents
- Spatial proximity: the agent backs off for objections and moves closer when closing the sale
