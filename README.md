# VeryVCV Rack

Companion repository for the paper:

**A Description-Driven XR Translation of VCV Rack Modular Synthesis Interfaces**

This repository contains the source material and evaluation logs for **VeryVCV Rack**, a research prototype that extends VCV Rack into mixed reality. The system keeps VCV Rack as the source environment for audio computation, patch state, and parameter application, while a Meta Quest 3 client reconstructs the live Rack patch as a spatial MR interaction layer.

The prototype uses a custom VCV Rack Bridge module to export a structured JSON description of the current patch and to exchange runtime parameter-control messages with the Quest client through OSC/UDP.

## Repository structure
```text
.
├── vcv_bridge/
├── unity_client/
└── test_results/
````

## `vcv_bridge/`
Contains the VCV Rack-side Bridge module.

The Bridge scans the live Rack patch, extracts modules, controls, jacks, layout information, parameter identifiers, values, and interaction metadata, serializes the resulting semantic patch snapshot as JSON, and exposes it through HTTP. It also receives parameter-control messages from the MR client and sends parameter updates back through OSC.

This folder contains the C++ source files and resources needed to inspect or build the research prototype as a VCV Rack plugin. Build instructions follow the standard VCV Rack SDK workflow.

## `unity_client/`

Contains the Unity/Meta Quest 3 mixed-reality client.

The client retrieves semantic patch snapshots from the Bridge, maps the JSON description to a Unity runtime model, and reconstructs Rack modules as MR panels populated with reusable controls such as knobs, sliders, buttons, labels, and jacks. It also implements hand-tracking interaction and OSC-based bidirectional parameter synchronization.

This folder includes the relevant Unity assets, C# scripts, prefabs, materials, and scene used to document the prototype implementation.

## Current scope
The current prototype supports parameter-level MR reconstruction and bidirectional synchronization between VCV Rack and Meta Quest 3. It does not yet support cable editing, user-defined spatial layouts, complete reconstruction of fully custom Rack controls, or module-specific MR-native representations.
