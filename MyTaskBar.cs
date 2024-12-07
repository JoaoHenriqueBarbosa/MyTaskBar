using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;
using System.Diagnostics;

/// <summary>
/// Representa a janela principal do aplicativo de gerenciamento de janelas.
/// Fornece uma interface minimalista para visualizar e gerenciar janelas ativas no Windows.
/// </summary>
public class MainWindow : Form
{
    private FlowLayoutPanel layout;
    private Timer updateTimer;
    private HashSet<IntPtr> windowsCache = new HashSet<IntPtr>();
    private Dictionary<Button, IntPtr> windowHandles = new Dictionary<Button, IntPtr>();
    private IntPtr activeWindow = IntPtr.Zero;
    private AppConfig config;
    private Dictionary<Button, string> executablePaths = new();
#if !RELEASE
    private static readonly string LogFile = "taskbar.log";
#endif
    private bool isInitializing = true;

    [DllImport("Kernel32")]
    private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

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

    private delegate bool EventHandler(CtrlType sig);
    private static EventHandler _handler = new EventHandler(Handler);

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
        // Cleanup antes de fechar
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

    private const int ABM_SETSTATE = 0x0000000A;
    private const int ABS_AUTOHIDE = 0x0000001;
    private const int ABS_ALWAYSONTOP = 0x0000002;

