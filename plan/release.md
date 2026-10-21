# Release

- **The first release, 9.7.1.1:** nuget.org has only `9.7.1.1-next.1` (2026-10-21). When the entries in `netvtk-core/changes/` reach main, `release.yml` opens "Release NetVtk 9.7.1.1"; approve its checks, merge, and `build.yml` publishes. Its tag `v9.7.1.1` is the commit consumers pin the vcpkg registry at.
- **nuget.org's ID prefix:** reserving `NetVtk` is optional; not known to be done.
