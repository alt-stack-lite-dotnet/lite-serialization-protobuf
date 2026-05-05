# Publishing

This repo publishes multiple NuGet packages:

- `Lite.Serialization.Protobuf`
- `Lite.Serialization.Protobuf.Body.Json`
- `Lite.Serialization.Protobuf.Body.Xml`
- `Lite.Serialization.Protobuf.Body.MessagePack`
- `Lite.Serialization.Protobuf.Body.MemoryPack`

Current prerelease version: `0.9.0-beta`.

## CI and publish flow

- CI workflow (`.github/workflows/ci.yml`)
  - runs on push to any branch
  - runs on pull requests to `master`, `develop`, and `release/**`
- Publish workflow (`.github/workflows/publish.yml`)
  - triggers on tag push `v*`
  - publishes only if the tagged commit belongs to `release/*` branch
  - packs and pushes all packages to NuGet (`NUGET_API_KEY` secret required)

## Prerequisites

- GitHub secret `NUGET_API_KEY` configured in the repository.
- Package versions in csproj files set to the intended release value.

## Release steps

1. Ensure release branch state is ready (build/tests green).
2. Update package versions (if needed).
3. Commit and push to `release/*` branch.
4. Create and push tag from that commit, for example:
   - `git tag v0.9.0-beta`
   - `git push origin v0.9.0-beta`
5. Verify `Publish Packages` workflow is green.
6. Verify packages on NuGet.

## Local pack (optional check)

From repo root:

```bash
dotnet pack "sources/Lite.Serialization.Protobuf/Lite.Serialization.Protobuf.csproj" -c Release -o ./nupkg
dotnet pack "sources/Lite.Serialization.Protobuf.Body.Json/Lite.Serialization.Protobuf.Body.Json.csproj" -c Release -o ./nupkg
dotnet pack "sources/Lite.Serialization.Protobuf.Body.Xml/Lite.Serialization.Protobuf.Body.Xml.csproj" -c Release -o ./nupkg
dotnet pack "sources/Lite.Serialization.Protobuf.Body.MessagePack/Lite.Serialization.Protobuf.Body.MessagePack.csproj" -c Release -o ./nupkg
dotnet pack "sources/Lite.Serialization.Protobuf.Body.MemoryPack/Lite.Serialization.Protobuf.Body.MemoryPack.csproj" -c Release -o ./nupkg
```

