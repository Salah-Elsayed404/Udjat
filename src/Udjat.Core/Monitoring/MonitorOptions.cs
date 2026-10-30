namespace Udjat.Core.Monitoring;

/// <summary>Timing and filtering settings shared by all sensors. See docs/ARCHITECTURE.md.</summary>
public sealed record MonitorOptions
{
    /// <summary>Time between two reads of all sensors.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How far below a limit (in the sensor's unit) a value must fall before the level drops.
    /// Stops a value hovering at a limit from flapping between levels.
    /// </summary>
    public double Hysteresis { get; init; } = 3;

    /// <summary>Consecutive readings at or above a limit needed before the level rises. Filters spikes.</summary>
    public int ConfirmReadings { get; init; } = 2;

    /// <summary>Minimum time between two alerts of the same level for the same sensor.</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a sensor must stay Critical before auto-protect may act.</summary>
    public TimeSpan SustainedCriticalAfter { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Throws <see cref="ArgumentException"/> if any setting is out of range.</summary>
    public void Validate()
    {
        if (PollInterval <= TimeSpan.Zero)
            throw new ArgumentException("PollInterval must be greater than zero.");
        if (!double.IsFinite(Hysteresis) || Hysteresis < 0)
            throw new ArgumentException("Hysteresis must be zero or more.");
        if (ConfirmReadings < 1)
            throw new ArgumentException("ConfirmReadings must be at least 1.");
        if (Cooldown < TimeSpan.Zero)
            throw new ArgumentException("Cooldown must not be negative.");
        if (SustainedCriticalAfter < TimeSpan.Zero)
            throw new ArgumentException("SustainedCriticalAfter must not be negative.");
    }
}
