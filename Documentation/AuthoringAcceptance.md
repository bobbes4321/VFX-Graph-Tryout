# Authoring acceptance fixture

`AuthoringAcceptance.QueueRun()` queues an Edit Mode acceptance pass on the next editor update. The menu entry is **Line Lab → Authoring → Run Authoring Acceptance**. It uses Unity's AssetDatabase, graph compiler, native catalog and runtime geometry. It does not edit serialized asset text.

The implementation lives in `Assets/LineLab/Production/Editor/AuthoringAcceptance.cs`. The fixture's existence does not establish that a pass has executed. Inspect the recorded `passed` flag, timestamps and check list in `Logs/authoring-acceptance.json`. Live phase updates go to `Logs/authoring-acceptance-progress.txt`; exceptions are recorded in both the result and Unity's console.

## Persistent examples

On the first pass, the fixture creates these missing assets:

- `Assets/LineLab/Recipes/Proof/OpalMotes.asset`: a new traveling mote module with an alternate texture, an HDR two-color gradient, a size curve and reverse motion.
- `Assets/LineLab/Recipes/Proof/OpalTransfer.asset`: a duplicated Renewal composition containing two existing ribbon modules, the existing Renewal mote module and the new Opal mote module.
- `Assets/LineLab/Resources/Production/ProofLibrary.asset`: the artist library plus Opal Transfer. Untouched sample content yields five recipes.

Existing assets at these locations are reused without resetting their fields or replacing their references. They remain useful examples for inspection and editing in the Effect Workbench. If edits remove a relationship the acceptance fixture specifically checks, that check can fail; the fixture preserves the edits.

## Checks

The pass checks deterministic rebaking, stable-key lookup, public read-only native views and CPU/GPU record sizes. Unsaved clones test whether changing a shared mote module updates both referring recipes and changes the content fingerprint. Other probes cover different counts in one shared slot, transparent padding, effect alpha, endpoint heights/scales and static per-card gradients.

It force-compiles the proof library's six required preview graph variants twice and verifies that graph GUIDs survive. A separate temporary library tests an all-zero secondary stream: the graph retains a zero burst and safe indexing, while empty Shuriken pools accept presentation without allocating particles. This compilation targets preview capacity four; it does not regenerate the complete benchmark capacity matrix.

The existing native lifecycle and 5,000-capacity validation also runs. Preexisting recipe, library, scene, dependency and metadata files are hashed before the pass and checked afterward. Loaded scene cameras are checked for exact transform preservation. Temporary source clones and preview objects are disposed; source assets are not temporarily modified to run the propagation tests.

This fixture checks authoring and runtime contracts. It does not establish visual parity, shader execution correctness on every GPU or benchmark performance. Those require the standalone captures and benchmark runs.
