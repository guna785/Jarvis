namespace Visor.Contract;

public interface ICallService
{
    Task<CallResult> PlaceCallAsync(string target);
}

public sealed class CallResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
