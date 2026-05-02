# yugiho-tools

Ferramenta desktop para análise de fusões de cartas de **Yu-Gi-Oh! Forbidden Memories**
(PSX). Lê as ROMs do jogo, calcula todas as fusões possíveis para uma mão de
cartas e exibe o resultado em um grafo interativo. Suporta múltiplos MODs,
detecção automática via captura do emulador e leitura direta do memory card do
ePSXe.

---

## Funcionalidades principais

### MODs
- Cadastro de múltiplas ROMs (`SLUS_014.11` + `WA_MRG.MRG`) com nome amigável.
- Cópia automática dos arquivos para `MOD/<slug>/` no diretório do executável.
- Listagem, exclusão e seleção persistida em `Preferences`.

### Fusões
- Seleção de MOD + parsing das 722 cartas (atributos, ATK/DEF, descrições,
  estrelas guardiãs, materiais e resultados de fusão).
- Catálogo filtrável por nome / descrição / `#ID`, tipo de monstro
  (Dragon, Spellcaster, Zombie...) e atributo (Light, Dark, Earth, Water,
  Fire, Wind, Magic, Trap), com ordenação por `#ID`, ATK, DEF ou tipo.
- Long-press de 3 s no catálogo abre modal com todos os atributos da carta.
- **MY DECK** sempre visível com cartas escolhidas; catálogo colapsável
  separadamente.
- Botão **CALCULAR FUSÕES** roda o engine e produz um grafo top-down único
  unindo todos os caminhos de fusão da mão.
- Cores únicas por par de fusão (HSL distribuído pelo *golden angle*),
  arestas convergindo para junções A+B antes de chegar ao resultado.
- Hover em uma junção destaca recursivamente toda a cadeia que leva àquela
  fusão (esmaecendo o resto).
- Click curto em uma carta no grafo: modal mostrando apenas as ramificações
  de origem e destino que envolvem aquela carta.
- Long-press 3 s em uma carta no grafo: modal de detalhes.
- Zoom independente em três áreas: catálogo, MY DECK e grafo de fusões.

### Detecção automática (ePSXe)
- Lista janelas visíveis filtrando por título contendo "ePSXe".
- `CopyFromScreen` captura a região da janela.
- Template matching com **OpenCV** (`MatchTemplate` em escala de cinza,
  threshold 0.8) compara contra os 722 thumbnails extraídos do MRG.
- Cartas detectadas são adicionadas ao deck (deduplicadas).
- Cada captura é salva em `logs/capture_<timestamp>.png` para debug.

### Memory Card
- Upload de qualquer arquivo `.mcr/.mc/.mem/.gme/.psx/.ps/.srm`.
- Parsing do diretório PSX (frames 1–15 do bloco 0): identifica todos os
  saves presentes, com country code, game code, save ID e número de blocos.
- Heurística para localizar a tabela de deck FM dentro do save (janela de
  40 IDs de 16 bits, strides 2 e 4 com padding, pontuação por densidade e
  diversidade).
- Aceita variantes/MODs do jogo (não exige product code "01411") — tenta
  extrair de qualquer save e mostra mensagem clara quando o padrão não é
  reconhecido.
- Cálculo de fusões direto sobre o deck extraído, com mesmo grafo
  interativo das outras telas.

### Atalhos globais
- Configuração de uma combinação de teclado (ex: `Ctrl+F2`) **ou**
  botão de gamepad (ex: `Pad:LT`, `Pad:Start`, `Pad:DPadUp`).
- Caixas de seleção para definir quais ações o atalho dispara: limpar deck,
  escanear emulador, calcular fusões.
- **Funciona com app em background / minimizado**:
  - Teclado: `RegisterHotKey` (Win32) em thread dedicada com message loop.
  - Gamepad XInput-compatível: polling em background Task a 60 Hz nos 4
    slots (`xinput1_4 → 1_3 → 9_1_0`).
  - Foreground (qualquer controle, inclusive DualShock/HID via JS Gamepad
    API): polling adicional via `requestAnimationFrame` na WebView.
- Debounce de 250 ms evita disparo duplicado entre os dois caminhos.

### Configurações
- Quantidade máxima de cartas no catálogo (default 50).
- Configuração do atalho global e mapeamento de ações.
- Tudo persistido via `Microsoft.Maui.Storage.Preferences`.

---

## Tecnologias

