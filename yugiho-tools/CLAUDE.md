# Prompt de Projeto — MAIU: Migração Python → .NET

## Contexto do Projeto

Você é um desenvolvedor .NET Fullstack sênior responsável por migrar e modernizar a ferramenta **Fusion Finder** do jogo *Yu-Gi-Oh! Forbidden Memories* (PS1). O código-fonte original em Python está disponível em `./doc/tools` no repositório do projeto.

A ferramenta original realiza:
- Leitura e parsing de arquivos ROM do PS1 (`SLUS_014.11`, `WA_MRG.MRG`)
- Extração de dados de fusões e cartas a partir dos binários do jogo
- Captura de tela do emulador em execução para identificar as cartas na mão do jogador (via Pillow/imagem)
- Interface gráfica com Tkinter para exibição das fusões possíveis

---

## Objetivos da Migração

1. **Reescrever 100% do código Python em .NET 8** com arquitetura limpa e boas práticas
2. **Substituir a GUI Tkinter** por uma interface moderna MAUI
3. **Substituir a captura de tela com Pillow** por equivalente .NET nativo
4. **Manter todas as funcionalidades existentes**, adicionando melhorias de UX, utilizar mudblazor como framework base das UI
5. **Estruturar o projeto para extensibilidade** (suporte a mods futuros, novos ROMs)

---

## Instruções de Análise

Antes de qualquer implementação, **leia e analise todos os arquivos em `./doc/tools`**:

```
./doc/tools/
├── *.py          → código-fonte Python original
├── requirements.txt → dependências (Pillow, tkinter, etc.)
└── data/         → estrutura de dados esperada (ROMs, configs)
```

Para cada arquivo `.py` encontrado, documente:
- Responsabilidade do módulo
- Dependências externas usadas
- Algoritmos principais (ex: parsing binário, matching de fusões)
- Equivalente .NET a ser usado

---

## Stack .NET a Utilizar

| Camada | Tecnologia | Justificativa |
|---|---|---|
| UI | **WPF** (.NET 8, Windows) ou **MAUI** (multiplataforma) | Substituir Tkinter |
| Lógica de Domínio | **C# 12**, `record` types, `readonly struct` | Imutabilidade, performance |
| Parsing Binário | `System.IO.BinaryReader`, `Span<byte>` | Substituir leitura manual com struct Python |
| Captura de Tela | `System.Drawing` / `Windows.Graphics.Capture` | Substituir Pillow |
| Dados / Cache | `System.Text.Json` + `Dictionary<>` em memória | Substituir estruturas Python dict |
| Testes | **xUnit** + **FluentAssertions** + **Moq** | Cobertura de parsing e lógica |
| Build / CI | `dotnet CLI`, `Directory.Build.props` | Padronização do projeto |

---

## Arquitetura do Projeto

Estruture a solution com separação clara de responsabilidades:

```
MAIU.sln
├── src/
│   ├── MAIU.Domain/              # Entidades, regras de fusão, contratos
│   │   ├── Entities/
│   │   │   ├── Card.cs
│   │   │   ├── Fusion.cs
│   │   │   └── FusionResult.cs
│   │   ├── Interfaces/
│   │   │   ├── IRomParser.cs
│   │   │   ├── IFusionEngine.cs
│   │   │   └── IScreenCapture.cs
│   │   └── ValueObjects/
│   │       ├── CardId.cs
│   │       └── GuardianStar.cs
│   │
│   ├── MAIU.Application/         # Casos de uso, orquestração
│   │   ├── UseCases/
│   │   │   ├── LoadRomDataUseCase.cs
│   │   │   ├── GetFusionsFromHandUseCase.cs
│   │   │   └── DetectHandFromScreenUseCase.cs
│   │   └── DTOs/
│   │       └── FusionResultDto.cs
│   │
│   ├── MAIU.Infrastructure/      # Implementações concretas
│   │   ├── Parsing/
│   │   │   ├── RomParser.cs      # Substituir leitura binária Python
│   │   │   └── FusionDataReader.cs
│   │   ├── ScreenCapture/
│   │   │   └── EmulatorScreenCapture.cs  # Substituir Pillow
│   │   └── Storage/
│   │       └── JsonCardRepository.cs
│   │
│   └── MAIU.UI/                  # Projeto WPF ou MAUI
│       ├── ViewModels/
│       │   └── MainViewModel.cs
│       ├── Views/
│       │   └── MainWindow.xaml
│       └── App.xaml
│
└── tests/
    ├── MAIU.Domain.Tests/
    ├── MAIU.Application.Tests/
    └── MAIU.Infrastructure.Tests/
```

---

## Boas Práticas Obrigatórias

