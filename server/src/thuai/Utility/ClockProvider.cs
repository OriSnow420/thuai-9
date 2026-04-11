namespace Thuai.Utility;

public class ClockProvider(int milliseconds)
{
    public int Milliseconds { get; } = milliseconds;
    public Task CreateClock() => Task.Delay(Milliseconds);
}
