
using System;
using System.IO;
using System.Linq;
using System.Management;

namespace LOQFanMonitor.Services;

/// <summary>
/// Read-only Lenovo telemetry access.
/// Uses getter methods only. Never changes fan speeds,
/// thermal modes, BIOS settings, or hardware configuration.
/// </summary>
public sealed class LenovoWmiReader
{
    private const string NamespacePath = @"\\.\root\WMI";

    private const uint Fan1FeatureId = 0x04030001;
    private const uint Fan2FeatureId = 0x04030002;

    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(),
        "LOQFanMonitor-wmi.log"
    );

    public TelemetrySnapshot Read()
    {
        uint? fan1 = ReadFan(Fan1FeatureId);
        uint? fan2 = ReadFan(Fan2FeatureId);

        int? cpu = ReadGameZoneTemperature("GetCPUTemp");
        int? gpu = ReadGameZoneTemperature("GetGPUTemp");

        return new TelemetrySnapshot(
            fan1,
            fan2,
            cpu,
            gpu,
            DateTime.Now
        );
    }

    private static uint? ReadFan(uint featureId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                NamespacePath,
                "SELECT * FROM LENOVO_OTHER_METHOD"
            );

            using var results = searcher.Get();

            foreach (ManagementObject instance in results)
            {
                using (instance)
                {
                    Log($"Reading fan feature ID: 0x{featureId:X8}");

                    using ManagementBaseObject? input =
                        instance.GetMethodParameters("GetFeatureValue");

                    if (input is null)
                    {
                        Log("GetMethodParameters returned null.");
                        continue;
                    }

                    input["IDs"] = featureId;

                    using ManagementBaseObject? output =
                        (ManagementBaseObject?)instance.InvokeMethod(
                            "GetFeatureValue",
                            input,
                            null
                        );

                    if (output is null)
                    {
                        Log("InvokeMethod returned null.");
                        continue;
                    }

                    foreach (PropertyData property in output.Properties)
                    {
                        Log(
                            $"Output: {property.Name} = " +
                            $"{property.Value ?? "<null>"}"
                        );
                    }

                    object? rawValue = output["value"];

                    if (rawValue is not null)
                    {
                        uint rpm = Convert.ToUInt32(rawValue);

                        Log($"Fan feature 0x{featureId:X8}: {rpm} RPM");

                        return rpm;
                    }

                    Log("The output did not contain a value parameter.");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Fan reading exception: {ex}");
        }

        return null;
    }

    private static int? ReadGameZoneTemperature(string methodName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                NamespacePath,
                "SELECT * FROM LENOVO_GAMEZONE_DATA"
            );

            using var results = searcher.Get();

            foreach (ManagementObject instance in results)
            {
                using (instance)
                {
                    using ManagementBaseObject? output =
                        (ManagementBaseObject?)instance.InvokeMethod(
                            methodName,
                            null,
                            null
                        );

                    if (output is null)
                    {
                        Log($"{methodName}: null output.");
                        continue;
                    }

                    foreach (PropertyData property in output.Properties)
                    {
                        Log(
                            $"{methodName} output: {property.Name} = " +
                            $"{property.Value ?? "<null>"}"
                        );
                    }

                    object? raw = output["Data"];

                    if (raw is not null)
                    {
                        int temperature = Convert.ToInt32(raw);

                        if (temperature >= 1 && temperature <= 125)
                        {
                            Log($"{methodName}: {temperature} C");
                            return temperature;
                        }

                        Log($"{methodName}: invalid temperature {temperature}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"{methodName} exception: {ex}");
        }

        return null;
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}"
            );
        }
        catch
        {
            // Logging failure must never crash the monitor.
        }
    }
}

public sealed record TelemetrySnapshot(
    uint? Fan1Rpm,
    uint? Fan2Rpm,
    int? CpuTempC,
    int? GpuTempC,
    DateTime Timestamp
);
