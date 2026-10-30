namespace Udjat.Core.Monitoring;

/// <summary>How serious a sensor's current value is. Higher means worse.</summary>
public enum AlertLevel
{
    Normal = 0,
    Warning = 1,
    Critical = 2,
}
