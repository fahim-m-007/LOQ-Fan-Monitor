using System;
using System.Diagnostics;
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
                    using ManagementBaseObject? input =
                        instance.GetMethodParameters("GetFeatureValue");

                    if (input is null)
                    {
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
                        continue;
                    }

                    object? rawValue = output["value"];

                    if (rawValue is not null)
                    {
                        return Convert.ToUInt32(rawValue);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Fan reading exception (0x{featureId:X8}): {ex.Message}");
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
                        continue;
                    }

                    object? raw = output["Data"];

                    if (raw is not null)
                    {
                        int temperature = Convert.ToInt32(raw);

                        if (temperature >= 1 && temperature <= 125)
                        {
                            return temperature;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{methodName} exception: {ex.Message}");
        }

        return null;
    }
}

public sealed record TelemetrySnapshot(
    uint? Fan1Rpm,
    uint? Fan2Rpm,
    int? CpuTempC,
    int? GpuTempC,
    DateTime Timestamp
);
