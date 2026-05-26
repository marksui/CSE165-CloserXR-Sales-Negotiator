# CloserXR Sales Agent Starter

The Mixamo files have been copied into `Assets/Mixamo`.

## Build the starter prefab

1. Open this Unity project with Unity `2022.3.62f1`.
2. Wait for Unity to import the FBX files.
3. Run `CloserXR > Build Sales Agent Starter`.
4. Open `Assets/Scenes/SampleScene.unity` and press Play.

The menu command creates:

- `Assets/Animations/SalesAgent.controller`
- `Assets/Prefabs/SalesAgent.prefab`
- A `SalesAgent` instance in `Assets/Scenes/SampleScene.unity`

## Debug keys in Play Mode

- `T`: start talking
- `Y`: stop talking
- `W`: start walking / pacing
- `E`: stop walking / pacing
- `P`: point / close the deal
- `A`: argue defensively
- `D`: dismiss pushback
- `C`: celebrate
- `S`: sad idle
- `R`: reset to idle

## Meta Quest 2 Controls

- Right index trigger: hold to record microphone input
- `A`: ask what the product is
- `B`: object to the price
- `X`: ask for proof
- `Y`: accept the deal
- Right thumbstick up: ask about competitors
- Right thumbstick down: reject the pitch
- Right thumbstick left: ask about contracts
- Right thumbstick right: say maybe / think about it

## Running the proposal demo

`Assets/Prefabs/SalesAgent.prefab` has a `CloserXRDemoRuntime` component. In Play Mode it sets up:

- Meta Quest passthrough through `OVRManager` and `OVRPassthroughLayer`
- A spatial anchor component on device builds
- Push-to-talk microphone capture with Space or the Quest index trigger
- Gemini REST calls when `GEMINI_API_KEY` is available
- A local fallback pitch when no API key is set
- Keyword-driven body language and pacing/proximity

Without a Gemini key, use the HUD buttons in Play Mode to run a canned sales conversation: ask what the product is, object to price, reject, ask for proof, compare competitors, ask about contracts, or accept the deal.

To add a Gemini key, select `SalesAgent`, find `GeminiSalesClient` in the Inspector, and paste the key into `Api Key Override`.

## Hooking up Gemini directly

Call these methods from the speech or LLM script:

```csharp
salesGestureRouter.RouteUserText(userTranscript);
salesGestureRouter.RouteAgentText(geminiResponseText);
salesGestureRouter.AgentStartedSpeaking();
salesGestureRouter.AgentStoppedSpeaking();
```
