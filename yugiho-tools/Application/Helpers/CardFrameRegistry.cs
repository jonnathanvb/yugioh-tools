namespace yugiho_tools.Application.Helpers;

/// <summary>
/// Cache em memória dos frames (espelhos) decodificados do ROM. Populado
/// durante o load do ROM. Lookup por <c>(cycle, color)</c> ou pelos
/// helpers de mapeamento por tipo/atributo de carta.
///
/// Decisões pendentes (TODO): a correspondência exata
/// "tipo de carta → (cycle, color)" foi inferida pelo testem visual da
/// ferramenta cardsleeves; o jogo na real usa colorIndex pra atributo
/// (Light/Dark/Earth/Water/Fire/Wind + extra) e cycle pra outras
/// variantes (provavelmente categoria: monstro, magic, trap, ritual…).
/// Por ora, mapeamento aproximado é suficiente — o usuário pode validar
/// trocando manualmente.
/// </summary>
public static class CardFrameRegistry
{
    private static readonly Dictionary<(int Cycle, int Color), string> Frames = new();

    /// <summary>
    /// Posições dos elementos sobre o frame, em coordenadas do canvas
    /// (144×200). Os valores REAIS são lidos do SLUS_014.11 nos offsets
    /// abaixo — o jogo permite que mods relocalizem os slots, então
    /// hardcoded errado pra qualquer ROM que não seja o de fábrica.
    /// Defaults aqui são da revisão NTSC-U; <see cref="LoadPositions"/>
    /// sobrescreve com o que estiver no SLUS do usuário.
    /// </summary>
    public static int ArtX  = 19,  ArtY  = 50;
    public static int NameX = 12,  NameY = 14;
    public static int AtkX  = 97,  AtkY  = 157;
    public static int DefX  = 97,  DefY  = 171;
    public static int AttrX = 110, AttrY = 13;
    /// <summary>Posição da PRIMEIRA estrela (mais à direita). Estrelas
    /// adicionais crescem pra esquerda. Default vem do SLUS NTSC-U.</summary>
    public static int StX   = 119, StY   = 32;

    /// <summary>Posição dos rótulos "ATK" e "DEF" (texto). Só renderizados
    /// quando <see cref="ShowAtkDefLabels"/> = true.</summary>
    public static int AtkLabelX = 70, AtkLabelY = 157;
    public static int DefLabelX = 70, DefLabelY = 171;

    /// <summary>Se true, CardCover renderiza "ATK"/"DEF" como overlay HTML
    /// nas posições <see cref="AtkLabelX"/> etc. Default false: o frame do
    /// ROM original já traz as palavras impressas.</summary>
    public static bool ShowAtkDefLabels;

    /// <summary>Tamanho de fonte em cqw (% da largura do container). Estes
    /// defaults batem com o app.css; o MOD sobrescreve via dialog.</summary>
    public static double NameFontSize        = 5.0;
    public static double AtkDefValueFontSize = 6.5;
    public static double AtkDefLabelFontSize = 6.5;

    public const int ArtW  = 102, ArtH  = 96;
    public const int NameW = 95,  NameH = 16;
    public const int AtkW  = 27,  AtkH  = 13;
    public const int DefW  = 27,  DefH  = 13;
    public const int AttrW = 19,  AttrH = 16;
    public const int StarW = 8,   StarH = 8;
    public const int AtkLabelW = 24, AtkLabelH = 13;
    public const int DefLabelW = 24, DefLabelH = 13;

    public const int FrameW = CardFrameDecoder.CanvasWidth;
    public const int FrameH = CardFrameDecoder.CanvasHeight;

    /// <summary>
    /// Lê as posições reais de ATK/DEF/nome/atributo direto do SLUS_014.11.
    /// Offsets vêm de <c>getPositionValues()</c> no JS BackgrounEdit.
    /// </summary>
    public static void LoadPositions(byte[] slus)
    {
        if (slus is null || slus.Length < 104601) return;
        ArtX  = slus[103476];  ArtY  = slus[103500];
        NameX = slus[103624];  NameY = slus[103676];
        AtkX  = slus[104112];  AtkY  = slus[104132];
        DefX  = slus[104252];  DefY  = slus[104272];
        StX   = slus[104396];  StY   = slus[104432];
        AttrX = slus[104580];  AttrY = slus[104600];
    }

