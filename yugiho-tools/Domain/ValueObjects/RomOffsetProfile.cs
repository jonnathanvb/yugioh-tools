namespace yugiho_tools.Domain.ValueObjects;

/// <summary>
/// ROM offsets used by <see cref="Infrastructure.Parsing.RomParser"/>. Default values
/// match the original NTSC-U <c>SLUS_014.11</c> / <c>WA_MRG.MRG</c>. Mods that keep
/// the original layout (e.g. LMFV) reuse the same profile; mods that relocate tables
/// can supply their own values via <c>mods-catalog.json</c>.
/// </summary>
public sealed record RomOffsetProfile
{
    // ── SLUS (game executable) ──────────────────────────────────────────────
    public int CardAttribs   { get; init; } = 0x1C4A44;
    public int LevelAttr     { get; init; } = 0x1C5B33;
    public int NamePtrs      { get; init; } = 0x1C6002;
    public int NameTable     { get; init; } = 0x1C6800;
    public int NamePtrBase   { get; init; } = 0x6000;
    public int DescPtrs      { get; init; } = 0x1B0A02;
    public int DescTable     { get; init; } = 0x1B11F4;
    public int DescPtrBase   { get; init; } = 0x9F4;
    public int DuelistNamePtrs { get; init; } = 0x1C6652;
    public int DuelistCount    { get; init; } = 39;

    // ── MRG (assets) ────────────────────────────────────────────────────────
    public int Fusions          { get; init; } = 0xB87800;
    public int FusionBlockSize  { get; init; } = 0x10000;
    public int Equips           { get; init; } = 0xB85000;
    public int EquipBlockSize   { get; init; } = 0x2800;
    public int Thumbnails       { get; init; } = 0x16BAE0;
    public int ThumbnailStride  { get; init; } = 14336;
    /// <summary>Offset da arte HD (102×96, indexed) — usada na tela de
    /// detalhes do jogo quando se aperta Triangle. Compartilha o stride
    /// com os thumbnails (mesmo slot de 14336 bytes por carta).</summary>
    public int CardArt          { get; init; } = 0x169000;
    public int CardArtWidth     { get; init; } = 102;
    public int CardArtHeight    { get; init; } = 96;
    public int DuelistData      { get; init; } = 0xE9B000;
    public int DuelistStride    { get; init; } = 0x1800;
    /// <summary>
    /// Offset da tabela de rituais no MRG. Cada entrada tem 10 bytes
    /// (5 × uint16 LE): <c>[ritualSpellId, req1, req2, req3, result]</c>,
    /// todos card IDs 1-based (mascarados em 10 bits). A leitura termina
    /// quando uma entrada zerada é encontrada.
    /// Engenharia reversa via TEAONLINE Ritual Editor.
    /// </summary>
    public int Rituals          { get; init; } = 0xB97400;
    public int DuelistDeckOff   { get; init; } = 0x000;
    public int DuelistSaPowOff  { get; init; } = 0x5B4;
    public int DuelistBcdPowOff { get; init; } = 0xB68;
    public int DuelistSaTecOff  { get; init; } = 0x111C;

    // ── Decoder behavior ────────────────────────────────────────────────────
    /// <summary>
    /// Bytes que introduzem uma sequência de controle de 3 bytes
    /// (o próprio byte + 2 args) e devem ser pulados ao decodificar texto.
    /// No FM-US e mods compatíveis, só <c>0xF8</c> é control (cor/fonte).
    /// <c>0xFD</c> NÃO é control padrão — adicionar à lista quebra a leitura
    /// de descrições que contêm 0xFD como caractere/separador legítimo
    /// (sintoma: descrição emendada com texto da próxima carta).
    /// </summary>
    public byte[] TextControlCodes3 { get; init; } = [0xF8];

    public static RomOffsetProfile Default { get; } = new();
}
