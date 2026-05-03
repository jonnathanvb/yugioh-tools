namespace yugiho_tools.Domain.Entities;

public class Mod
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string GameFileName { get; set; } = "SLUS_014.11";
    public string MrgFileName  { get; set; } = "WA_MRG.MRG";

    /// <summary>
    /// Template para a URL das imagens das cartas. Deve conter o placeholder
    /// <c>{id}</c>, que é substituído pelo CardId em tempo de renderização.
    /// Ex.: <c>https://www.basededatostea.xyz/img/lmfv/{id}.jpg</c>.
    /// </summary>
    public string ImageUrlTemplate { get; set; } = "";

    public DateTime CreatedAt  { get; set; }
}
