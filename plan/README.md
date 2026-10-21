# NetVtk plan

Open work only, a file per topic: unfinished items, unresolved faults and risks, undecided questions. When an item is done, its lasting facts go into the documentation (`netvtk-documentation/articles/`) or the `CLAUDE.md` files and the item leaves; a topic with nothing left loses its file. Decisions taken: [Design decisions](../netvtk-documentation/articles/design.md).

| File | Open |
|---|---|
| [release.md](release.md) | the first release, 9.7.1.1 |
| [ci.md](ci.md) | the patched VTK on macOS |
| [direct3d-sharing.md](direct3d-sharing.md) | sharing without the 1 x 1 step; a re-created render framebuffer; depth with reverse Z |
| [macos-hosting.md](macos-hosting.md) | an NSView child, on a Mac |
| [wrapping-gaps.md](wrapping-gaps.md) | what netvtk-gen leaves out: vectors of other elements, maps and sets, size hints that call members |
| [modules.md](modules.md) | more modules and readers; the packages' size |
