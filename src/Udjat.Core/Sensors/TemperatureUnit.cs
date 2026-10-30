namespace Udjat.Core.Sensors;

/// <summary>How temperatures are shown. Readings, limits and the settings file always stay in Celsius; only what the user sees changes.</summary>
public enum TemperatureUnit
{
    Celsius,
    Fahrenheit,
}

/// <summary>Converts a temperature between Celsius and the unit the user chose.</summary>
public static class Temperatures
{
    /// <summary>A Celsius value in the user's unit.</summary>
    public static double From(double celsius, TemperatureUnit unit) =>
        unit == TemperatureUnit.Fahrenheit ? celsius * 9 / 5 + 32 : celsius;

    /// <summary>A value in the user's unit, back in Celsius.</summary>
    public static double ToCelsius(double value, TemperatureUnit unit) =>
        unit == TemperatureUnit.Fahrenheit ? (value - 32) * 5 / 9 : value;

    /// <summary>"°C" or "°F".</summary>
    public static string Symbol(TemperatureUnit unit) => unit == TemperatureUnit.Fahrenheit ? "°F" : "°C";
}