### 1. Clean Architecture
- Dependências apontam sempre para dentro (UI → Application → Domain)
- Domain **não referencia nenhum projeto externo**
- Interfaces definidas no Domain, implementadas na Infrastructure

### 2. Imutabilidade e Records
```csharp
// Prefira records para entidades de valor
public record Card(CardId Id, string Name, CardType Type, GuardianStar Star1, GuardianStar Star2);
public record Fusion(Card Material1, Card Material2, Card Result);
public readonly record struct CardId(ushort Value);
```

### 3. Parsing Binário com Span<T>
```csharp
// Evite alocações desnecessárias ao ler o ROM
public IReadOnlyList<Card> ParseCards(ReadOnlySpan<byte> romData)
{
    // Use MemoryMarshal, BinaryPrimitives
}
```

### 4. Injeção de Dependência
- Configure DI no projeto de UI usando `Microsoft.Extensions.DependencyInjection`
- Todos os serviços registrados por interface
- Sem `new` em construtores de serviços

### 5. MVVM na UI (WPF/MAUI)
- ViewModels não referenciam View
- Use `ICommand` (`RelayCommand` via CommunityToolkit.Mvvm)
- Bindings declarativos no XAML
- Sem code-behind com lógica

### 6. Tratamento de Erros
```csharp
// Use Result pattern para operações que podem falhar
public Result<IReadOnlyList<Card>> LoadCards(string path)
{
    // Nunca lance exceção para fluxo normal
}
```

### 7. Testes
- Todo parser binário deve ter testes unitários com dados de ROM mock
- Cobertura mínima: 80% em Domain e Application
- Testes de integração para leitura de ROM real (marcados com `[Trait("Category","Integration")]`)

---

## Mapeamento de Funcionalidades Python → .NET

| Funcionalidade Python | Arquivo Original | Equivalente .NET |
|---|---|---|
| Leitura ROM binária | `*.py` (struct/unpack) | `BinaryReader` + `Span<byte>` |
| Parsing de cartas | script de extração | `RomParser.cs` |
| Lógica de fusões | engine de matching | `FusionEngine.cs` |
| Captura de tela | `Pillow` (PIL) | `Graphics.CopyFromScreen` ou WinRT |
| Reconhecimento de cartas na imagem | pixel matching | `System.Drawing` / ML.NET (opcional) |
| Interface gráfica | `tkinter` | WPF `MainWindow.xaml` / MAUI `ContentPage` |
| Seleção de mod/subfolder | dropdown tkinter | `ComboBox` binding a `ObservableCollection` |
| Botão "LOAD DATA" | tkinter button | `ICommand` binding |
| Botão "GET FUSIONS" | tkinter button | `ICommand` binding |

---

## Entrega Esperada por Fase

### Fase 1 — Análise e Domain
- [ ] Ler e mapear todos os arquivos `.py` em `./doc/tools`
- [ ] Criar `MAIU.Domain` com todas as entidades e interfaces
- [ ] Documentar o formato binário do ROM parseado pelo Python

### Fase 2 — Infrastructure (Parsing)
- [ ] Implementar `RomParser.cs` equivalente ao parsing Python
- [ ] Testes unitários cobrindo os parsers com fixtures de bytes

### Fase 3 — Application (Use Cases)
- [ ] Implementar `LoadRomDataUseCase` e `GetFusionsFromHandUseCase`
- [ ] Implementar `DetectHandFromScreenUseCase`

### Fase 4 — UI
- [ ] ViewModel com propriedades reativas (`ObservableProperty`, `RelayCommand`)
- [ ] View XAML com layout equivalente à UI Tkinter, porém modernizado
- [ ] Configuração de DI no App.xaml.cs

### Fase 5 — Testes e Documentação
- [ ] Cobertura de testes ≥ 80% em Domain e Application
- [ ] README.md atualizado com instruções de build e uso
- [ ] MIGRATION_NOTES.md documentando decisões de design

---

## Restrições e Decisões de Design

- **Sem dependências Python**: a versão .NET deve ser 100% autossuficiente
- **Windows-first**: foco inicial em Windows (WPF); MAUI pode ser avaliado em fase posterior
- **Performance**: o parsing do ROM deve ser feito uma única vez e cacheado em memória
- **Extensibilidade**: estrutura deve suportar múltiplos ROMs/mods via configuração, sem recompilar
- **Sem banco de dados**: dados de fusão ficam em memória após carregamento do ROM

---

## Referência do Código Original

O repositório base está em:
> `https://github.com/vishtheshnu/forbidden-memories-fusion-finder/tree/dependabot/pip/pillow-10.0.1`

A cópia local está em:
> `./doc/tools` (no repositório do projeto MAIU)

**Sempre consulte o código original em `./doc/tools` antes de implementar qualquer parser ou lógica de fusão**, para garantir compatibilidade com os dados do ROM.