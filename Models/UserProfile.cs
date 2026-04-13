
using SQLite;
using System.Diagnostics.CodeAnalysis;

namespace Visor.Models;

[Preserve(AllMembers = true)]
public class UserProfile
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string Name { get; set; }
    public string PersonalInstructions { get; set; }
    public byte[] VoicePrintBlob { get; set; } // The 192-float signature

    [Ignore]
    public float[] VoicePrint => VoicePrintBlob == null ? null :
        Enumerable.Range(0, VoicePrintBlob.Length / 4)
                  .Select(i => BitConverter.ToSingle(VoicePrintBlob, i * 4)).ToArray();
}

[Preserve(AllMembers = true)]
public class ChatHistory
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Role { get; set; } // "User" or "Visor"
    public string Message { get; set; }
    public DateTime Timestamp { get; set; }
}

// Internal attribute for linker hints
internal class PreserveAttribute : Attribute
{
    public bool AllMembers { get; set; }
    public bool Conditional { get; set; }
}