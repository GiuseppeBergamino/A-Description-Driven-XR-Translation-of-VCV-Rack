# VeryVCV Bridge
This folder contains the VCV Rack-side component of VeryVCV Rack.  

The Bridge is implemented as a custom VCV Rack module that scans the live Rack patch, exports a semantic JSON snapshot through HTTP, and exchanges runtime parameter-control messages with the Quest MR client through OSC/UDP.

## Contents

- `VeryVCVBridge.cpp`: main module and widget implementation.
- `VeryVCVBridgeScan.cpp`: extraction of modules, controls, jacks, and layout data from the Rack patch.
- `VeryVCVBridgeJson.cpp`: JSON serialization of the semantic patch snapshot.
- `VeryVCVBridgeHttp.*`: local HTTP snapshot server/cache.
- `VeryVCVBridgeOSC.*`: OSC runtime communication and profiling messages.
- `VeryVCVMR.hpp`: shared constants and data structures used by the Bridge.

## Build

The Bridge follows the standard VCV Rack plugin structure.  
To build it, install the VCV Rack SDK and follow the official VCV Rack plugin development and building [documentation](https://vcvrack.com/manual/PluginDevelopmentTutorial).

Typical Rack plugin builds use the `RACK_DIR` environment variable to point to the local Rack SDK installation, followed by `make`.

Tested with:
- VCV Rack: 2.6.6
- Platform: macOS on Apple Silicon
