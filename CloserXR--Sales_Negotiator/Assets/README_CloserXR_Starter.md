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

## Hooking up Gemini later

Call these methods from the speech or LLM script:

```csharp
salesGestureRouter.RouteUserText(userTranscript);
salesGestureRouter.RouteAgentText(geminiResponseText);
salesGestureRouter.AgentStartedSpeaking();
salesGestureRouter.AgentStoppedSpeaking();
```
