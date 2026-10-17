# Professional AEC modeling workflow

BIM-agent should help an architect turn intent into a model that can be inspected, revised and documented. A prompt alone is not an adequate design specification.

## 1. Establish project context

- Identify target application, version, active file and whether the project is disposable, backed up or production.
- Read units, model origin, story elevations or layers/tags, design phases, existing families/components and view or sheet conventions.
- Ask for missing design decisions that change geometry or compliance. State assumptions for low-impact choices.

## 2. Write a modeling plan

Represent each scope item with: element class, type/family/component, level or host, dimensions and coordinates, material intent, naming, expected count, and dependencies. Keep a table mapping user language to host-native objects. Example: “partition” should normally map to a Revit wall type, not an anonymous mesh.

## 3. Execute in bounded stages

Create reference structure first (levels, grids, layers, tags). Then create primary geometry, openings, detail objects and documentation. Use small batches with approval and host-native transactions/undo. Do not silently change existing project settings or linked content.

## 4. Verify the deliverable

Read back IDs and properties; compare quantities and dimensions to the plan. Check host warnings, views, clashes, disconnected MEP connectors, face orientation and material assignment as applicable. Inspect at least one plan and one 3D view or render. Correct errors before reporting completion.

## 5. Handoff

Summarize created and modified objects, exact model file, unresolved decisions, warnings, and what still needs human review. Save/export only when the user has asked for the target deliverable and the host supports that operation.

## Current connector limits

The workflow is a quality target, not a claim that every step is automated today. `capabilities.json` and the application-specific docs are the source of truth for tool availability. When a needed operation is missing, an agent should implement a typed adapter operation and validate it in the host application before using it on a production model.
