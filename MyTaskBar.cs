namespace MyTaskBar;

using System.Runtime.InteropServices;
using System.Text;
using Timer = System.Windows.Forms.Timer;
using System.Diagnostics;
using MyTaskBar.Components;
using MyTaskBar.Native;

/// <summary>
/// Representa a janela principal do aplicativo de gerenciamento de janelas.
/// Fornece uma interface minimalista para visualizar e gerenciar janelas ativas no Windows.
/// </summary>
public partial class MainWindow : Form
{
    private readonly FlowLayoutPanel layout;
    private readonly Timer updateTimer;
    private readonly AppConfig config;
    private readonly Dictionary<IntPtr, TaskWindowState> windowStates = new();
    private readonly object stateLock = new();
#if !RELEASE
    private static readonly string LogFile = "taskbar.log";
#endif
    private bool isInitializing = true;
    private IntPtr taskbarHwnd;

    // Delegate e handler para controle do console
    private delegate bool ConsoleEventHandler(CtrlType sig);
    private static readonly ConsoleEventHandler _handler = Handler;

    [DllImport("Kernel32")]
    private static extern bool SetConsoleCtrlHandler(ConsoleEventHandler handler, bool add);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
    private static extern IntPtr CreateRoundRectRgn(
        int nLeftRect,
        int nTopRect,
        int nRightRect,
        int nBottomRect,
        int nWidthEllipse,
        int nHeightEllipse
    );

    enum CtrlType
    {
        CTRL_C_EVENT = 0,
        CTRL_BREAK_EVENT = 1,
        CTRL_CLOSE_EVENT = 2,
        CTRL_LOGOFF_EVENT = 5,
        CTRL_SHUTDOWN_EVENT = 6
    }

    private static bool Handler(CtrlType sig)
    {
        Application.Exit();
        return true;
    }

