using Newtonsoft.Json;
using System;
using System.Diagnostics;

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
    public List<string> IgnoredApps { get; set; } = new();
    public WindowPosition WindowPosition { get; set; } = new WindowPosition 
    { 
        X = 100, 
        Y = 100, 
        MonitorDevice = Screen.PrimaryScreen?.DeviceName ?? ""
    };

    public static AppConfig Load()
    {
        string configPath = "config.json";
        try
        {
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
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

    public void Save()
    {
        try
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText("config.json", json);
            Debug.WriteLine($"Configuração salva com monitor: {WindowPosition.MonitorDevice}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving config: {ex.Message}");
        }
    }
} 