namespace yugiho_tools.Domain.Entities;

public class Mod
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string GameFileName { get; set; } = "SLUS_014.11";
    public string MrgFileName  { get; set; } = "WA_MRG.MRG";
    public DateTime CreatedAt  { get; set; }
}
