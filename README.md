# yugiho-tools

Ferramenta desktop para análise e exploração de **Yu-Gi-Oh! Forbidden Memories**
(PSX). Lê as ROMs do jogo, extrai todos os dados (cartas, fusões, rituais,
duelistas, drops, equipamentos), calcula caminhos de fusão para uma mão,
detecta cartas via captura do emulador, lê memory cards do ePSXe e suporta
múltiplos MODs com cache em disco.

---

## Funcionalidades

### MODs
- Cadastro de múltiplas ROMs (`SLUS_014.11` + `WA_MRG.MRG`) com nome amigável.
- Cópia automática para `MOD/<slug>/` no diretório do executável.
- **Extração completa** (`ModExtractor`) para `MOD/<slug>/data.json` +
  `MOD/<slug>/img/`:
  - 722 cartas com atributos, ATK/DEF, descrições, estrelas guardiãs.
  - Fusões, rituais, equipamentos, custo em Star Chips, IDs TCG.
  - 40 duelistas com pools de drop (deck / S-A POW / BCD POW / S-A TEC) e
    pesos. Inclui Rakuza/extra-duelistas via offset detection.
  - Sprites de carta extraídos do MRG (artwork, nome, atributo, estrelas)
    em BMP 32-bit RGBA.
- Importação de mods externos via `template-mod-data.json` (suporte ao
  formato 1-based usado pela TEAONLINE).
- Exportação de descrições traduzidas e dados editáveis.

### Cartas (`/cards`)
- Catálogo completo navegável das 722 cartas com filtros por nome / tipo /
  atributo / level e ordenação por `#ID` / ATK / DEF / tipo.
- Modal de detalhes com fusões que produzem a carta, fusões que ela
  produz, rituais que envolvem ela, duelistas que dropam.
- Visualização de sprites originais do MRG quando o MOD foi extraído.

### Fusões (`/`)
- Catálogo filtrável e MY DECK persistido com cartas escolhidas.
- Botão **CALCULAR FUSÕES** monta um grafo top-down único unindo todos
  os caminhos de fusão da mão.
- Cores únicas por par (HSL distribuído pelo *golden angle*); arestas
  convergem para junções A+B antes do resultado.
- Hover em junção destaca recursivamente a cadeia que leva àquela fusão.
- Click em carta no grafo: modal mostrando ramificações de origem/destino.
- Long-press 3 s: modal completo de detalhes.
- Zoom independente em três áreas (catálogo, MY DECK, grafo).

### Duelistas (`/duelists`)
- Lista os 40 duelistas extraídos do MOD.
- Modal com tabela de drops por pool, dropRate (%), e cartas únicas/raras.
- Agrupamento por duelista nas estatísticas de drop de uma carta.

### Favoritos (`/favorites`)
- Múltiplas listas nomeadas por MOD (ex: "Deck para Pegasus", "Farm BEUD").
- Estrelas (1–5) por carta dentro da lista.
- Página dedicada por lista com edição inline.

### Detecção automática (ePSXe)
- Lista janelas filtrando por título "ePSXe", `CopyFromScreen` na região.
- **OpenCV** template matching em escala de cinza (threshold 0.8) contra os
  722 thumbnails extraídos do MRG.
- Cartas detectadas adicionadas ao deck (deduplicadas).
- Cada captura salva em `logs/capture_<timestamp>.png` para debug.

### Memory Card (`/memory-card`)
- Upload de `.mcr/.mc/.mem/.gme/.psx/.ps/.srm`.
- Parsing do diretório PSX (frames 1–15 do bloco 0): country code, game
  code, save ID, blocos.
- Heurística de localização do deck FM (janela de 40 IDs 16-bit, strides
  2/4 com padding, score por densidade + diversidade).
- Aceita variantes/MODs sem product code "01411".
- Cálculo de fusões direto sobre o deck importado.

### Tradução automática de descrições
- Quatro provedores de tradução plugáveis selecionáveis em Settings:
  - **Anthropic Claude** (`claude-haiku` / `sonnet`, batched JSON).
  - **Ollama** local (default `llama3.1:8b`); suporte a múltiplos endpoints
    para paralelismo real (`OLLAMA_NUM_PARALLEL`).
  - **LM Studio** (OpenAI-compatible, sem `response_format`).
  - **DeepL** (free/pro), com preservação de markers via tag XML `<keep>`.
