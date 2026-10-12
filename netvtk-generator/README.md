# netvtk-generator

Generates the SWIG interface files for [netvtk-core](../netvtk-core): one `.i` per VTK module, plus the value-type structs and native thunks. Forked from [netocc-gen](https://github.com/paulbuechner/netocc/tree/main/netocc-generator) (MIT).

| Folder | Role |
|---|---|
| netvtk-generator | reads VTK's install, writes netvtk-core's `src/SWIG_files/{wrapper,headers}`; runs on VTK upgrades and generator changes |
| netvtk-core | commits that output and builds without this folder |

## Commands

```bash
# writes netvtk-core's src/SWIG_files/{wrapper,headers,modules.json,classes.json} for config/modules.yaml's modules;
# --check only compares
dotnet run --project src/NetVtk.Generator -c Release -- generate --vtk <VTK install prefix> --core ../netvtk-core

# unit and golden tests (no VTK needed); NETVTK_UPDATE_GOLDEN=1 rewrites the expected files
dotnet test NetVtk.Generator.slnx -c Release
```

## Config

`config/modules.yaml` (hand-edited): the native libraries and their VTK modules, `exclude_headers`; per module `classes`, `exclude_classes`, `exclude_methods`, `exclude_namespaces`, `prelude`, `value_types`, `directors`.

License: MIT, see `LICENSE`.
