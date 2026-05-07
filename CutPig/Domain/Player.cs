using System.ComponentModel.DataAnnotations;

namespace CutPig.Domain;

public class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Nickname { get; set; }

    public string? AvatarData { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