- Cache em disco por idioma em `data.json` (`DescriptionsByLanguage`).
- i18n da UI em **pt / en / es** (`Resources/Raw/i18n/*.json`),
  trocável em runtime via `LocalizationService`.

### Atalhos globais
- Combinação de teclado (`Ctrl+F2` etc.) **ou** botão de gamepad
  (`Pad:LT`, `Pad:Start`, `Pad:DPadUp`...).
- Ações combináveis: limpar deck / escanear emulador / calcular fusões.
- **Funciona em background**:
  - Teclado: `RegisterHotKey` (Win32) em thread dedicada.
  - Gamepad XInput: polling 60 Hz nos 4 slots
    (`xinput1_4 → 1_3 → 9_1_0`).
  - Foreground (DualShock/HID via JS Gamepad API): polling adicional na
    WebView.
- Debounce de 250 ms entre os caminhos.

### Configurações
- Idioma da UI (pt/en/es).
- Provedor de tradução + credenciais por provedor.
- Fonte das imagens de carta (Tea CDN / sprites locais do MOD).
- Quantidade máxima de cartas no catálogo, cores das descrições.
- Atalho global e mapeamento de ações.
- **Verificar atualizações** (Velopack).
- Tudo persistido via `Microsoft.Maui.Storage.Preferences`.

### Auto-update (Velopack + GitHub Releases)
- Pipeline GitHub Actions (`.github/workflows/release.yml`) acionado por
  tags `v*`.
- Canais derivados do sufixo SemVer:
  - `v1.0.0` → canal `stable`
  - `v1.0.0-beta.1` → canal `beta` (release marcada como pre-release)
  - `v1.0.0-alpha.3` → canal `alpha`
- Cada install fica gravado em um canal: usuário stable só recebe stable,
  beta só recebe beta. Trocar de canal exige reinstalar pelo Setup do
  outro canal.
- Updates incrementais via `RELEASES`, com restart automático.

---

## Tecnologias

| Camada | Tecnologia |
|---|---|
| Runtime | **.NET 10** — MAUI Blazor Hybrid (Windows-only) |
| UI | **Blazor** + **MudBlazor 8.x**, tema dark custom |
| Parsing binário | `BinaryPrimitives`, `Span<byte>` |
| Sprites | Decoders próprios (`SpriteDecoder`, `CardFrameDecoder`, `BmpEncoder`) |
| Reconhecimento | **OpenCvSharp4.Windows** (template matching) |
| Captura | Win32 (`EnumWindows`, `GetWindowRect`, `Graphics.CopyFromScreen`) |
| Hotkeys globais | Win32 `RegisterHotKey` + thread message loop |
| Gamepad nativo | XInput (`xinput1_4` / `1_3` / `9_1_0`) |
| Gamepad browser | JS Gamepad API + JSInterop |
| Tradução | Anthropic / Ollama / LM Studio / DeepL (HttpClient) |
| Storage | `Preferences` + JSON em `MOD/<slug>/data.json` |
| Auto-update | **Velopack** + GitHub Releases |
| Layout do grafo | DAG top-down, longest-path layering + barycenter sweeps |

---

## Arquitetura

