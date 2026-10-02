# Small Talk — prototype systems

## Quick start
1. Open the project; wait for packages to import (Netcode for GameObjects 2.13.3, Multiplayer Play Mode 2.0.2).
2. Menu **SmallTalk > Build Test Room**. This (re)creates `Prefabs/Player.prefab` and the grey-box room in `Assets/smallTalk.unity`.
   Safe to re-run; it only replaces `SmallTalk_TestRoom` and `NetworkManager`.
3. Press Play → **Host**.
4. More players on one computer: *Window > Multiplayer > Multiplayer Play Mode* → tick Player 2–4 → in each window click **Join** (127.0.0.1).
   Other computers on the same Wi-Fi: type the IP the host shows.

Controls: WASD, Shift sprint, Space jump, mouse look, **E** use, Esc frees the mouse.

## Test room
- **Door A** ← Switch 1 (Toggle). Basic shared-state check.
- **Door B** ← Switch 2A (spawn room) + Switch 2B (west room), both *Timed 3 s*, logic *All*, latches open.
  Two players in different rooms must count down and pull together.

## Reusable components (`Scripts/Interaction`)
| Component | What it does | Key settings |
|---|---|---|
| `Interactable` | Base class. Client presses E → server validates → `OnInteractServer`. | `maxUseDistance` |
| `NetworkSwitch` | Lever with synced on/off. Raises `Changed` on every machine. | `mode` Toggle/Timed, `onDuration`, `startLocked` |
| `NetworkDoor` | Synced door. Driven by switches (All/Any), manually (E), or by script `ServerSetOpen`. | `switches`, `logic`, `latchOpen`, `manualControl`, `openOffset` |

To make a new puzzle object: inherit from `Interactable`, keep its state in `NetworkVariable`s, change them only in server code.

## Networking model
Host = server + player 1; others are clients. Puzzle state is server-authoritative; each player's movement is owner-authoritative (`NetworkTransform` AuthorityMode = Owner).
Next steps: Unity Relay join codes (play over the internet) and Vivox voice.
