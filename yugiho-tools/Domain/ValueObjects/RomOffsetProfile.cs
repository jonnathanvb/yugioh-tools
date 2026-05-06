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
    public int DuelistData      { get; init; } = 0xE9B000;
    public int DuelistStride    { get; init; } = 0x1800;
    public int DuelistDeckOff   { get; init; } = 0x000;
    public int DuelistSaPowOff  { get; init; } = 0x5B4;
    public int DuelistBcdPowOff { get; init; } = 0xB68;
    public int DuelistSaTecOff  { get; init; } = 0x111C;

    // ── Decoder behavior ────────────────────────────────────────────────────
    /// <summary>
    /// Bytes that introduce a 3-byte control sequence (the byte itself + 2 args)
    /// and should be skipped while decoding text. Common in modded ROMs that use
    /// color/font escape codes (e.g. F8 0A 06 prefix in LMFV).
    /// </summary>
    public byte[] TextControlCodes3 { get; init; } = [0xF8, 0xFD];

    public static RomOffsetProfile Default { get; } = new();
}
