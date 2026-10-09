# PlusEMU++
Community continuation of PlusEMU By Sledmore (et al)

Focus is mostly on improving the code quality & emulator architecture so that implemented new features will be easy.

Want to join the discussion? Head over to http://arcturus.pw

## AIR wire-header profile

Copy `Resources/Revisions/OCTANE-AIR-IDS-WIN63-202609161723-93809945.json` into the server's runtime `revisions/` directory. Use the matching Octane-Renderer `protocol/OCTANE-AIR-IDS-WIN63-202609161723-93809945.json` configuration overlay, including `floorplan.wire.profile: octane-floor-20260909`, so the client announces the same revision name. Restart the server after adding the revision.

This opt-in profile uses AIR headers where the existing endpoint identity is supported and separate compatibility headers elsewhere. It preserves current handler destinations, Renderer consumers and packet bodies; it does not implement native AIR payloads. Existing revision files and default configuration remain unchanged.