```
Domain/
├── Entities/        Card, FusionStep, Mod, MemoryCardSave, Duelist, ...
├── Interfaces/      IRomParser, IFusionEngine, IScreenCapture,
│                    ICardDetector, IModRepository, IMemoryCardParser,
│                    IGlobalShortcutService
└── ValueObjects/    RomOffsetProfile, ...

Application/
├── Services/        FusionEngine, AppSettings, LocalizationService,
│                    LoadedRomCache, FavoritesService, ModExtractor,
│                    AnthropicTranslationService, ExtractedDataLoader,
│                    LabJsonImporter, ModCatalogService,
│                    CurrentModContext, UpdateService
├── UseCases/        LoadRomDataUseCase, GetFusionsFromHandUseCase,
│                    DetectHandFromScreenUseCase, RegisterModUseCase,
│                    ListModsUseCase, DeleteModUseCase,
│                    ParseMemoryCardUseCase
├── DTOs/            FusionStep, FusionSequence, FusionResultDto
└── Helpers/         GraphLayout, CardImage, CardMetadata,
                     SpriteDecoder, CardFrameDecoder, CardFrameRegistry,
                     BmpEncoder, ExtractedAssets, MermaidGenerator

Infrastructure/
├── Parsing/         RomParser, EpsxeMemoryCardParser,
│                    DropPoolScanner, DuelistOffsetDetector
├── ScreenCapture/   WindowsScreenCapture, Win32Helper
├── CardDetection/   OpenCvCardDetector
├── Shortcuts/       WindowsGlobalShortcutService
└── Storage/         FileModRepository, ExtractedDataRepository

Components/
├── Pages/           Home, Cards, Duelists, Favorites, FavoritesList,
│                    Mods, MemoryCard, Settings
├── Layout/          MainLayout (drawer + AppBar)
├── Shared/          CardCatalog
├── FusionGraph(Svg).razor       Grafo SVG interativo
├── CardDetailDialog.razor       Modal de detalhes
├── CardBranchesDialog.razor     Modal de ramificações
├── CardCover.razor              Frame da carta extraído do MRG
├── CardPickerDialog.razor       Seletor de carta
├── DuelistDetailDialog.razor    Detalhes do duelista
├── ModExtractionDialog.razor    Extração + tradução
├── JunctionFlowDialog.razor     Fluxo da junção no grafo
└── TextPromptDialog.razor       Input genérico

Resources/Raw/
├── i18n/{pt,en,es}.json         Strings da UI
└── card-types.json              Mapa tipo → ícone/cor
```

Dependências sempre apontam pra dentro (UI → Application → Domain).
Domain não referencia bibliotecas externas.

---

## Setup

```powershell
git clone <repo-url>
cd yugiho

dotnet workload restore
dotnet restore yugiho.sln
dotnet build yugiho-tools\yugiho-tools.csproj -c Debug

# Executar
dotnet run --project yugiho-tools\yugiho-tools.csproj

# Release self-contained x64
dotnet publish yugiho-tools\yugiho-tools.csproj -c Release -r win-x64
```

Pré-requisitos:
- Windows 10/11 (x64 ou ARM64)
- .NET 10 SDK
- Workload `maui-windows` (`dotnet workload install maui-windows`)

---

## Releases

Tags acionam o workflow:

```bash
git tag v1.0.0           # canal stable
git tag v1.0.0-beta.1    # canal beta
git push --tags
```

Setup.exe + RELEASES + .nupkg são publicados na GitHub Release. Usuários
recebem update automático ao clicar em **Settings → Verificar
atualizações** (mesmo canal).

---

## Uso

1. **MODs** → cadastre sua ROM (`SLUS_014.11` + `WA_MRG.MRG`).
2. Clique em **Extrair** no MOD para gerar `data.json` + sprites.
3. (Opcional) Configure tradução em **Settings** e traduza descrições.
4. Em **Fusões** monte o deck:
   - Catálogo, **OU**
   - **ESCANEAR EMULADOR** com ePSXe aberto, **OU**
   - **Memory Card** importado.
5. **CALCULAR FUSÕES** → grafo aparece.
6. Use **Cartas** / **Duelistas** / **Favoritos** para explorar dados.

---

## Estrutura de pastas em runtime

```
<exe-dir>/
├── MOD/
│   ├── mods.json
│   └── <slug>/
│       ├── SLUS_014.11
│       ├── WA_MRG.MRG
│       ├── chartable.tbl
│       ├── data.json            # extração + traduções
│       └── img/                 # sprites BMP por carta
├── logs/
│   └── capture_YYYYMMDD_HHmmss_fff.png
└── chartable.tbl
```

---

## Créditos

- [forbidden-memories-fusion-finder](https://github.com/vishtheshnu/forbidden-memories-fusion-finder)
  (Python) — base da lógica de fusão e mapeamento inicial dos offsets do ROM.
- TEAONLINE — formato `template-mod-data.json` e mapeamento dos offsets de
  rituais/equipamentos.
- Imagens das cartas: `https://www.basededatostea.xyz/img/lmfv/{cardId}.jpg`
  (configurável; sprites locais do MOD também são suportados).