    [DllImport("shell32.dll")]
    private static extern int SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uCallbackMessage;
        public int uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    private static extern bool SetWindowVisibility(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("psapi.dll")]
    private static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, 
        [Out] StringBuilder lpBaseName, uint nSize);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    // Lista de processos protegidos (jogos com anti-cheat conhecidos)
    private static readonly HashSet<string> ProtectedProcesses = new()
    {
        "valorant.exe",
        "valorant-win64-shipping.exe",
        "csgo.exe",
        "cs2.exe",
        "easyanticheat.exe",
        "battleye.exe",
        "pubg.exe",
        "tslgame.exe",
        "r5apex.exe",        // Apex Legends
        "fortnite.exe",
        "overwatch.exe",
        "destiny2.exe",
        "riotclient.exe",
        "leagueclient.exe",
        "league of legends.exe",
        "faceitclient.exe",
        "esportal.exe"
    };

    private static bool IsProtectedProcess(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out int processId);
            using var process = Process.GetProcessById(processId);
            return ProtectedProcesses.Contains(process.ProcessName.ToLower());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao verificar processo protegido: {ex.Message}");
            return false;
        }
    }

    public MainWindow()
    {
        SetConsoleCtrlHandler(_handler, true);
        isInitializing = true;
        config = AppConfig.Load();
        
        // Inicializa os componentes principais
        layout = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new(5),
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        updateTimer = new() { Interval = 5000 };

        InitializeWindow();
        ConfigureWindow();
        SetupEventHandlers();
        RegisterWindowsHooks();

        this.Visible = false; // Começa invisível
        
        // Verifica o estado inicial após um breve delay
        Timer initialCheck = new() { Interval = 100 };
        initialCheck.Tick += (s, e) =>
        {
            this.Visible = !IsAnyFullscreenWindowOnTaskbarMonitor();
            initialCheck.Dispose();
        };
        initialCheck.Start();
    }

    private void ConfigureWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        BackColor = Color.Black;
        TransparencyKey = Color.Black;
        Opacity = 0.8;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        
        Controls.Add(layout);
    }

    private void SetupEventHandlers()
    {
        updateTimer.Tick += (s, e) => UpdateWindowsList();
        
        Shown += (s, e) =>
        {
            UpdateWindowsList();
            updateTimer.Start();
            isInitializing = false;
            HideSystemTaskbar();
        };

        FormClosing += (s, e) => RestoreSystemTaskbar();
        LocationChanged += (s, e) => SaveWindowPosition();
    }

    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private void RestoreWindowPosition()
    {
        var screens = Screen.AllScreens;
        LogDebug("=== Detalhes dos Screens ===");
        foreach (var screen in screens)
        {
            LogDebug($"Screen: {screen.DeviceName} Config: {config.WindowPosition.MonitorDevice}");
        }
        LogDebug("===========================");

        // Tenta encontrar o monitor pelo DeviceName
        Screen targetScreen = screens.FirstOrDefault(s => s.DeviceName == config.WindowPosition.MonitorDevice)
            ?? Screen.PrimaryScreen
            ?? screens[0];

        LogDebug($"Usando monitor: {targetScreen.DeviceName}");

        // Calcula a posição absoluta
        int absoluteX = targetScreen.Bounds.Left + config.WindowPosition.X;
        int absoluteY = targetScreen.Bounds.Top + config.WindowPosition.Y;

        LogDebug($"Tentando posicionar em - X:{absoluteX}, Y:{absoluteY}");

        // Usa SetWindowPos para posicionar a janela
        bool result = SetWindowPos(
            this.Handle,
            IntPtr.Zero,
            absoluteX,
            absoluteY,
            0, 0,  // Mantém o tamanho atual
            SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW
        );

        if (!result)
        {
            int error = Marshal.GetLastWin32Error();
            LogDebug($"SetWindowPos falhou com erro: {error}");

            // Fallback para o método tradicional
            this.Location = new Point(absoluteX, absoluteY);
        }

        LogDebug($"Posição final: X={this.Location.X}, Y={this.Location.Y} em {targetScreen.DeviceName}");
    }

    private void SaveWindowPosition()
    {
        if (isInitializing)
        {
            LogDebug("Ignorando salvamento durante inicialização");
            return;
        }

        var currentScreen = Screen.FromPoint(this.Location);

        // Converte para coordenadas relativas ao monitor
        var relativeX = this.Location.X - currentScreen.Bounds.Left;
        var relativeY = this.Location.Y - currentScreen.Bounds.Top;

        LogDebug($"=== Salvando Posição ===");
        LogDebug($"Posição absoluta: X={this.Location.X}, Y={this.Location.Y}");
        LogDebug($"Monitor atual: {currentScreen.DeviceName}");
        LogDebug($"Posição relativa: X={relativeX}, Y={relativeY}");

        // Só salva se realmente mudou de monitor ou posição
        if (currentScreen.DeviceName != config.WindowPosition.MonitorDevice ||
            Math.Abs(relativeX - config.WindowPosition.X) > 1 ||  // Tolerância de 1 pixel
            Math.Abs(relativeY - config.WindowPosition.Y) > 1)    // para evitar flutuações
        {
            config.WindowPosition.MonitorDevice = currentScreen.DeviceName;
            config.WindowPosition.X = relativeX;
            config.WindowPosition.Y = relativeY;
            config.Save();
            LogDebug($"Nova posição salva em {currentScreen.DeviceName}");
        }
    }

    // Renomear WindowState para TaskWindowState para evitar conflito
    private class TaskWindowState
    {
        public required IntPtr Handle { get; set; }
        public required string Title { get; set; }
        public required string ExePath { get; set; }
        public bool IsActive { get; set; }
        public required TaskButton Button { get; set; }
    }

    private Dictionary<Button, IntPtr> windowHandles = new();
    private IntPtr activeWindow = IntPtr.Zero;
    private Dictionary<Button, string> executablePaths = new();

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    private WinEventDelegate? shellProcDelegate;
    private IntPtr hookHandle = IntPtr.Zero;

    private void HideSystemTaskbar()
    {
        try
        {
            // Encontra a janela da taskbar
            taskbarHwnd = FindWindow("Shell_TrayWnd", string.Empty);
            if (taskbarHwnd != IntPtr.Zero)
            {
                // Esconde completamente
                SetWindowVisibility(taskbarHwnd, SW_HIDE);
            }
        }
        catch (Exception ex)
        {
            LogDebug($"Erro ao esconder taskbar: {ex.Message}");
        }
    }

    private void RestoreSystemTaskbar()
    {
        try
        {
            if (taskbarHwnd != IntPtr.Zero)
            {
                SetWindowVisibility(taskbarHwnd, SW_SHOW);
            }
        }
        catch (Exception ex)
        {
            LogDebug($"Erro ao restaurar taskbar: {ex.Message}");
        }
    }

    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const uint EVENT_SYSTEM_FOREGROUND = 3;
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_HIDE = 0x8003;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint EVENT_OBJECT_STATECHANGE = 0x800A;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private void UpdateWindowsList()
    {
        var currentWindows = GetAltTabWindows();
        var foregroundWindow = GetForegroundWindow();
        bool needsLayout = false;

        Monitor.Enter(stateLock);
        try
        {
            // Remove janelas fechadas
            var closedWindows = windowStates.Keys
                .Where(handle => !currentWindows.Contains(handle))
                .ToList();

            foreach (var handle in closedWindows)
            {
                var state = windowStates[handle];
                if (state.Button != null)
                {
                    layout.Controls.Remove(state.Button);
                    state.Button.Dispose();
                }
                windowStates.Remove(handle);
                needsLayout = true;
            }

            // Atualiza estados ativos
            foreach (var state in windowStates.Values)
            {
                bool isActive = state.Handle == foregroundWindow;
                if (state.IsActive != isActive)
                {
                    state.IsActive = isActive;
                    state.Button.IsActive = isActive;
                }
            }

            // Adiciona novas janelas
            foreach (var handle in currentWindows)
            {
                if (!windowStates.ContainsKey(handle))
                {
                    string title = GetWindowText(handle);
                    if (string.IsNullOrEmpty(title)) continue;
                    if (config.IgnoredApps.Contains(title)) continue;

                    GetWindowThreadProcessId(handle, out int processId);
                    string exePath = IconHelper.GetProcessExecutablePath(processId);
                    if (string.IsNullOrEmpty(exePath)) continue;

                    var state = new TaskWindowState
                    {
                        Handle = handle,
                        Title = title,
                        ExePath = exePath,
                        Button = CreateWindowButton(title, exePath, handle),
                        IsActive = handle == foregroundWindow
                    };

                    state.Button.IsActive = state.IsActive;
                    windowStates[handle] = state;
                    layout.Controls.Add(state.Button);
                    needsLayout = true;
                }
            }
        }
        finally
        {
            Monitor.Exit(stateLock);
        }

        if (needsLayout)
        {
            this.BeginInvoke(new Action(() =>
            {
                layout.SuspendLayout();
                layout.ResumeLayout(true);
                this.PerformLayout();
            }));
        }
    }

    private TaskButton CreateWindowButton(string title, string exePath, IntPtr handle)
    {
        var btn = new TaskButton(exePath, title);

        btn.IgnoreAppRequested += (s, appTitle) =>
        {
            config.IgnoreApp(appTitle);
            ProcessWindowRemoval(handle);
        };

        btn.RestoreAppRequested += (s, appTitle) =>
        {
            config.RestoreApp(appTitle);
            UpdateWindowsList(); // Atualiza a lista para mostrar o app restaurado
        };

        btn.GetIgnoredApps = (s, e) => config.IgnoredApps;

        btn.MinimizeRequested += (s, e) => MinimizeWindow(handle);
        btn.ActivateRequested += (s, e) => ActivateWindow(handle);
        btn.KillProcessRequested += (s, e) => KillProcess(handle);

        return btn;
    }

    private void MinimizeWindow(IntPtr hwnd)
    {
        try
        {
            if (!ShowWindow(hwnd, SW_MINIMIZE))
            {
                Debug.WriteLine($"Falha ao minimizar janela: {hwnd}");
                return;
            }

            if (windowStates.TryGetValue(hwnd, out var state))
            {
                state.IsActive = false;
                state.Button.IsActive = false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao minimizar janela: {ex.Message}");
        }
    }

    private void ActivateWindow(IntPtr hwnd)
    {
        try
        {
            WINDOWPLACEMENT placement = new() { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(hwnd, ref placement))
            {
                Debug.WriteLine($"Falha ao obter posição da janela: {hwnd}");
                return;
            }

            if (placement.showCmd == SW_SHOWMINIMIZED)
            {
                if (!ShowWindow(hwnd, SW_RESTORE))
                {
                    Debug.WriteLine($"Falha ao restaurar janela: {hwnd}");
                    return;
                }
            }

            if (!SetForegroundWindow(hwnd))
            {
                Debug.WriteLine($"Falha ao trazer janela para frente: {hwnd}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao ativar janela: {ex.Message}");
        }
    }

    private void KillProcess(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out int processId);
            using var process = Process.GetProcessById(processId);
            
            if (!process.HasExited)
            {
                process.Kill();
                ProcessWindowRemoval(hwnd);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao matar processo: {ex.Message}");
        }
    }

    private static HashSet<IntPtr> GetAltTabWindows()
    {
        HashSet<IntPtr> windows = [];

        EnumDesktopWindows(IntPtr.Zero, (hwnd, lParam) =>
        {
            if (IsAltTabWindow(hwnd) && !IsProtectedProcess(hwnd))
            {
                windows.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static bool IsAltTabWindow(IntPtr hWnd)
    {
        // Verifica se a janela é visível
        if (!IsWindowVisible(hWnd)) return false;

        // Verifica se a janela tem owner (janelas filhas como tooltips têm owner)
        if (GetWindow(hWnd, 4) != IntPtr.Zero) return false;

        // Obtém os estilos da janela
        int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false; // Ignora tool windows

        return true;
    }

    // Adicione estas constantes e PInvokes
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    // PInvoke para API do Windows
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static string GetWindowText(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;

        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private void LogDebug(string message)
    {
#if !RELEASE
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(LogFile, $"[{timestamp}] {message}\n");
            Debug.WriteLine($"[{timestamp}] {message}");
        }
        catch
        {
            // Ignora erros de logging
        }
#endif
    }

    private void LogMonitorInfo()
    {
        var screens = Screen.AllScreens;
        LogDebug($"Total de monitores: {screens.Length}");
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            LogDebug($"Monitor {i + 1} ({screen.DeviceName}):");
            LogDebug($"  Bounds: {screen.Bounds} (Absoluto)");
            LogDebug($"  Working Area: {screen.WorkingArea}");
            LogDebug($"  Primary: {screen.Primary}");
            LogDebug($"  BitsPerPixel: {screen.BitsPerPixel}");
        }
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public Point ptMinPosition;
        public Point ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    private const int SW_SHOWMINIMIZED = 2;
    private const int SW_RESTORE = 9;
    private const int SW_MINIMIZE = 6;

    private bool IsAnyFullscreenWindowOnTaskbarMonitor()
    {
        var taskbarScreen = Screen.FromHandle(this.Handle);
        var windows = GetAltTabWindows();

        foreach (var hwnd in windows)
        {
            // Ignora a própria janela
            if (hwnd == this.Handle) continue;

            WINDOWPLACEMENT placement = new() { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            GetWindowPlacement(hwnd, ref placement);

            // Ignora janelas minimizadas
            if (placement.showCmd == SW_SHOWMINIMIZED) continue;

            // Verifica se a janela está no mesmo monitor da taskbar
            var windowScreen = Screen.FromHandle(hwnd);
            if (windowScreen.DeviceName != taskbarScreen.DeviceName) continue;

            // Obtém o retângulo da janela
            RECT rect;
            if (GetWindowRect(hwnd, out rect))
            {
                var windowBounds = new Rectangle(rect.Left, rect.Top, 
                    rect.Right - rect.Left, rect.Bottom - rect.Top);

                // Verifica se a janela ocupa toda a área do monitor
                if (windowBounds.Width >= windowScreen.Bounds.Width &&
                    windowBounds.Height >= windowScreen.Bounds.Height)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // Modifique o WinEventProc para verificar fullscreen
    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero || idObject != 0) return;

        // Ignora eventos de processos protegidos
        if (IsProtectedProcess(hwnd)) return;

        // Para eventos de foco ou mudança de tamanho, verifica fullscreen
        if (eventType == EVENT_SYSTEM_FOREGROUND || 
            eventType == EVENT_OBJECT_STATECHANGE)
        {
            this.BeginInvoke(new Action(() =>
            {
                bool shouldHide = IsAnyFullscreenWindowOnTaskbarMonitor();
                this.Visible = !shouldHide;
            }));
        }

        // Para eventos de foco, processa imediatamente sem BeginInvoke
        if (eventType == EVENT_SYSTEM_FOREGROUND)
        {
            string focusTitle = GetWindowText(hwnd);
            if (!string.IsNullOrEmpty(focusTitle))
            {
                LogDebug($"WinEventProc - Mudança de foco para: {focusTitle}");
                UpdateActiveWindow(hwnd);
            }
            return;
        }

        // Para outros eventos, usa BeginInvoke
        this.BeginInvoke(new Action(() => 
        {
            try 
            {
                switch (eventType)
                {
                    case EVENT_OBJECT_CREATE:
                    case EVENT_OBJECT_SHOW:
                        string newTitle = GetWindowText(hwnd);
                        if (!string.IsNullOrEmpty(newTitle) && IsAltTabWindow(hwnd))
                        {
                            LogDebug($"WinEventProc - Nova janela detectada: {newTitle}");
                            UpdateWindowsList();
                        }
                        break;

                    case EVENT_OBJECT_DESTROY:
                    case EVENT_OBJECT_HIDE:
                        ProcessWindowRemoval(hwnd);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Erro ao processar evento de janela: {ex.Message}");
            }
        }));
    }

    // Método separado para remover janelas
    private void ProcessWindowRemoval(IntPtr hwnd)
    {
        Monitor.Enter(stateLock);
        try
        {
            if (windowStates.TryGetValue(hwnd, out var state))
            {
                LogDebug($"WinEventProc - Janela fechada/ocultada: {state.Title}");
                
                try
                {
                    layout.Controls.Remove(state.Button);
                    state.Button.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro ao remover botão: {ex.Message}");
                }
                finally
                {
                    windowStates.Remove(hwnd);
                }
                
                try
                {
                    layout.PerformLayout();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro ao atualizar layout: {ex.Message}");
                }
            }
        }
        finally
        {
            Monitor.Exit(stateLock);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private void UpdateActiveWindow(IntPtr newActiveWindow)
    {
        if (isInitializing) return;

        if (!IsWindow(newActiveWindow) || !IsWindowVisible(newActiveWindow)) return;

        LogDebug($"UpdateActiveWindow - Nova janela ativa: {GetWindowText(newActiveWindow)}");

        Monitor.Enter(stateLock);
        try
        {
            // Otimização: Verifica se já é a janela ativa
            var currentActive = windowStates.Values.FirstOrDefault(w => w.IsActive);
            if (currentActive?.Handle == newActiveWindow) return;

            // Desativa apenas a janela atual (em vez de iterar por todas)
            if (currentActive != null)
            {
                currentActive.IsActive = false;
                currentActive.Button.IsActive = false;
            }

            // Ativa a nova janela
            if (windowStates.TryGetValue(newActiveWindow, out var activeState))
            {
                activeState.IsActive = true;
                activeState.Button.IsActive = true;
                
                // Força atualização visual imediata
                activeState.Button.Invalidate();
            }
        }
        finally
        {
            Monitor.Exit(stateLock);
        }
    }

    // Adicione estes P/Invokes
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainWindow());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                // Limpa todos os hooks
                if (hookHandle != IntPtr.Zero)
                {
                    try
                    {
                        if (!UnhookWinEvent(hookHandle))
                        {
                            Debug.WriteLine("Falha ao remover hook de eventos");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Erro ao remover hook: {ex.Message}");
                    }
                    finally
                    {
                        hookHandle = IntPtr.Zero;
                    }
                }

                Monitor.Enter(stateLock);
                try
                {
                    foreach (var state in windowStates.Values)
                    {
                        try
                        {
                            state.Button?.Image?.Dispose();
                            state.Button?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Erro ao descartar botão: {ex.Message}");
                        }
                    }
                    windowStates.Clear();
                }
                finally
                {
                    Monitor.Exit(stateLock);
                }

                try
                {
                    layout?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro ao descartar layout: {ex.Message}");
                }

                try
                {
                    updateTimer?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro ao descartar timer: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao descartar recursos: {ex.Message}");
            }
        }
        base.Dispose(disposing);
    }

    private void InitializeWindow()
    {
        LogMonitorInfo();
        RestoreWindowPosition();
    }

    private void RegisterWindowsHooks()
    {
        // Registra múltiplos hooks de eventos do Windows
        shellProcDelegate = new WinEventDelegate(WinEventProc);
        
        // Hook para mudança de foco
        hookHandle = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, shellProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        
        // Hook para criação de janelas
        var createHook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE,
            IntPtr.Zero, shellProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        
        // Hook para exibição de janelas
        var showHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW,
            IntPtr.Zero, shellProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Hook para destruição de janelas
        var destroyHook = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY,
            IntPtr.Zero, shellProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Hook para ocultação de janelas
        var hideHook = SetWinEventHook(EVENT_OBJECT_HIDE, EVENT_OBJECT_HIDE,
            IntPtr.Zero, shellProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Hook para mudanças de posição/tamanho
        var locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, shellProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Hook para mudanças de estado
        var stateHook = SetWinEventHook(EVENT_OBJECT_STATECHANGE, EVENT_OBJECT_STATECHANGE,
            IntPtr.Zero, shellProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }
}
