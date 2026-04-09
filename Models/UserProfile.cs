using SQLite;

namespace Jarvis.Models;

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

public class ChatHistory
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Role { get; set; } // "User" or "Jarvis"
    public string Message { get; set; }
    public DateTime Timestamp { get; set; }
}