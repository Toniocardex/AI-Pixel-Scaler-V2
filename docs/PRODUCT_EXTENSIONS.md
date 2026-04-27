# Product Extensions Roadmap

This document summarizes extension points for public-product evolution.

## Pipeline Contract

- Core orchestration entry-point: `AiPixelScaler.Core.Pipeline.Imaging.PixelArtPipeline`.
- Stable data contracts:
  - `PixelArtPipeline.Options`
  - `PixelArtPipeline.Report`

These contracts are intended to be consumed by Desktop UI, Batch and future CLI.

## CLI v1

Project scaffold: `src/AiPixelScaler.CLI`.

Initial scope:
- `process <input.png> <output.png>`
- delegates processing to `PixelArtPipeline`

Future scope:
- expose the full `Options` matrix via command-line flags
- add batch command parity with `AiPixelScaler.Batch`

## Plugin v1

Project scaffold: `src/AiPixelScaler.Plugins`.

Interface:
- `IImageProcessorPlugin`
  - `Id`
  - `DisplayName`
  - `Version`
  - `Process(Image<Rgba32>)`

Future scope:
- dynamic plugin loader (assembly probing)
- compatibility/version guardrails
- fault isolation around plugin execution

## Batch Alignment

`src/AiPixelScaler.Batch` is aligned to run processing through `PixelArtPipeline` before optional shared-palette remap and export.

This guarantees one common processing contract across app surfaces.
