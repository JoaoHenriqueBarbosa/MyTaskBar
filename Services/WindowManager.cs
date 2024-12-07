namespace MyTaskBar.Services;

using MyTaskBar.Components;
using MyTaskBar.Native;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

public sealed class WindowManager : IDisposable
{
    private readonly Action<string> logDebug;
    private readonly HashSet<string> protectedProcesses = new(DefaultProtectedProcesses);
    private readonly ConcurrentDictionary<IntPtr, TaskWindowState> windowStates = new();
    private bool disposed;

    public WindowManager(Action<string> logger)
    {
        logDebug = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public sealed class TaskWindowState
    {
        public required IntPtr Handle { get; set; }
        public required string Title { get; set; }
        public required string ExePath { get; set; }
        public bool IsActive { get; set; }
        public required TaskButton Button { get; set; }
    }

    private static readonly HashSet<string> DefaultProtectedProcesses = new()
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

    public void UpdateWindow(IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        try
        {
            if (!User32.IsWindow(hwnd))
            {
                logDebug($"Handle inválido: {hwnd}");
                return;
            }

            var title = User32.GetWindowText(hwnd);
            if (string.IsNullOrEmpty(title))
            {
                logDebug($"Título vazio para hwnd: {hwnd}");
                return;
            }

            if (User32.GetWindowThreadProcessId(hwnd, out var processId) == 0)
            {
                logDebug($"Falha ao obter ID do processo para hwnd: {hwnd}");
                return;
            }

            var exePath = IconHelper.GetProcessExecutablePath(processId);
            if (string.IsNullOrEmpty(exePath))
            {
                logDebug($"Caminho do executável vazio para processo: {processId}");
                return;
            }

            if (windowStates.TryGetValue(hwnd, out var existingState))
            {
                existingState.Title = title;
                existingState.Button.Text = title;
            }
            else
            {
                var button = CreateWindowButton(title, exePath, hwnd);
                var state = new TaskWindowState
                {
                    Handle = hwnd,
                    Title = title,
                    ExePath = exePath,
                    Button = button
                };

                if (!windowStates.TryAdd(hwnd, state))
                {
                    logDebug($"Falha ao adicionar estado da janela: {hwnd}");
                    button.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            logDebug($"Erro ao atualizar janela: {ex.Message}");
        }
    }

    public void RemoveWindow(IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        try
        {
            if (windowStates.TryRemove(hwnd, out var state))
            {
                try
                {
                    state.Button?.Image?.Dispose();
                    state.Button?.Dispose();
                }
                catch (Exception ex)
                {
                    logDebug($"Erro ao descartar botão: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            logDebug($"Erro ao remover janela: {ex.Message}");
        }
    }

    private TaskButton CreateWindowButton(string title, string exePath, IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        try
        {
            var button = new TaskButton(title, exePath);
            button.MinimizeRequested += (s, e) => MinimizeWindow(hwnd);
            button.ActivateRequested += (s, e) => ActivateWindow(hwnd);
            return button;
        }
        catch (Exception ex)
        {
            logDebug($"Erro ao criar botão: {ex.Message}");
            throw;
        }
    }

    private void MinimizeWindow(IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        try
        {
            if (!User32.ShowWindow(hwnd, User32.SW_MINIMIZE))
            {
                logDebug($"Falha ao minimizar janela: {hwnd}");
            }
        }
        catch (Exception ex)
        {
            logDebug($"Erro ao minimizar janela: {ex.Message}");
        }
    }

    private void ActivateWindow(IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        try
        {
            var placement = new User32.WINDOWPLACEMENT 
            { 
                length = Marshal.SizeOf<User32.WINDOWPLACEMENT>() 
            };

            if (!User32.GetWindowPlacement(hwnd, ref placement))
            {
                logDebug($"Falha ao obter posição da janela: {hwnd}");
                return;
            }

            if (placement.showCmd == User32.SW_SHOWMINIMIZED)
            {
                if (!User32.ShowWindow(hwnd, User32.SW_RESTORE))
                {
                    logDebug($"Falha ao restaurar janela: {hwnd}");
                    return;
                }
            }

            if (!User32.SetForegroundWindow(hwnd))
            {
                logDebug($"Falha ao trazer janela para frente: {hwnd}");
            }
        }
        catch (Exception ex)
        {
            logDebug($"Erro ao ativar janela: {ex.Message}");
        }
    }

    public void AddWindow(TaskWindowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        windowStates.TryAdd(state.Handle, state);
    }

    public TaskWindowState? GetWindow(IntPtr handle)
    {
        windowStates.TryGetValue(handle, out var state);
        return state;
    }

    public IEnumerable<TaskWindowState> GetAllWindows()
    {
        return windowStates.Values;
    }

    public void Dispose()
    {
        if (disposed) return;

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
                    logDebug($"Erro ao descartar botão: {ex.Message}");
                }
            }
            windowStates.Clear();
        }
        catch (Exception ex)
        {
            logDebug($"Erro ao descartar recursos: {ex.Message}");
        }
        finally
        {
            disposed = true;
        }
    }
} 