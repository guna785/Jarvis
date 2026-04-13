using Visor.Models;
using SQLite;

namespace Visor.Services;

public class VisorDatabase
{
    private SQLiteAsyncConnection _database;

    async Task Init()
    {
        if (_database is not null) return;

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "VisorVault.db3");
        _database = new SQLiteAsyncConnection(dbPath);

        await _database.CreateTableAsync<UserProfile>();
        await _database.CreateTableAsync<ChatHistory>();
    }

    public async Task SaveMessageAsync(int userId, string role, string message)
    {
        await Init();
        var entry = new ChatHistory
        {
            UserId = userId,
            Role = role,
            Message = message,
            Timestamp = DateTime.Now
        };
        await _database.InsertAsync(entry);
    }

    public async Task<List<string>> GetRecentHistoryAsync(int userId, int limit = 6)
    {
        await Init();
        var history = await _database.Table<ChatHistory>()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .ToListAsync();

        // Format for the LLM: "User: Hello"
        return history.Select(h => $"{h.Role}: {h.Message}").Reverse().ToList();
    }

    public async Task<List<ChatHistory>> GetFullHistoryAsync(int userId, int limit = 20)
    {
        await Init();
        var history = await _database.Table<ChatHistory>()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .ToListAsync();
        
        return history.AsEnumerable().Reverse().ToList();
    }

    public async Task<UserProfile> GetOrCreateUserAsync(string name)
    {
        await Init();
        var user = await _database.Table<UserProfile>().Where(u => u.Name == name).FirstOrDefaultAsync();
        if (user == null)
        {
            user = new UserProfile { Name = name, PersonalInstructions = "Be brief and loyal." };
            await _database.InsertAsync(user);
        }
        return user;
    }

    public async Task<UserProfile> CreateUserWithVoiceAsync(string name, float[] voicePrint)
    {
        await Init();
        var user = new UserProfile 
        { 
            Name = name, 
            PersonalInstructions = "Be brief and loyal.",
            VoicePrintBlob = new byte[voicePrint.Length * 4]
        };
        Buffer.BlockCopy(voicePrint, 0, user.VoicePrintBlob, 0, user.VoicePrintBlob.Length);
        await _database.InsertAsync(user);
        return user;
    }

    public async Task<List<UserProfile>> GetAllUsersAsync()
    {
        await Init();
        return await _database.Table<UserProfile>().ToListAsync();
    }

    public async Task UpdateUserAsync(UserProfile user)
    {
        await Init();
        await _database.UpdateAsync(user);
    }
}