    /// <summary>
    /// Carrega posições do JSON pré-extraído. Usado quando o ROM já
    /// foi processado e o app não tem mais acesso ao SLUS de origem.
    /// </summary>
    public static void LoadPositions(Domain.Entities.FramePositions p)
    {
        ArtX  = p.ArtX;  ArtY  = p.ArtY;
        NameX = p.NameX; NameY = p.NameY;
        AtkX  = p.AtkX;  AtkY  = p.AtkY;
        DefX  = p.DefX;  DefY  = p.DefY;
        StX   = p.StX;   StY   = p.StY;
        AttrX = p.AttrX; AttrY = p.AttrY;
        AtkLabelX = p.AtkLabelX; AtkLabelY = p.AtkLabelY;
        DefLabelX = p.DefLabelX; DefLabelY = p.DefLabelY;
        NameFontSize        = p.NameFontSize;
        AtkDefValueFontSize = p.AtkDefValueFontSize;
        AtkDefLabelFontSize = p.AtkDefLabelFontSize;
    }

    /// <summary>True se o registry foi populado com sucesso.</summary>
    public static bool HasFrames => Frames.Count > 0;

    public static void Load(byte[] mrg)
    {
        Frames.Clear();
        var decoded = CardFrameDecoder.DecodeAll(mrg);
        foreach (var kv in decoded) Frames[kv.Key] = kv.Value;
    }

    /// <summary>
    /// Popula o registry com data URLs já decodificadas (lidas do disco).
    /// Usado pelo <c>ExtractedDataLoader</c> quando o MOD foi extraído
    /// previamente — pula a leitura do MRG inteiro.
    /// </summary>
    public static void LoadFromMemory(IDictionary<(int Cycle, int Color), string> dataUrls)
    {
        Frames.Clear();
        foreach (var kv in dataUrls) Frames[kv.Key] = kv.Value;
    }

    public static string? GetFrame(int cycle, int color)
        => Frames.TryGetValue((cycle, color), out var url) ? url : null;

    /// <summary>
    /// Conjunto de CardIds que aparecem como RESULTADO de pelo menos uma
    /// fusão registrada. Usado pra colorir frames dessas cartas com a
    /// paleta roxa (color 4). Populado em <see cref="LoadFusionResults"/>.
    /// </summary>
    private static readonly HashSet<int> FusionResultCardIds = new();

    /// <summary>
    /// Constrói o conjunto de cartas-resultado-de-fusão a partir da lista
    /// de cartas parseada. Chamado depois do parse para que
    /// <see cref="MappingForCard"/> consiga distinguir um monstro normal
    /// de um produto de fusão.
    /// </summary>
    public static void LoadFusionResults(IEnumerable<Domain.Entities.Card> cards)
    {
        FusionResultCardIds.Clear();
        foreach (var c in cards)
        {
            // Em FM os índices em FusionResults são 0-based; CardId é 1-based.
            foreach (var idx in c.FusionResults)
                FusionResultCardIds.Add(idx + 1);
        }
    }

    /// <summary>
    /// Mapeia uma carta pra um par (cycle, color) que decide qual frame usar.
    ///
    /// Convenção empírica (descoberta via <c>/frames-debug</c>):
    ///   • color 0 → monstro padrão
    ///   • color 1 → Magic (Type 20) / Equip (Type 23) — verde
    ///   • color 2 → Trap (Type 21)                    — vermelho/rosa
    ///   • color 3 → Ritual (Type 22)                  — azul
    ///   • color 4 → resultado de fusão                — roxo
    ///   • color 5 → monstro variante                  — alternativo
    ///   • color 6 → Divine-Beast (Type 13)            — destaque
    ///
    /// Cycle 0 pra tudo: as outras 9 cycles são variações sutis (selected/
    /// dimmed/etc) que não fazem diferença visual fora do contexto do jogo.
    /// </summary>
    public static (int Cycle, int Color) MappingForCard(Domain.Entities.Card c)
    {
        if (c.IsRitual) return (0, 3);
        if (c.IsFusion) return (0, 4);
        
        return c.CardType switch
        {
  
            21 => (0, 2),                                       // Trap
            20 or 23 => (0, 1),                                 // Magic / Equip
            _  => (0, 0),                                     // Monstro padrão
        };
    }

    public static string? GetFrameForCard(Domain.Entities.Card c)
    {
        var (cycle, color) = MappingForCard(c);
        // Fallback se o cycle/color escolhido não tem frame decodificado.
        return GetFrame(cycle, color) ?? GetFrame(0, 0);
    }

    /// <summary>Sobrecarga legada (só atributo) — ainda usada em alguns
    /// pontos. Prefere a versão que recebe a Card inteira.</summary>
    public static string? GetFrameForCard(int attribute)
    {
        var dummy = new Domain.Entities.Card { Attribute = attribute };
        return GetFrameForCard(dummy);
    }
}
