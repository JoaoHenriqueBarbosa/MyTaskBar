namespace MyTaskBar;

using System.Diagnostics;
using System.Windows.Forms;
using Newtonsoft.Json;

public class WindowPosition
{
    private string monitorDevice = "";
    
    [JsonProperty]
    public string MonitorDevice
    {
        get => monitorDevice;
        set
        {
            if (value != monitorDevice)
            {
                Debug.WriteLine($"Monitor sendo alterado de '{monitorDevice}' para '{value}'");
                monitorDevice = value;
            }
        }
    }

    // Coordenadas relativas ao monitor
    public int X { get; set; }
    public int Y { get; set; }

    // Mantido para compatibilidade com configurações antigas
    [JsonIgnore]
    public int Monitor
    {
        get
        {
            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i].DeviceName == MonitorDevice)
                    return i + 1;
            }
            return 1; // Monitor padrão se não encontrar
        }
        set
        {
            var screens = Screen.AllScreens;
            if (value > 0 && value <= screens.Length)
            {
                MonitorDevice = screens[value - 1].DeviceName;
            }
            else
            {
                MonitorDevice = Screen.PrimaryScreen?.DeviceName ?? "";
            }
        }
    }
}

public class AppConfig
{
    public HashSet<string> IgnoredApps { get; set; } = [];
    public bool ShowInTaskbar { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool ForceHideTaskbar { get; set; } = false;
    public WindowPosition WindowPosition { get; set; } = new()
    {
        X = 100,
        Y = 100,
        MonitorDevice = Screen.PrimaryScreen?.DeviceName ?? ""
    };

    private const string ConfigPath = "config.json";

    public void IgnoreApp(string appTitle)
    {
        if (!IgnoredApps.Contains(appTitle))
        {
            IgnoredApps.Add(appTitle);
            Save();
        }
    }

    public void RestoreApp(string appTitle)
    {
        if (IgnoredApps.Contains(appTitle))
        {
            IgnoredApps.Remove(appTitle);
            Save();
        }
    }

    public void Save()
    {
        try
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
            Debug.WriteLine($"Configuração salva com monitor: {WindowPosition.MonitorDevice}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving config: {ex.Message}");
        }
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var config = JsonConvert.DeserializeObject<AppConfig>(json);
                if (config == null)
                {
                    throw new JsonException("Failed to deserialize config");
                }
                
                // Só converte se realmente não tiver MonitorDevice
                if (string.IsNullOrEmpty(config.WindowPosition.MonitorDevice))
                {
                    Debug.WriteLine("MonitorDevice vazio, convertendo do valor numérico");
                    var screens = Screen.AllScreens;
                    var monitor = config.WindowPosition.Monitor;
                    if (monitor > 0 && monitor <= screens.Length)
                    {
                        config.WindowPosition.MonitorDevice = screens[monitor - 1].DeviceName;
                    }
                    else
                    {
                        config.WindowPosition.MonitorDevice = Screen.PrimaryScreen?.DeviceName ?? "";
                    }
                }
                else
                {
                    Debug.WriteLine($"Usando MonitorDevice configurado: {config.WindowPosition.MonitorDevice}");
                }
                
                return config;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading config: {ex.Message}");
        }
        return new AppConfig();
    }
}