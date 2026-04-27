# Refactor Changelog

Questo documento riassume il refactor architetturale completato sulle milestone prodotto.

## Obiettivo

- Centralizzare la pipeline immagini in un entry-point unico.
- Ridurre la frammentazione tra UI, Batch e futuri client.
- Preparare il repository a evoluzione CLI e plugin.

## Modifiche principali

### 1) Pipeline unificata (Core)

- Aggiunto orchestratore unico: `src/AiPixelScaler.Core/Pipeline/Imaging/PixelArtPipeline.cs`
  - Contratti stabili:
    - `PixelArtPipeline.Options`
    - `PixelArtPipeline.Report`
  - Orchestrazione completa: chroma, quantize, denoise, island denoise, outline, alpha threshold, recover fill, requantize.

### 2) Riduzione duplicazioni UI

- `src/AiPixelScaler.Desktop/Views/MainWindow.axaml.cs`
  - `RunQuickProcess()` e `RunPixelPipeline()` convergono su flusso comune:
    - `TryBuildPipelineOptions(...)`
    - `ExecutePipeline(...)`
  - Preset safe/aggressive allineati allo stesso entry-point pipeline.

### 3) ViewModel minimo pipeline

- Aggiunto `src/AiPixelScaler.Desktop/ViewModels/PipelineViewModel.cs`
  - Stato parametri pipeline.
  - Preset:
    - `ApplySafePreset()`
    - `ApplyAggressiveRecoverPreset()`
  - Costruzione opzioni:
    - `BuildOptions(...)`

### 4) Batch allineato al contratto unico

- `src/AiPixelScaler.Batch/Program.cs`
  - Processing base ora passa da `PixelArtPipeline`.
  - Mantiene export indexed/shared palette e report batch.

### 5) Skeleton prodotto pubblico

- Nuovo progetto CLI:
  - `src/AiPixelScaler.CLI/AiPixelScaler.CLI.csproj`
  - `src/AiPixelScaler.CLI/Program.cs`
- Nuovo progetto Plugins:
  - `src/AiPixelScaler.Plugins/AiPixelScaler.Plugins.csproj`
  - `src/AiPixelScaler.Plugins/IImageProcessorPlugin.cs`
- Solution aggiornata:
  - `AiPixelScaler.sln` include `AiPixelScaler.CLI` e `AiPixelScaler.Plugins`.

### 6) Documentazione estensioni

- Aggiunto:
  - `docs/PRODUCT_EXTENSIONS.md`
  - descrive contratto pipeline, piano CLI e plugin v1.

## Impatti positivi

- Coerenza: un solo punto di orchestrazione algoritmica.
- Manutenibilità: meno duplicazioni tra quick/full flow.
- Estendibilità: base pronta per CLI e plugin senza riscrivere la logica core.
- Rilasciabilità: build/test/publish restano invariati come processo.

## Verifica eseguita

- Build Release solution: OK
- Test Release: OK
- Publish Desktop win-x64 self-contained:
  - `publish/win-x64`
  - `dist/win-x64`

## Rischi residui

- `MainWindow.axaml.cs` è ancora grande: è consigliato continuare la migrazione verso MVVM completo.
- CLI skeleton è minimale: da espandere con mapping completo delle opzioni pipeline.
- Plugin loader dinamico non è ancora implementato (solo interfaccia v1).

## Prossimi passi consigliati

1. Spostare ulteriormente stato/comandi da `MainWindow` a ViewModel dedicati.
2. Espandere `AiPixelScaler.CLI` con flag completi e comandi batch parity.
3. Implementare plugin loader con version guardrails e isolamento errori.
