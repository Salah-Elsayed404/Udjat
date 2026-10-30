using Udjat.Core.Monitoring;

namespace Udjat.Core.Status;

/// <summary>One value of one sensor at one time.</summary>
public readonly record struct HistoryPoint(DateTimeOffset Time, double Value);

/// <summary>
/// The last few minutes of every reading, for the dashboard's charts. In memory only: when Udjat stops,
/// the history is gone, which matches the "log alerts, not sensor history" rule. Safe to use from several threads.
/// </summary>
public sealed class ReadingHistory
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<HistoryPoint>> _points = new();

    /// <param name="capacity">How many points to keep per sensor; older ones fall off.</param>
    public ReadingHistory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>The number of points that covers <paramref name="window"/> when polling every <paramref name="pollInterval"/>.</summary>
    public static int CapacityFor(TimeSpan window, TimeSpan pollInterval)
    {
        if (window <= TimeSpan.Zero || pollInterval <= TimeSpan.Zero)
            throw new ArgumentException("The times must be greater than zero.");

        return Math.Max(1, (int)Math.Ceiling(window / pollInterval));
    }

    /// <summary>Remembers every reading of one poll.</summary>
    public void Add(PollResult poll)
    {
        lock (_gate)
        {
            foreach (var status in poll.Sensors)
            {
                if (!_points.TryGetValue(status.Reading.Id, out var queue))
                    _points[status.Reading.Id] = queue = new Queue<HistoryPoint>();

                queue.Enqueue(new HistoryPoint(poll.Time, status.Reading.Value));
                while (queue.Count > _capacity)
                    queue.Dequeue();
            }
        }
    }

    /// <summary>A copy of one sensor's points, oldest first. Empty if the sensor is unknown.</summary>
    public IReadOnlyList<HistoryPoint> Snapshot(string id)
    {
        lock (_gate)
            return _points.TryGetValue(id, out var queue) ? queue.ToArray() : [];
    }
}
