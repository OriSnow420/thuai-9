namespace Thuai.Recorder;

using System.Collections.Concurrent;

public class RecordPage
{
    private readonly ConcurrentQueue<string> _records = new();

    public int Length => _records.Count;

    public void Enqueue(string record)
    {
        _records.Enqueue(record);
    }

    public string ToJson()
    {
        var records = _records.ToArray();
        return "[" + string.Join(",", records) + "]";
    }

    public void Clear()
    {
        while (_records.TryDequeue(out _)) { }
    }
}
