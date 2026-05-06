namespace yugiho_tools.Domain.Entities;

public class Duelist
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Weight per card index (722 entries) summing to 2048.</summary>
    public ushort[] Deck { get; set; } = new ushort[722];

    /// <summary>Drop pool when the player wins with S-POW or A-POW rank.</summary>
    public ushort[] SaPow { get; set; } = new ushort[722];

    /// <summary>Drop pool when the player wins with B/C/D-POW.</summary>
    public ushort[] BcdPow { get; set; } = new ushort[722];

    /// <summary>Drop pool when the player wins with S-TEC or A-TEC.</summary>
    public ushort[] SaTec { get; set; } = new ushort[722];

    public override string ToString() => $"#{Id} {Name}";
}