    private void DisableTaskbarAutoHide()
    {
        try
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.lParam = IntPtr.Zero;  // Remove todas as flags

            SHAppBarMessage(ABM_SETSTATE, ref abd);
        }
        catch (Exception ex)
        {
            LogDebug($"Erro ao desativar autohide da taskbar: {ex.Message}");
        }
    }

    private void EnableTaskbarAutoHide()
    {
        try
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.lParam = (IntPtr)(ABS_AUTOHIDE | ABS_ALWAYSONTOP);  // Restaura as flags originais

            SHAppBarMessage(ABM_SETSTATE, ref abd);
        }
        catch (Exception ex)
        {
            LogDebug($"Erro ao restaurar autohide da taskbar: {ex.Message}");
        }
    }

    private void ForceTaskbarHide()
    {
        try
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.lParam = (IntPtr)ABS_AUTOHIDE;  // Força o modo autohide

            SHAppBarMessage(ABM_SETSTATE, ref abd);
        }
        catch (Exception ex)
        {
            LogDebug($"Erro ao forçar hide da taskbar: {ex.Message}");
        }
    }

    private void RestoreTaskbarState()
    {
        try
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.lParam = (IntPtr)(ABS_ALWAYSONTOP);  // Restaura apenas AlwaysOnTop sem autohide

            SHAppBarMessage(ABM_SETSTATE, ref abd);
        }
        catch (Exception ex)
        {
            LogDebug($"Erro ao restaurar estado da taskbar: {ex.Message}");
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    private static extern bool SetWindowVisibility(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private IntPtr taskbarHwnd;

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

    private const int WH_SHELL = 10;
    private const int HSHELL_WINDOWACTIVATED = 4;
    private const int HSHELL_RUDEAPPACTIVATED = 32772;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const uint EVENT_SYSTEM_FOREGROUND = 3;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    private WinEventDelegate? shellProcDelegate;
    private IntPtr hookHandle = IntPtr.Zero;

    public MainWindow()
    {
        SetConsoleCtrlHandler(_handler, true);
        isInitializing = true;
        config = AppConfig.Load();
        LogMonitorInfo();
        InitializeWindow();
        RestoreWindowPosition();

        // Configuração da Janela
        this.FormBorderStyle = FormBorderStyle.None;
        this.TopMost = true;
        this.BackColor = Color.Black;
        this.TransparencyKey = Color.Black;
        this.Opacity = 0.8;
        this.AutoSize = true;
        this.AutoSizeMode = AutoSizeMode.GrowAndShrink;

        layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(5),
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        this.Controls.Add(layout);

        // Registra o hook de eventos do Windows
        shellProcDelegate = new WinEventDelegate(WinEventProc);
        hookHandle = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, shellProcDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Atualiza a lista de janelas a cada 5 segundos
        updateTimer = new Timer
        {
            Interval = 5000 // 5 segundos
        };
        updateTimer.Tick += (s, e) => UpdateWindowsList();

        this.Shown += (s, e) =>
        {
            UpdateWindowsList(); // Faz a primeira atualização
            updateTimer.Start(); // Inicia o timer
            isInitializing = false;
            HideSystemTaskbar();  // Esconde completamente a taskbar
        };

        this.FormClosing += (s, e) =>
        {
            RestoreSystemTaskbar();  // Restaura a taskbar ao fechar
        };
    }

    private void InitializeWindow()
    {
        this.LocationChanged += (s, e) => SaveWindowPosition();

        // Removida a configuração dos botões pois agora usamos o ButtonHelper
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

    // Atualizar a referência para o novo nome
    private Dictionary<IntPtr, TaskWindowState> windowStates = new();
    private readonly object stateLock = new object();

    /// <summary>
    /// Atualiza a lista de janelas e seus estados.
    /// Verifica mudanças a cada intervalo definido.
    /// </summary>
    private void UpdateWindowsList()
    {
        var currentWindows = GetAltTabWindows();
        var foregroundWindow = GetForegroundWindow();
        bool needsLayout = false;

        lock (stateLock)
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

        btn.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Middle)
            {
                if (!config.IgnoredApps.Contains(title))
                {
                    config.IgnoredApps.Add(title);
                    config.Save();

                    if (windowStates.TryGetValue(handle, out var state))
                    {
                        layout.Controls.Remove(state.Button);
                        state.Button.Dispose();
                        windowStates.Remove(handle);
                        this.PerformLayout();
                    }
                }
            }
        };

        btn.Click += (s, e) => FocusWindow(handle);
        return btn;
    }

    private void FocusWindow(IntPtr hwnd)
    {
        bool wasActive = false;
        TaskWindowState? state = null;

        lock (stateLock)
        {
            // Verifica se a janela está ativa usando nosso estado interno
            if (windowStates.TryGetValue(hwnd, out state) && state != null)
            {
                wasActive = state.IsActive;
                LogDebug($"Estado interno - IsActive: {wasActive}, Title: {state.Title}");
            }
            else
            {
                return; // Se não encontrou o estado, não faz nada
            }

            if (wasActive)
            {
                LogDebug("Minimizando janela ativa");
                // Força minimização
                ShowWindow(hwnd, SW_MINIMIZE);

                // Desativa o estado visual do botão
                state.IsActive = false;
                state.Button.IsActive = false;

                // Não precisamos mais desativar todas as janelas aqui
                return;
            }
            else
            {
                // Resto da lógica para quando a janela não está ativa...
                var previousActive = windowStates.Values.FirstOrDefault(w => w.IsActive);
                if (previousActive != null)
                {
                    previousActive.IsActive = false;
                    previousActive.Button.IsActive = false;
                }

                state.IsActive = true;
                state.Button.IsActive = true;
            }
        }

        // Verificar o estado atual da janela
        WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
        placement.length = Marshal.SizeOf(placement);
        GetWindowPlacement(hwnd, ref placement);

        // Se estiver minimizada, restaurar
        if (placement.showCmd == SW_SHOWMINIMIZED)
        {
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
        // Caso contrário, apenas trazer para frente
        else
        {
            SetForegroundWindow(hwnd);
        }
    }

    private HashSet<IntPtr> GetAltTabWindows()
    {
        HashSet<IntPtr> windows = new HashSet<IntPtr>();

        EnumDesktopWindows(IntPtr.Zero, (hwnd, lParam) =>
        {
            if (IsAltTabWindow(hwnd))
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

        StringBuilder sb = new StringBuilder(length + 1);
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
    private const int SW_FORCEMINIMIZE = 11;

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND && hwnd != IntPtr.Zero)
        {
            string windowTitle = GetWindowText(hwnd);
            // Ignora eventos com título vazio
            if (string.IsNullOrEmpty(windowTitle)) return;

            LogDebug($"WinEventProc - Evento de mudança de foco para: {windowTitle}");
            this.BeginInvoke(new Action(() => 
            {
                try 
                {
                    UpdateActiveWindow(hwnd);
                }
                catch (Exception ex)
                {
                    LogDebug($"Erro ao atualizar janela ativa: {ex.Message}");
                }
            }));
        }
    }

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private void UpdateActiveWindow(IntPtr newActiveWindow)
    {
        if (isInitializing) return;

        LogDebug($"UpdateActiveWindow - Nova janela ativa: {GetWindowText(newActiveWindow)}");

        lock (stateLock)
        {
            bool stateChanged = false;

            // Desativa a janela anterior
            foreach (var state in windowStates.Values)
            {
                if (state.IsActive)
                {
                    LogDebug($"Desativando janela: {state.Title}");
                    state.IsActive = false;
                    state.Button.IsActive = false;
                    stateChanged = true;
                }
            }

            // Ativa a nova janela se ela existir em nossa lista
            if (windowStates.TryGetValue(newActiveWindow, out var activeState))
            {
                LogDebug($"Ativando janela: {activeState.Title}");
                activeState.IsActive = true;
                activeState.Button.IsActive = true;
                stateChanged = true;
            }

            if (!stateChanged)
            {
                LogDebug("Nenhuma mudança de estado detectada");
            }
        }
    }

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
            if (hookHandle != IntPtr.Zero)
            {
                UnhookWinEvent(hookHandle);
                hookHandle = IntPtr.Zero;
            }

            lock (stateLock)
            {
                foreach (var state in windowStates.Values)
                {
                    state.Button?.Image?.Dispose();
                    state.Button?.Dispose();
                }
                windowStates.Clear();
            }

            layout?.Dispose();
        }
        base.Dispose(disposing);
    }
}
