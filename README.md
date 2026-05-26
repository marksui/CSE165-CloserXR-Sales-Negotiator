# CloserXR - Sales Negotiator

CloserXR is a CSE 165 mixed reality prototype where a charismatic AI sales agent pitches a fake product to the user. The project focuses on conversation-aware body language: the avatar talks, points, paces, argues, dismisses objections, and celebrates based on the user's response.

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
- Sales agent avatar using Mixamo animations
- Animator state machine for talking, pacing, pointing, arguing, dismissing, and celebrating
- Basic spatial anchor support on device builds
- Push-to-talk microphone input
- Gemini REST API integration
- Local canned dialogue fallback when no API key is available
- Play Mode debug HUD for quick testing

## Running The Demo

1. Open `CloserXR--Sales_Negotiator` in Unity.
2. Open `Assets/Scenes/SampleScene.unity`.
3. Press Play.
4. Use the on-screen HUD to test the conversation.

Without a Gemini key, click the preset HUD buttons such as:

- `What are you selling?`
- `This is too expensive`
- `Can you prove it works?`
- `Yes, deal, sign me up`

## Gemini API Key

For quick testing, select `SalesAgent` in the scene or open `Assets/Prefabs/SalesAgent.prefab`, find the `GeminiSalesClient` component in the Inspector, and paste the key into `Api Key Override`.

For a cleaner setup, set an environment variable before opening Unity:

```powershell
$env:GEMINI_API_KEY="your_api_key_here"
```

Do not commit a real API key into the Unity prefab or project files.

## Controls

- `Space`: hold to record microphone input in the Unity Editor
- Quest index trigger: hold to record microphone input on Meta Quest 2
- `A`: ask what the product is
- `B`: object to the price
- `X`: ask for proof
- `Y`: accept the deal
- Right thumbstick up: ask about competitors
- Right thumbstick down: reject the pitch
- Right thumbstick left: ask about contracts
- Right thumbstick right: say maybe / think about it
- `T`: start talking animation
- `Y`: stop talking animation
- `W`: start pacing animation
- `E`: stop pacing animation
- `P`: pointing gesture
- `A`: arguing gesture
- `D`: dismissing gesture
- `C`: celebration gesture
- `S`: sad idle
- `R`: reset to idle

## Important Files

- `Assets/Scenes/SampleScene.unity`
- `Assets/Prefabs/SalesAgent.prefab`
- `Assets/Animations/SalesAgent.controller`
- `Assets/Scripts/SalesAgent/`
- `Assets/Mixamo/`
