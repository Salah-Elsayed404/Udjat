namespace Udjat.Core.Monitoring;

/// <summary>
/// The two limits for one kind of sensor. A value at or above a limit counts as over it.
/// The unit is the sensor's own unit (°C, or % used).
/// </summary>
public sealed record Threshold(double Warning, double Critical)
{
    /// <summary>Throws <see cref="ArgumentException"/> if the limits make no sense.</summary>
    public void Validate()
    {
        if (!double.IsFinite(Warning) || !double.IsFinite(Critical))
            throw new ArgumentException("Threshold limits must be finite numbers.");
        if (Warning >= Critical)
            throw new ArgumentException($"Warning ({Warning}) must be lower than Critical ({Critical}).");
    }
}
