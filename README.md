<div align="center">

# Intelligent Games Labs

The lab work for the Intelligent Games course, one Unity project that grows with each lab.
The newest is a procedural dungeon: BSP builds its rooms and corridors from a seed, so every
level is random but can be replayed exactly. A flood fill checks that it can be finished, the
enemies run the state machine from lab 5, and you win by finding the key and reaching the exit.

![Unity](https://img.shields.io/badge/Unity-6.3%20LTS-000000?logo=unity)
![URP](https://img.shields.io/badge/Render-URP-2196F3)
![C#](https://img.shields.io/badge/C%23-239120?logo=csharp&logoColor=white)
![Input System](https://img.shields.io/badge/Input%20System-New-orange)
![AI Navigation](https://img.shields.io/badge/AI%20Navigation-2.0-4CAF50)
![FSM](https://img.shields.io/badge/AI-Finite%20State%20Machine-E91E63)
![Behavior Tree](https://img.shields.io/badge/AI-Behavior%20Tree-9C27B0)
![PCG](https://img.shields.io/badge/PCG-Seeded%20Spawner-FF9800)

</div>

---

## Labs in this repo

| Lab | Scene | Topic |
|---|---|---|
| 1 to 3 | `Assets/Scenes/NPCDetector.unity` | Perception, a NavMesh guard, and a steering farmhand |
| 4 | `Assets/Scenes/Praktikum4/P4A_AStarGrid.unity` | A* written by hand on a grid, with the open and closed sets drawn on the floor |
| 4 | `Assets/Scenes/Praktikum4/P4B_NavMeshChase.unity` | NavMesh chasing, repathing, and `NavMeshObstacle` carving |
| 5 | `Assets/Scenes/Praktikum5/Praktikum05_FSM.unity` | Enemy AI as a finite state machine |
| 6 | `Assets/Scenes/Praktikum6/P06_BehaviorTree.unity` | Enemy AI as a behavior tree, compared with utility AI |
| 9 | `Assets/Scenes/Praktikum9/P09_PCG_SmartSpawner.unity` | Procedural content generation: a seeded, weighted, validated spawner |
| 10 | `Assets/Scenes/Praktikum10/P10_BSP_Dungeon.unity` | Procedural dungeon: BSP rooms and corridors, BFS validation, repair, and a playable start-to-goal run |

## Running it from source

Clone the repo and open the folder with Unity 6.3 LTS. Unity rebuilds the `Library` folder on first launch, which takes a few minutes. Open any scene from the table above and press Play.

The scenes for labs 4 to 10 are generated. The **Praktikum 4**, **Praktikum 5**, **Praktikum 6**, **Praktikum 9** and **Praktikum 10** menus in the editor rebuild them from scratch, so a scene broken by an experiment is one click from new. Labs 4 to 9 bake their NavMesh in the menu, and lab 10 bakes it at runtime for each dungeon. Rebuilding overwrites the scene file, including anything you changed in it by hand.

---

## Lab 10: Procedural dungeon with BSP

The level itself is now generated. Every time the scene starts, Binary Space Partitioning builds a dungeon of rooms and corridors from a seed. The same seed and settings always give the same dungeon, and a different seed gives a different one. A flood fill then checks that everything required can be reached, and repairs the layout if it cannot. Rebuild the scene and its prefabs from **Praktikum 10 → Build Scene - BSP Dungeon**. The dungeon is not stored in the scene.

Your goal: find the key, which unlocks the exit, then walk into the exit. The exit stays red until you have the key. Enemies guard the rooms and run the lab 5 finite state machine.

### Controls

| Input | Action |
|---|---|
| WASD | Walk |
| Left click | Strike whatever stands just in front of you |
| R | Regenerate with the same seed (also restarts after you lose) |
| N | Regenerate with a random seed |
| T | Validation test: cut a corridor on the required path, then repair it |
| V | Show or hide the required path, the safe zone ring, and the BSP partitions on the minimap |
| M | Switch between the follow camera and the full map |

The camera is fixed top-down and the cursor stays free, so the HUD buttons work at any time. The buttons are **Regenerate (R)**, **Seed Acak (N)**, **Seed -1**, **Seed +1**, **Uji Validasi (T)**, **Jalur (V)** and **Peta Penuh (M)**. Jumping and flight are off in this scene, so you cannot cross the walls.

### How a dungeon is generated

[`BspDungeonGenerator.cs`](Assets/Scripts/Praktikum10/BspDungeonGenerator.cs) does all the work on a grid and creates no objects. It returns a layout, which [`DungeonSpawner.cs`](Assets/Scripts/Praktikum10/DungeonSpawner.cs) then builds from modular prefabs. One cell is 2 × 2 m.

1. **Partition.** Split the map in two, then split each part again, down to `maxSplitDepth`. A part that is clearly longer one way is cut across its long side, and no part gets smaller than `minLeafSize`.
2. **Rooms.** Each leaf partition gets one room of random size and position, inset by `roomPadding`. A leaf too small for `roomMinSize` stays empty.
3. **Corridors.** At every BSP node, take the closest pair of rooms from its two children and join them with an L-shaped corridor. This connects the whole tree. `extraConnections` then adds corridors outside the tree, which create loops.
4. **Start, key and goal.** A BFS from a random room finds the farthest room, which becomes the start. A second BFS from there finds the goal. The key goes in one of the two rooms that make the longest detour between them.
5. **Objects**, in order: props, enemies, items, safe points. A random cell is rejected if it breaks a rule, and redrawn up to `maxPlacementAttempts` times:
   - enemies and props stay out of the safe zone, and enemies out of the start room,
   - props, enemies and items stay off the required path (start to key, then key to goal),
   - everything keeps its distance from what is already placed,
   - a prop is refused if a flood fill shows it would cut off any floor.
6. **Validate.** [`DungeonValidator.cs`](Assets/Scripts/Praktikum10/DungeonValidator.cs) runs a BFS from the start and checks the key, the goal, every room, every floor cell and every object.
7. **Repair or retry.** A failed layout gets new corridors joining each cut-off area to the main one, then is validated again. If it still fails, the generator starts over with a derived seed, up to `maxGenerationAttempts`. A map too small to fit 3 rooms fails every attempt, and the previous level stays in place.

Each stage draws from its own `System.Random`, all derived from the seed: layout, start and goal, props, enemies, items, and safe points. Changing the enemy count therefore does not reshape the dungeon. `UnityEngine.Random` is used only to pick a new seed for N, never for generation.

The spawner then works in a fixed order: build the floor and walls, bake the NavMesh at runtime, move the player and exit, and only then spawn the enemies, because their `NavMeshAgent` needs a NavMesh when it starts. Enemies are instantiated under an inactive parent first, so `EnemyPerception.SetTarget` and `EnemyFSM.SetRoute` run before they wake up.

### What gets spawned

| Kind | Type | Notes |
|---|---|---|
| Enemy | Skeleton (FSM) | 100 HP, patrols its own room and the rooms connected to it, never routed through the start room |
| Item | Key | Exactly one per dungeon, +25 score, opens the exit |
| Item | Coin | 70% of items, +10 score |
| Item | Potion | 30% of items, +5 score, heals 30 HP |
| Prop | Crate, two barrels | Blocks the way and blocks enemy sight |
| Safe point | Green pad | Up to two, in rooms other than the start and goal. Enemies flee here on low health |

### Parameters

All of these sit on **Dungeon Systems → Dungeon Spawner** in the Inspector. Those marked HUD can also be changed while playing, which regenerates with the same seed.

| Parameter | Default | HUD | Effect on the level |
|---|---|---|---|
| `mapWidth`, `mapHeight` | 36 × 36 | yes | More space and longer paths. The HUD changes both together |
| `maxSplitDepth` | 4 | yes | Deeper splits allow more rooms |
| `minLeafSize` | 9 | yes | Smaller partitions allow more, smaller rooms |
| `roomMinSize`, `roomMaxSize` | 4, 9 | yes | Tight rooms or large halls |
| `roomPadding` | 1 | yes | Smaller rooms with more wall between them |
| `corridorWidth` | 2 | yes | Narrow chokepoints or wide passages |
| `extraConnections` | 1 | yes | More loops and alternative routes |
| `enemyCount`, `itemCount`, `propCount` | 6, 8, 10 | yes | Object density. The layout does not change |
| `safeRadius` | 5 | yes | Cells around the start kept free of enemies and props |
| `minSpacing` | 3 | no | Distance between objects, in cells |
| `potionChance` | 0.3 | no | Share of items that are potions |
| `maxPlacementAttempts` | 40 | no | Draws per object before it is skipped |
| `maxGenerationAttempts` | 5 | no | Whole-dungeon retries before giving up |

### Debug view

The HUD shows the seed, a layout hash, the attempt count and repair result, the validation checks, objects spawned out of those requested, rejected positions split by reason (safe zone, path, spacing, blocking), and the log of every attempt. On the full map the panel shrinks to the seed, hash, validation result and object counts, so it does not cover the dungeon. Every generation is also logged to the Console.

The minimap shows the layout from the same data:

- grey: rooms, darker grey: corridors, brown: props,
- light blue line: start to key, yellow line: key to goal,
- green: start, cyan: key, magenta: goal, red: enemy spawns, yellow: coins, pink: potions,
- white: the player, brown outlines: BSP partitions.

In the game view, V also draws the two path lines on the floor and a green ring for the safe zone. Each enemy shows its state as a coloured marker above its head: green for Patrol, yellow for Chase, red for Attack, blue for Flee.

Gizmos in the Scene view, in Play mode:

- orange boxes: BSP partitions,
- green boxes: rooms,
- red spheres: enemy spawns,
- blue cubes: items.

### Experiments

**Praktikum 10 → Run Generator Tests** generates 200 seeds (1 to 200) for each of seven configurations and checks every one, without pressing Play. Each seed is generated twice to compare the hashes, and once more with a corridor cut. A separate check outside the generator re-verifies the placement rules. Results go to the Console.

| Configuration | Changed from default | Valid | Unique layouts | Not repeatable | Rule violations | Avg rooms | Avg attempts | Enemies spawned | Cuts repaired |
|---|---|---|---|---|---|---|---|---|---|
| Default | none | 200/200 | 200 | 0 | 0 | 9.3 | 1.00 | 1156/1200 | 199/199 |
| Small map | 24 × 24, 3 enemies, 3 items, 4 props | 200/200 | 200 | 0 | 0 | 4.0 | 1.00 | 394/600 | 86/86 |
| Large map | 64 × 64, depth 6 | 200/200 | 200 | 0 | 0 | 27.1 | 1.00 | 1200/1200 | 200/200 |
| Narrow corridors | width 1, no extra corridors | 200/200 | 200 | 0 | 0 | 9.3 | 1.00 | 1158/1200 | 200/200 |
| Wide corridors | width 4, 3 extra corridors | 200/200 | 200 | 0 | 0 | 9.3 | 1.00 | 1134/1200 | 182/182 |
| Large rooms | rooms 7 to 14, leaf 14 | 200/200 | 200 | 0 | 0 | 4.0 | 1.00 | 1157/1200 | 72/72 |
| Crowded | 20 enemies, 20 items, 30 props, spacing 2 | 200/200 | 200 | 0 | 0 | 9.3 | 1.00 | 2568/4000 | 199/199 |

"Cuts repaired" counts only seeds where the cut really disconnected something. The count is lower when a seed could not be cut, for example because a loop keeps the goal reachable. Those seeds stay valid without a repair.

The number of rooms follows the map size and partition settings, not the corridors: narrow and wide corridors both average 9.3 rooms. Enemies fall short of the request when rooms are few or crowded, as the rules leave too few free cells. A dungeon stays valid in that case, and the HUD reports the shortfall.

A repair in practice, seed 101 with **Uji Validasi (T)**: the cut leaves the goal unreachable, with 54 floor cells and 1 room cut off, and 1 object on them. Repair carves 6 new corridor cells, and the same attempt then passes.

### Scripts

| File | Role |
|---|---|
| [`DungeonConfig.cs`](Assets/Scripts/Praktikum10/DungeonConfig.cs) | Seed and every generator parameter, kept consistent by `Clamp` |
| [`DungeonLayout.cs`](Assets/Scripts/Praktikum10/DungeonLayout.cs) | The dungeon as plain data: grid, rooms, object plans, validation result, layout hash |
| [`BspDungeonGenerator.cs`](Assets/Scripts/Praktikum10/BspDungeonGenerator.cs) | BSP, rooms, corridors, start, key and goal, object placement, repair and retry |
| [`DungeonValidator.cs`](Assets/Scripts/Praktikum10/DungeonValidator.cs) | BFS distances, shortest paths, reachability and placement checks |
| [`DungeonSpawner.cs`](Assets/Scripts/Praktikum10/DungeonSpawner.cs) | Builds the prefabs, bakes the NavMesh, wires enemies to the scene, R, N and T keys |
| [`DungeonHUD.cs`](Assets/Scripts/Praktikum10/DungeonHUD.cs) | uGUI debug panel, minimap, parameter and action buttons, goal banner |
| [`DungeonDebugOverlay.cs`](Assets/Scripts/Praktikum10/DungeonDebugOverlay.cs) | Path lines, safe zone ring, Scene view Gizmos |
| [`TopDownCamera.cs`](Assets/Scripts/Praktikum10/TopDownCamera.cs) | Follow camera and full map view |
| [`Editor/Praktikum10SceneBuilder.cs`](Assets/Scripts/Praktikum10/Editor/Praktikum10SceneBuilder.cs) | Builds the scene and the floor, wall, prop, enemy, coin and safe point prefabs |
| [`Editor/Praktikum10GeneratorTests.cs`](Assets/Scripts/Praktikum10/Editor/Praktikum10GeneratorTests.cs) | The seven-configuration test run |

Earlier labs are reused rather than rewritten:

- **Lab 5:** `EnemyFSM`, `EnemyPerception`, `Health`, `SafePoint`, `EnemyVisualFeedback`, and the animator at `Assets/Scenes/Praktikum5/EnemyAnimation.controller`. `EnemyFSM` gained `SetRoute`, for enemies created at runtime.
- **Lab 6:** `WorldHealthBar`.
- **Lab 9:** `Collectible`, `ExitGate`, `P9GameGoal`, `HudPressButton`, and the key, potion and coin prefabs. `P9GameGoal` gained `requiredItemType`, so one item can open the exit. The coin model was wider than a cell, so this lab saves a smaller variant.
- **Player:** `PlayerController` gained `allowFlight`, which this scene turns off.

Build the lab 9 scene once first if its prefabs are missing, because the lab 10 builder needs them for the items.

---

## Lab 9: Procedural content generation, the smart spawner

Enemies and items are no longer placed by hand. Every time the scene starts, a spawner picks their types and positions from a seed. The same seed in the same arena always gives the same layout, and a different seed gives a different one. The arena, obstacles, player, exit, waypoints and safe points are fixed. Only what the spawner creates changes. Rebuild the scene from **Praktikum 9 → Build Scene - PCG Smart Spawner**.

The spawned enemies are the lab 6 behavior tree enemies, in three types. Your goal: pick up every item, then walk into the exit. The exit stays red until the last item is taken.

### Controls

| Input | Action |
|---|---|
| WASD | Walk |
| Mouse | Orbit the camera |
| Left click | Strike whatever stands just in front of you |
| R | Regenerate with the same seed (also restarts after you lose) |
| N | Regenerate with a random seed |
| Esc | Free the cursor so you can press the HUD buttons |

The HUD buttons are **Regenerate (R)**, **Seed Acak (N)**, **Seed -1**, **Seed +1** and **Run Experiments**. They react when the mouse button goes down, because the camera grabs the cursor again on the same click.

### How a layout is generated

[`SpawnGenerator.cs`](Assets/Scripts/Praktikum9/SpawnGenerator.cs) does all the work and creates no objects. It returns a plan, which [`SmartSpawner.cs`](Assets/Scripts/Praktikum9/SmartSpawner.cs) then instantiates. For each enemy, then each item:

1. **Weighted random type.** Draw `r` in `[0, total weight)` and subtract the weights one by one until `r` drops below zero. With weights 60:30:10, the first type comes up 60% of the time.
2. **Random position** inside the spawn area (36 × 36 m, centred on the spawner).
3. **Validation**, cheapest check first:
   - at least `playerSafeRadius` from the player,
   - outside the exit's keep-out circle,
   - at least `minSpacing` from everything already placed,
   - no obstacle inside a capsule of radius `obstacleClearance` (`Physics.CheckCapsule` on the Obstacle layer).
4. A rejected position is redrawn, up to `maxAttempts` times. When the attempts run out, the object is skipped and counted as a failed spawn.

Each concern draws from its own `System.Random`, all derived from the seed: position, type, rotation and legendary. Turning random rotation off therefore does not move anything. `UnityEngine.Random` is never used for generation, because other scripts share it and would shift the sequence.

Enemies are instantiated under an inactive parent first. A prefab cannot reference the player, waypoints or safe points in the scene, so the spawner fills them in (`EnemyPerception.SetTarget`, `EnemyBrain.SetRoute`) before the enemy wakes up. Only then does it move the enemy to the active parent.

All parameters sit on **PCG Systems → Smart Spawner** in the Inspector: seed, enemy and item counts, area size, safe radius, spacing, clearance, max attempts, type weights, and the legendary settings.

### What gets spawned

| Kind | Type | Weight | Notes |
|---|---|---|---|
| Enemy | Grunt (skeleton) | 60 | 100 HP, balanced |
| Enemy | Brute (charred) | 30 | 160 HP, bigger, slow, hits hard |
| Enemy | Scout (robot) | 10 | 60 HP, small, fast, flees early |
| Item | Coin | 60 | +10 score |
| Item | Potion | 30 | +5 score, heals 30 HP |
| Item | Key | 10 | +25 score |
| Item | Legendary chest | 8% per item, at most 1 | +100 score, heals 50 HP, glows, only 12 m or more from the player |

### Development features

| Feature | Where |
|---|---|
| Random rotation | Every object gets a random Y rotation from its own random stream. Toggle it with `randomRotation`. |
| Object type statistics | The HUD lists how many of each enemy and item type were spawned, the target percentage, and how many of each you have collected. |
| Regenerate UI buttons | Same seed, random seed, seed −1 or +1, and run the experiments, all without the keyboard. |
| Legendary item | A rare glowing chest. It has its own chance, its own cap, and a larger safe distance, so it is always a detour. |

There is also a small seed history in the HUD.

### Debug view

The HUD shows the seed, a layout hash, enemies and items spawned out of those requested, failed spawns, and rejected positions split by reason (player, exit, spacing, obstacle). Every generation is also logged to the Console.

Gizmos in the Scene view:

- yellow box: the spawn area,
- green disc: the player's safe zone,
- orange ring: the legendary minimum distance,
- red ring: the exit keep-out.

In Play mode the Scene view also shows each spawned object with a ring of half the minimum spacing, and a small red cube wherever a candidate position was rejected.

### Experiments

**PCG Systems → Experiment Runner** runs every test from the assignment in one go, either in Play mode (the **Run Experiments** button) or in Edit mode (**Praktikum 9 → Run Experiments**). It only plans layouts and creates nothing. Results go to the Console and to [`Assets/Scenes/Praktikum9/Experiments`](Assets/Scenes/Praktikum9/Experiments) as `P09_ExperimentResults.csv` and `.md`.

The base settings for every run are 8 enemies, 15 items, a 36 × 36 m area, safe radius 5, spacing 1.5, 30 max attempts and enemy weights 60:30:10. Each test changes only one of them. "Rejected" counts candidate positions that failed validation. "Failed" counts objects that ran out of attempts.

**1. Different seeds**

| Seed | Enemy | Item | Failed | Rejected (player / exit / spacing / obstacle) | Enemy types | Item types | Layout hash |
|---|---|---|---|---|---|---|---|
| 100 | 8/8 | 15/15 | 0 | 5 (1 / 2 / 1 / 1) | Grunt 6, Brute 1, Scout 1 | Coin 4, Potion 6, Key 4, Legendary 1 | `F5BEA89B` |
| 200 | 8/8 | 15/15 | 0 | 11 (3 / 2 / 2 / 4) | Grunt 7, Brute 1, Scout 0 | Coin 9, Potion 4, Key 1, Legendary 1 | `3F4109B1` |
| 300 | 8/8 | 15/15 | 0 | 10 (1 / 1 / 2 / 6) | Grunt 5, Brute 2, Scout 1 | Coin 8, Potion 5, Key 1, Legendary 1 | `85A72B68` |

Each seed gives a different layout and a different mix of types. All three still fill every slot.

**2. Reproducibility, seed 500 three times**

| Run | Enemy | Item | Rejected | Enemy types | Layout hash |
|---|---|---|---|---|---|
| 1 | 8/8 | 15/15 | 8 | Grunt 2, Brute 6, Scout 0 | `62609F50` |
| 2 | 8/8 | 15/15 | 8 | Grunt 2, Brute 6, Scout 0 | `62609F50` |
| 3 | 8/8 | 15/15 | 8 | Grunt 2, Brute 6, Scout 0 | `62609F50` |

The hashes match, so all three layouts are identical, down to positions and rotations. This holds only while the arena and the player's start position stay the same, which is why the spawner puts the player back at the start before each generation.

**3. Player safe radius, seed 500**

| Safe radius | Enemy | Item | Failed | Rejected near player | Rejected total |
|---|---|---|---|---|---|
| 3 | 8/8 | 15/15 | 0 | 0 | 7 |
| 5 | 8/8 | 15/15 | 0 | 1 | 8 |
| 8 | 8/8 | 15/15 | 0 | 4 | 11 |

A bigger safe radius rejects more positions near the player and pushes everything outward. The layout changes, but the types do not, because types come from their own random stream. The arena is large compared with an 8 m circle, so nothing fails to spawn.

**4. Minimum spacing, seed 500**

| Min spacing | Enemy | Item | Failed | Rejected for spacing | Rejected total |
|---|---|---|---|---|---|
| 0.5 | 8/8 | 15/15 | 0 | 0 | 7 |
| 1.5 | 8/8 | 15/15 | 0 | 3 | 8 |
| 3 | 8/8 | 15/15 | 0 | 13 | 18 |

Spacing has the strongest effect of any parameter: going from 1.5 to 3 more than doubles the rejected positions, and objects spread out more evenly. With more objects or a smaller area, the attempts would start running out and the failed count would rise.

**5. Enemy weights, 20 generations (seeds 1 to 20, 160 enemies)**

| Weights | Grunt | Brute | Scout |
|---|---|---|---|
| 60:30:10 | 92 (57.5%, target 60%) | 49 (30.6%, target 30%) | 19 (11.9%, target 10%) |
| 33:33:34 | 48 (30.0%, target 33%) | 53 (33.1%, target 33%) | 59 (36.9%, target 34%) |

Over many generations, the share of each type lands within about 3 points of its weight. A single generation can stray far: seed 500 rolled 2 Grunts and 6 Brutes under 60:30:10. Weights set a probability, not a quota. Changing the weights leaves the positions alone, so the rejected counts are the same for both rows.

### Scripts

| File | Role |
|---|---|
| [`SpawnGenerator.cs`](Assets/Scripts/Praktikum9/SpawnGenerator.cs) | `SpawnConfig`, `SpawnEntry`, weighted pick, position validation, `SpawnReport` with the layout hash |
| [`SmartSpawner.cs`](Assets/Scripts/Praktikum9/SmartSpawner.cs) | Instantiates the plan, wires spawned enemies to the scene, R and N keys, Gizmos |
| [`SpawnExperimentRunner.cs`](Assets/Scripts/Praktikum9/SpawnExperimentRunner.cs) | Runs the five tests, writes CSV and Markdown |
| [`SpawnerHUD.cs`](Assets/Scripts/Praktikum9/SpawnerHUD.cs) | uGUI debug panel, type statistics, buttons, win and lose banner |
| [`HudPressButton.cs`](Assets/Scripts/Praktikum9/HudPressButton.cs) | A button that fires on press, so it works with the locked camera cursor |
| [`Collectible.cs`](Assets/Scripts/Praktikum9/Collectible.cs) | Item pickup by distance, score, healing, bobbing and glow |
| [`P9GameGoal.cs`](Assets/Scripts/Praktikum9/P9GameGoal.cs) | Collect everything, then reach the exit. Lose when your health runs out |
| [`ExitGate.cs`](Assets/Scripts/Praktikum9/ExitGate.cs) | Exit pad, red while locked and green once open |
| [`Editor/Praktikum9SceneBuilder.cs`](Assets/Scripts/Praktikum9/Editor/Praktikum9SceneBuilder.cs) | Builds the arena, the seven prefabs and three personalities, and bakes the NavMesh |

The lab 6 enemy animator is reused from `Assets/Scenes/Praktikum6/Animation/EnemyBT.controller`. Build the lab 6 scene once first, or the spawned enemies will slide around without animating.

---

## Lab 6: Enemy behavior tree

The skeleton from lab 5, with its state machine swapped for a behavior tree. The arena, waypoints and safe points are the same. Rebuild the scene from **Praktikum 6 → Build Scene - Behavior Tree AI**.

### Controls

| Input | Action |
|---|---|
| WASD | Walk |
| Mouse | Orbit the camera |
| Left click | Strike whatever stands just in front of you, 20 damage |
| M | Switch the enemy between behavior tree and utility AI |
| 1, 2, 3 | Personality: Balanced, Aggressive, Coward |
| K | Wound the enemy by 15 |
| L | Kill the enemy outright |
| H | Restore your health |
| Tab | Hide or show the debug panels |
| R | Restart the scene |

### The tree

```
Root Selector
├── Flee Sequence     Health Low? → Flee
├── Attack Sequence   Can See Player? → In Attack Range? → Attack Or Wait
│                                                         ├── Cooldown → Attack
│                                                         └── Wait Cooldown
├── Chase Sequence    Can See Player? → Chase
├── Search Sequence   Has Last Seen? → Search Last Seen
└── Patrol
```

The tree is evaluated from the root every frame, so the first child of the root selector that does not fail wins. That order is the priority: Flee, then Attack, then Chase, then Search, then Patrol. A wounded skeleton runs even with you standing in front of it, because the flee branch is checked before the attack branch ever gets a turn.

The cooldown decorator fails while it is cooling down and does not run its child at all, so a hit lands at most once per cooldown. Without the `Wait Cooldown` fallback, that failure would drop the skeleton into the chase branch between hits and it would walk into you. With it, the skeleton holds its ground and keeps facing you.

Search is the bonus branch. When the skeleton loses you, it walks to where it last saw you and turns on the spot for a few seconds before it goes back to patrolling. A hit from behind gives it the same clue.

The framework lives in [`Assets/Scripts/Praktikum6/BehaviorTree`](Assets/Scripts/Praktikum6/BehaviorTree): `BTNode`, `SelectorNode`, `SequenceNode`, `ConditionNode`, `ActionNode`, `DecoratorNode` and `CooldownDecorator`. The tree itself is built in `EnemyBrain.BuildTree`.

### Utility AI

[`UtilityScoring.cs`](Assets/Scripts/Praktikum6/UtilityScoring.cs) gives every action a score from 0 to 1 each frame, and the highest score wins. The scores are always computed, so the top-right panel shows what utility AI would pick next to what the tree actually picked. Press M and the utility scores take over, running the very same action nodes.

| Action | Score |
|---|---|
| Flee | 0.9 to 1 at or below the flee threshold, falling off steeply as health rises above it |
| Attack | 0.9 when you are seen and in range, scaled down to 60% as its own health drops |
| Chase | 0.55 to 0.8 when you are seen, higher the further away you are |
| Search | 0.4 while it has a last seen position to check |
| Patrol | 0.1, always |

Each score is then multiplied by the personality's weight, and the action already running gets a small bonus so the choice does not flicker. The two methods disagree in interesting places. An Aggressive skeleton at 10% health with you in reach scores Attack 0.77 against Flee 0.65 and keeps swinging, while the tree, with the same numbers, always flees.

### Personalities

| | Balanced | Aggressive | Coward |
|---|---|---|---|
| Flees at | 30% | 15% | 50% |
| Chase speed | 4.5 | 5.5 | 3.5 |
| Attack cooldown | 1.5 s | 1 s | 2 s |
| Search time | 4 s | 6 s | 2 s |
| Utility weights (attack, chase, flee) | 1, 1, 1 | 1.25, 1.2, 0.7 | 0.8, 0.75, 1.3 |

They are ScriptableObjects in `Assets/Scenes/Praktikum6/Personalities`. The tree stays the same; only its thresholds and the utility weights change.

### Reading it while it runs

| Where | What you see |
|---|---|
| Top-left panel | Current action, mode, personality, both health bars, the perception checks and the attack cooldown |
| Tree panel below it | The whole tree, live. Green for Success, yellow for Running, red for Failure, grey for nodes not reached this frame |
| Top-right panel | The utility score of every action, and whether the tree and utility AI agree |
| Inspector, on `Enemy_Skeleton (Behavior Tree)` | `Current Action`, `Previous Action`, `Active Branch` and the utility scores |
| Console | One colour-coded line per action change with the reason, plus a line for every hit |
| Over each head | A health bar that faces the camera. A white trail shows the last hit, and a blue tick on the enemy's bar marks its flee threshold |
| Ball over the skeleton | Green Patrol, yellow Chase, red Attack, purple Search, blue Flee |

The gizmos are the same as lab 5, plus a purple cross on the last seen position while the skeleton is searching.

### Animation

The skeleton still borrows the Supersoldier's Idle and Running clips. The scene builder also generates an attack clip from Humanoid muscle curves, a wind-up and a straight punch with the right arm, and plays it on an upper-body layer masked to the torso and right arm, so the legs keep walking while the arm swings. `EnemyBrainVisual` sets the `Speed`, `Attack` and `Action` parameters.

### Scripts

| File | Job |
|---|---|
| `BehaviorTree/*.cs` | The behavior tree framework |
| `EnemyBrain.cs` | Builds the tree, runs it or the utility AI, and holds the actions |
| `UtilityScoring.cs` | The utility scores |
| `EnemyPersonality.cs` | The personality asset |
| `EnemyBrainVisual.cs` | Animator parameters, the action ball, the lunge and the fall |
| `WorldHealthBar.cs` | The health bars over each head |
| `BTDebuggerHUD.cs` | The panels, the tree view and the test keys |
| `Editor/Praktikum6SceneBuilder.cs` | Builds the scene, the personalities, the animator and the NavMesh |

Perception, health, safe points and the player's strike are reused from lab 5.

---

## Lab 5: Enemy FSM

A skeleton guards a walled field with four waypoints and two safe points. It runs on a finite state machine with five states.

### Controls

| Input | Action |
|---|---|
| WASD | Walk |
| Mouse | Orbit the camera |
| Left click | Strike whatever stands just in front of you, 20 damage, twice a second at most |
| L | Kill the enemy outright |
| H | Restore your health |
| R | Restart the scene |

Click the Game view once first, so the camera takes the cursor. Turn on the **Gizmos** button in the Game view too, otherwise the view cone and ranges stay hidden.

### States and transitions

| From | Condition | To |
|---|---|---|
| Patrol | You are seen | Chase |
| Chase | You come within `attackRange` (2 m) | Attack |
| Chase | It has not seen you for `loseSightTime` (3 s) | Patrol |
| Attack | You move beyond `attackExitRange` (3 m) | Chase |
| Patrol, Chase, Attack | Its health falls to `fleeThreshold` (30%) | Flee |
| Flee | It reaches a safe point | Patrol |
| Any state | Its health reaches 0 | Dead |

The order of the checks matters as much as the table. Every frame [`EnemyFSM.cs`](Assets/Scripts/Praktikum5/EnemyFSM.cs) asks about death first, then about fleeing, and only then hands control to the active state. Dead therefore wins on the very frame health hits zero, and an enemy that just took a fatal blow cannot land one last strike.

| State | What the skeleton does |
|---|---|
| Patrol | Walks the waypoint loop at speed 2, pausing a second at each point |
| Chase | Runs at you at speed 4.5. Once it loses sight of you it heads for the last place it saw you |
| Attack | Stops, turns to face you, and hits for 10 every 1.5 seconds |
| Flee | Sprints at speed 6 to a safe point, then heals to 70% there |
| Dead | Disables its agent and its senses, and falls over |

Attack is left at `attackExitRange`, not at `attackRange`. With a single threshold, a player standing right on the 2 metre line would flip the enemy between Chase and Attack every frame. The extra metre gives it room to settle.

Flee picks between the two safe points by how far each one takes it from you, less how far it has to run. That keeps it from choosing a refuge whose path goes straight through you. The heal on arrival lifts it clear of the flee threshold, otherwise it would reach safety, start patrolling, and flee again on the next frame.

### How it sees you

[`EnemyPerception.cs`](Assets/Scripts/Praktikum5/EnemyPerception.cs) uses the same three tests as the guard from lab 2, cheapest first.

1. **Vision range.** Are you within 12 metres?
2. **Field of view.** Are you inside the 110 degree cone ahead of it?
3. **Line of sight.** Does a ray from its eyes, 1.6 metres up, reach you, a metre off the ground, without hitting anything on the `Obstacle` layer?

The stone walls are three metres tall so you can hide behind them. The perception script only reports what it sees; every decision lives in the FSM.

### Reading it while it runs

| Where | What you see |
|---|---|
| Console | One colour-coded line per transition, with the reason, plus a line for every hit |
| Inspector, on `Enemy_Skeleton (FSM)` | `Current State`, `Previous State` and `Last Transition` |
| Top-left panel | Current state, both health bars, the attack cooldown, and the flee destination |
| Over each head | A health bar, with the enemy's state written above its own |
| Ball over the skeleton | Green for Patrol, yellow Chase, red Attack, blue Flee, grey Dead. It flashes white when the skeleton is hit |

| Gizmo | Meaning |
|---|---|
| Large circle and shaded cone | Vision range and field of view. Yellow while it cannot see you, red once it can |
| Line to you | Green when line of sight is clear, red when an obstacle blocks it |
| Solid red ring | Attack range |
| Thin orange ring | Attack exit range |
| Cyan loop | The patrol route |
| Green rings | Safe points |

### About the skeleton

The Synty skeleton ships without animations. Its rig is Humanoid, so it borrows the Supersoldier's Idle and Running clips by retargeting, blended by the agent's speed in a controller the scene builder generates. There is no attack or death clip, so those are done in code. The model lunges forward on each hit and topples backwards when it dies.

### Scripts

| File | Job |
|---|---|
| `EnemyFSM.cs` | The state machine, its transitions, the console log and the range gizmos |
| `EnemyPerception.cs` | Vision range, field of view, line of sight |
| `Health.cs` | Health for both sides, with damaged, healed and died events |
| `SafePoint.cs` | A flee destination that heals on arrival |
| `EnemyVisualFeedback.cs` | Animator speed, the state ball, the lunge and the fall |
| `PlayerMeleeAttack.cs` | The player's left-click strike |
| `FSMDebugHUD.cs` | The on-screen panel, the overhead bars and the test keys |
| `Editor/Praktikum5SceneBuilder.cs` | Builds the scene, the enemy animator and the NavMesh |

---

## Labs 1 to 3: Autonomous steering agent

A farmhand that finds its way around the island with no map at all,
sharing it with a guard who hunts you by sight and loses you when you break cover.

### Controls

| Input | On the ground | In flight |
|---|---|---|
| WASD or arrows | Walk | Steer |
| Mouse | Orbit the camera | Orbit the camera |
| Space | Jump | Gain height |
| Space twice, quickly | Take off | Cancel and drop |
| Shift | — | Descend |

A flight runs for 10 seconds. Touching the ground ends it early, and the next one waits 3 seconds.

Tapping Space twice to climb faster will cancel the flight instead, since that gesture is also the cancel command. Leave a gap between taps.

### How the farmhand steers

[`SteeringAgent.cs`](Assets/Scripts/SteeringAgent.cs) is this lab, carried in the scene by `Pengelola Sawit`. It walks the same island as the guard from the previous lab and shares none of its code. It never touches the NavMesh. The guard asks Unity for a path and follows it; the farmhand knows only what its sensors report this frame.

Every frame it works out the velocity it wants, subtracts the one it has, and divides by `responseTime` to get a steering force. That force is capped by `maxForce`, added to velocity, capped by `maxSpeed`, and applied to its position.

| State | When | What it does |
|---|---|---|
| Wander | No target | Drifts a point around a circle projected ahead of it and steers at that point |
| Seek | Target beyond `slowRadius` | Runs at it at full speed |
| Arrive | Target inside `slowRadius` | Scales speed down with distance, brakes to a halt inside `stopRadius` |

Wander stays smooth because that point moves a little each frame instead of jumping somewhere new. `wanderStrength` sets how fast it drifts. Small values give long straight walks, large ones make it fidget.

Give it an `NPCSensor` and it finds its own target: it chases whatever the sensor sees and drops back to wander `loseTargetDelay` seconds after losing sight of it. Leave that slot empty and the target is whatever you drag into the Inspector.

### How the farmhand avoids things

[`SteeringSensor.cs`](Assets/Scripts/SteeringSensor.cs) fires three rays along the direction of travel rather than the direction the body faces. The body turns with a slerp and lags behind during a turn, so facing is the wrong thing to ask.

Surfaces tilted less than `maxWalkableSlope` count as ground and are ignored. The island pieces sit on the `Obstacle` layer along with the crates, so without that test every hill ahead would read as a wall.

Three more probes point down at the ground ahead. A probe that finds nothing is treated like a wall, which is what keeps it on the island. The circular `boundsRadius` is a second net behind that one, useful because the island is not a circle.

Two habits stop it dithering. It remembers which way it chose to turn and only switches when the other side is clearer by `sideSwitchMargin`, and the avoidance force is damped over `avoidanceSmoothing` so it cannot reverse within a frame.

The avoidance force does not add to the behaviour force. It takes over:

```
takeover = threatLevel × avoidanceWeight
force    = lerp(behaviour force, avoidance force, takeover)
```

Adding the two was the obvious approach and it failed. A Seek force aimed straight at you wins the sum, so the farmhand pressed into the wall and shook. Handing avoidance full control at the moment it is cornered lets it turn.

None of that is a guarantee. Steering is advice, and advice can arrive too late. Before each step lands, a spherecast as wide as its body checks the path; if something blocks it, the step is cut short and the remainder slides along the surface. It runs twice, because sliding off one wall can bury it in the next. This is what stops it walking through the house. The steering is what makes going around the house look deliberate.

### Tuning the farmhand

Every number below is on the agent or its sensor, and changing one alone is the quickest way to see what it owns.

| Parameter | Turn it down | Turn it up |
|---|---|---|
| `maxSpeed` | Ambles | Overshoots targets and corners |
| `maxForce` | Wide lazy turns | Sharp turns, harder braking |
| `turnSpeed` | Body trails behind the path it walks | Snaps to face the path |
| `responseTime` | Stiff, almost robotic | Slides about as if on ice |
| `slowRadius` | Brakes late and abruptly | Crawls the last stretch |
| `stopRadius` | Stands on top of the target | Halts well short of it |
| `wanderStrength` | Long straight walks | Fidgets |
| `sensorDistance` | Notices walls too late | Swerves around things that were never in the way |
| `avoidanceWeight` | Ignores obstacles and leans on collision instead | Timid, bends away early |

Two of them are linked. The distance needed to stop is roughly `maxSpeed² ÷ (2 × maxForce)`, so raising `maxSpeed` without raising `maxForce` eventually pushes that distance past `slowRadius`, and the farmhand starts sailing through targets again.

### How the guard sees you

The second lab is still here, and the third is best read against it.

[`NPCSensor.cs`](Assets/Scripts/NPCSensor.cs) runs three tests every frame, ordered so the cheap ones reject you before the expensive one runs.

1. **Distance.** Are you inside the 10 metre radius?
2. **Angle.** Are you inside the 61 degree cone in front of the guard?
3. **Line of sight.** Does a ray from the guard's eyes reach yours without hitting anything on the `Obstacle` layer?

Fail any one of them and the guard perceives nothing.

That third test is what lets you hide. Crates, trees and hills sit on a layer the raycast collides with, so putting one between yourself and the guard breaks the chain. The ray leaves the guard a metre off the ground and arrives at the same height on you, which means cover shorter than a metre will not save you.

The farmhand borrows this same script to find you. Its own sensor only reads walls and ledges.

### How the guard decides

[`NPCBrain.cs`](Assets/Scripts/NPCBrain.cs) drives a `NavMeshAgent` through three states.

| State | What the guard does |
|---|---|
| Patrol | Walks the waypoint loop at speed 5 |
| Chase | Runs straight at you at speed 10 |
| Search | Goes to the spot where it last saw you, hunts for 3 seconds, gives up |

Search carries the memory. The guard records your position on every frame it can see you, so breaking line of sight never erases what it already knows. It walks to that spot first, then returns to patrol after finding nothing.

### How you move

[`PlayerController.cs`](Assets/Scripts/PlayerController.cs) moves a `CharacterController`, so walls and crates stop you. It integrates vertical motion by hand rather than leaving it to gravity, because flight needs control that gravity alone will not give.

W sends you wherever the camera faces, and the character turns to meet the direction it travels. [`CameraFollow.cs`](Assets/Scripts/CameraFollow.cs) orbits on a spring arm that shortens whenever scenery gets between the camera and your back.

[`PlayerAnimatorDriver.cs`](Assets/Scripts/PlayerAnimatorDriver.cs) writes one integer into the Animator, and three Any State transitions read it to pick the clip. A jump keeps the ground pose, since the character ships without a jump animation.

### Scripts

| File | Job |
|---|---|
| `SteeringAgent.cs` | Seek, Arrive, Wander, ground snapping, collision blocking |
| `SteeringSensor.cs` | Obstacle rays, ledge probes, threat level |
| `SteeringDebug.cs` | Gizmos for every part of the steering |
| `SimplePlayerController.cs` | Bare manual controller, kept as a reference point |
| `NPCSensor.cs` | Distance, field of view, line of sight |
| `NPCBrain.cs` | Patrol, Chase and Search over a NavMesh |
| `EnemyDetector.cs` | Colours the guard's light to match its state |
| `PlayerController.cs` | Walking, jumping, and the timed flight |
| `CameraFollow.cs` | Orbit camera that dodges scenery |
| `PlayerAnimatorDriver.cs` | Turns the locomotion state into Animator parameters |

### Gizmos

Keep the Scene view in sight while you play. Reading the farmhand's gizmos is the fastest way to tell a tuning problem from a reference you forgot to fill in, and [`SteeringDebug.cs`](Assets/Scripts/SteeringDebug.cs) draws all of them.

| Gizmo | Meaning |
|---|---|
| Label overhead | The state it is in right now |
| Cyan line | Velocity, the direction it is actually travelling |
| Yellow line | The direction the active behaviour wants |
| Magenta circle | The wander circle, drawn only while wandering |
| Three rays | Green is clear, red has hit something |
| Three small spheres | Ground probes ahead. Red means no ground there |
| Orange line | The avoidance force after damping |
| Grey circle at chest height | The collision body. Red while something is holding it back |
| White circle | The wander boundary |

The guard has its own set. `NPCSensor` draws the detection sphere, the edges of the view cone, and a line to you that turns red the moment the guard has you and grey when it does not. Select the guard and the `Has Line Of Sight` box in the Inspector tells you the same thing at a glance.

### Notes

Anything works as cover if it carries a collider, sits on the `Obstacle` layer, and stands taller than a metre. The single crates land right on that limit, so reach for the stacked pair or the trees.

`EnemyDetector.cs` is the distance-only detector from the first lab, kept for comparison. It now consults `NPCSensor` before switching state, so the light stops turning red while you are behind cover.

The guard and the farmhand are the comparison the third lab is really about. Put them beside the same wall and the guard rounds it without hesitating, because the NavMesh was baked before it took a step. The farmhand pauses, decides, then commits, because three metres of ray is everything it knows. Neither approach is the better one. The guard cannot react to anything that was not there at bake time; the farmhand needs no bake at all.

Two limits worth knowing. The collision check is a single sphere at chest height, so anything much shorter gets walked straight over. And `Physics.queriesHitBackfaces` is switched on globally, so walls register from inside a building, which the guard's line of sight tests inherit whether they want them or not.