| Camada | Tecnologia |
|---|---|
| Runtime | **.NET 10** (preview) — MAUI Blazor Hybrid |
| UI | **Blazor** + **MudBlazor 8.x** (componentes), tema dark custom |
| Parsing binário | `BinaryPrimitives`, `Span<byte>` |
| Imagens (thumbnails) | `System.Drawing.Common` (PNG, BGRA → base64) |
| Reconhecimento | **OpenCvSharp4.Windows** (template matching) |
| Captura de tela | Win32 P/Invoke (`EnumWindows`, `GetWindowRect`,
  `Graphics.CopyFromScreen`) |
| Hotkeys globais | Win32 `RegisterHotKey` + thread message loop |
| Gamepad nativo | XInput (`xinput1_4.dll` / 1_3 / 9_1_0) |
| Gamepad browser | JS Gamepad API + JSInterop |
| Storage | `Microsoft.Maui.Storage.Preferences` + JSON em `MOD/mods.json` |
| Layout do grafo | DAG top-down com longest-path layering + barycenter
  sweeps + pixel positioning por baricentro de predecessores |

---

## Arquitetura

```
Domain/
├── Entities/        Card, FusionStep, Mod, MemoryCardSave, ...
└── Interfaces/      IRomParser, IFusionEngine, IScreenCapture,
                     ICardDetector, IModRepository, IMemoryCardParser,
                     IGlobalShortcutService

Application/
├── Services/        FusionEngine, AppSettings
├── UseCases/        LoadRomDataUseCase, GetFusionsFromHandUseCase,
                     DetectHandFromScreenUseCase, RegisterModUseCase,
                     ListModsUseCase, DeleteModUseCase,
                     ParseMemoryCardUseCase
├── DTOs/            FusionStep, FusionSequence, FusionResultDto
└── Helpers/         GraphLayout, CardImage, CardMetadata, MermaidGenerator

Infrastructure/
├── Parsing/         RomParser, EpsxeMemoryCardParser
├── ScreenCapture/   WindowsScreenCapture, Win32Helper
├── CardDetection/   OpenCvCardDetector
├── Shortcuts/       WindowsGlobalShortcutService (RegisterHotKey + XInput)
└── Storage/         FileModRepository

Components/
├── Pages/           Home (fusões), Mods, MemoryCard, Settings
├── Layout/          MainLayout (drawer + AppBar)
├── FusionGraphSvg.razor       Grafo SVG interativo
├── CardDetailDialog.razor     Modal de detalhes
└── CardBranchesDialog.razor   Modal de ramificações
```

Dependências apontam sempre para dentro (UI → Application → Domain).
Domain não referencia nenhuma biblioteca externa.

---

## Setup

```powershell
# Clonar
git clone <repo-url>
cd yugiho

# Restore + build
dotnet restore yugiho.sln
dotnet build yugiho.sln -c Release

# Executar (Windows)
dotnet run --project yugiho-tools\yugiho-tools.csproj -f net10.0-windows10.0.19041.0
```

Pré-requisitos:
- Windows 10/11
- .NET 10 SDK (preview)
- Workload `maui-windows`

---

## Uso

1. Abra o app e vá em **MODs** → cadastre sua ROM com `SLUS_014.11` e
   `WA_MRG.MRG`. O app copia tudo para `MOD/<slug>/`.
2. Volte em **Fusões**, selecione o MOD. As 722 cartas são carregadas.
3. Monte o deck:
   - Clicando nas cartas do catálogo, **OU**
   - Clicando em **ESCANEAR EMULADOR** com o ePSXe aberto, **OU**
   - Importando do **Memory Card** do ePSXe.
4. Clique em **CALCULAR FUSÕES** — o grafo aparece abaixo.
5. No grafo: clique em uma carta para ver suas ramificações; segure 3 s
   para ver os detalhes; passe o mouse sobre uma junção `+` para destacar
   o caminho que leva àquela fusão.

---

## Estrutura de pastas em runtime

```
<exe-dir>/
├── MOD/
│   ├── mods.json
│   ├── <slug>/
│   │   ├── SLUS_014.11
│   │   ├── WA_MRG.MRG
│   │   └── chartable.tbl
│   └── ...
├── logs/
│   └── capture_YYYYMMDD_HHmmss_fff.png
└── chartable.tbl
```

---

## Créditos

Baseado na lógica de fusão de
[forbidden-memories-fusion-finder](https://github.com/vishtheshnu/forbidden-memories-fusion-finder)
(Python). Imagens das cartas servidas por
`https://www.basededatostea.xyz/img/lmfv/{cardId}.jpg`.



Creditos: https://github.com/vishtheshnu/forbidden-memories-fusion-finder
Esse repositorio me ajudou a mapear alguns endereços de dados da room. agradecimentos.