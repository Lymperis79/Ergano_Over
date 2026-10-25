using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

/// <summary>
/// A small secret that every PC using this database must share (for example the key that protects the saved
/// Ergani and mail passwords), so a password saved on one PC can be used on all the others.
/// </summary>
public class AppSecret
{
    [Key, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Value { get; set; } = string.Empty;
}
