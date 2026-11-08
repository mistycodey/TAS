namespace TAS;

/// <summary>
/// Short plain-English explanations of what a sensor reading means. Names come from the hardware library and vary by
/// CPU/GPU model, so known names are matched loosely and everything else falls back to a description of its type.
/// </summary>
internal static class SensorDescriptions
{
    public static string TypeLabel(string type) => type switch
    {
        "Temperature" => "Temperature",
        "Load" => "Load (%)",
        "Clock" => "Clock speed (MHz)",
        "Power" => "Power (W)",
        "Voltage" => "Voltage (V)",
        "Current" => "Current (A)",
        "Fan" => "Fan speed (RPM)",
        "Control" => "Fan / control (%)",
        "SmallData" => "Memory (MB)",
        "Data" => "Data (GB)",
        "Throughput" => "Data throughput",
        "Level" => "Level (%)",
        "Factor" => "Ratio",
        "Frequency" => "Frequency (Hz)",
        "Energy" => "Energy",
        _ => type,
    };

    public static string Describe(string name, string type, bool gpu)
    {
        string n = name.ToLowerInvariant();
        bool Has(string text) => n.Contains(text);

        switch (type)
        {
            case "Temperature":
                if (Has("hot spot")) return "Hottest point anywhere on the GPU chip; usually the first to climb under load.";
                if (Has("memory junction")) return "Temperature inside the graphics memory chips; GDDR6X memory runs hot.";
                if (Has("gpu memory")) return "Temperature of the graphics memory.";
                if (Has("gpu core")) return "Temperature of the main graphics chip.";
                if (Has("ccds max")) return "Hottest chiplet (CCD) on a Ryzen CPU.";
                if (Has("ccds average")) return "Average temperature across the Ryzen chiplets (CCDs).";
                if (Has("ccd")) return "Temperature of one chiplet (CCD) on a Ryzen CPU.";
                if (Has("core max")) return "Temperature of the hottest single CPU core.";
                if (Has("core average")) return "Average temperature across all CPU cores.";
                if (Has("package") || Has("tctl") || Has("tdie")) return "Overall CPU temperature; the main number to watch.";
                if (Has("core #")) return "Temperature of one CPU core.";
                if (Has("soc")) return "Temperature of the CPU's system-on-chip (memory controller) area.";
                return gpu ? "A temperature reported by the graphics card." : "A temperature reported by the CPU.";

            case "Load":
                if (Has("cpu total")) return "How busy the whole CPU is, averaged over all cores.";
                if (Has("cpu core max")) return "How busy the single busiest CPU core is.";
                if (Has("thread") || Has("cpu core #")) return "How busy one CPU core or thread is.";
                if (Has("gpu core")) return "How hard the main graphics engine is working; near 100% means the GPU is maxed out.";
                if (Has("memory controller")) return "How much of the video memory bandwidth is being used.";
                if (Has("gpu memory")) return "How much of the video memory (VRAM) is in use.";
                if (Has("video engine")) return "How busy the video encode/decode hardware is.";
                if (Has("gpu bus")) return "How busy the GPU's PCIe connection to the rest of the PC is.";
                if (Has("gpu power")) return "Power draw as a percentage of the card's power limit.";
                if (Has("d3d")) return "How busy one Windows graphics (Direct3D) engine is, such as 3D, Copy or Video Decode.";
                return gpu ? "A usage figure for part of the graphics card." : "A usage figure for part of the CPU.";

            case "Clock":
                if (Has("gpu core")) return "Current graphics chip speed; drops when the GPU throttles because of heat or power limits.";
                if (Has("gpu memory")) return "Current speed of the video memory.";
                if (Has("shader")) return "Current speed of the GPU's shader units.";
                if (Has("bus")) return "The CPU's base reference clock that core speeds are multiplied from.";
                if (Has("core #")) return "Current speed of one CPU core; drops when it throttles or idles.";
                return gpu ? "A clock speed on the graphics card." : "A clock speed on the CPU.";

            case "Power":
                if (Has("package")) return gpu ? "Total power the graphics card is drawing." : "Total power the CPU is drawing, including cores, cache and memory controller.";
                if (Has("cores")) return "Power drawn by all the CPU cores together.";
                if (Has("core #")) return "Power drawn by one CPU core.";
                if (Has("graphics") || Has("igpu")) return "Power drawn by the CPU's built-in graphics.";
                if (Has("memory")) return "Power drawn by the memory controller or memory.";
                if (Has("gpu power") || Has("board")) return "Total power the graphics card is drawing.";
                return "Power drawn by part of this device.";

            case "Voltage":
                if (Has("core")) return "Voltage supplied to the cores; rises when the chip boosts.";
                if (Has("soc")) return "Voltage supplied to the CPU's system-on-chip area.";
                return "A voltage this device reports.";

            case "Current":
                return "Electrical current this device reports.";

            case "Fan":
                return "Fan speed in RPM. A very low or zero reading means the fan has slowed or stopped.";

            case "Control":
                return Has("fan")
                    ? "Fan speed as a percentage of its maximum, as requested by the fan controller."
                    : "A control output, shown as a percentage of its maximum.";

            case "SmallData":
            case "Data":
                if (Has("dedicated") && Has("used")) return "Video memory your apps are using right now.";
                if (Has("shared")) return "System memory being shared with the graphics card.";
                if (Has("memory used")) return "Video memory (VRAM) in use. Running out slows games down.";
                if (Has("memory free")) return "Video memory (VRAM) that is still free.";
                if (Has("memory total")) return "Total video memory (VRAM) the card has.";
                return "An amount of memory or data this device reports.";

            case "Throughput":
                if (Has("rx")) return "Data the graphics card is receiving over PCIe.";
                if (Has("tx")) return "Data the graphics card is sending over PCIe.";
                return "How fast data is moving through this device.";

            case "Level":
                return "A level reported as a percentage.";

            case "Factor":
                return "A ratio reported by the hardware.";

            case "Frequency":
                return "A frequency this device reports.";

            default:
                return "A reading reported by the hardware.";
        }
    }
}
