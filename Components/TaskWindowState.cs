namespace MyTaskBar.Components;

public class TaskWindowState
{
    public required IntPtr Handle { get; set; }
    public required string Title { get; set; }
    public required string ExePath { get; set; }
    public bool IsActive { get; set; }
    public required TaskButton Button { get; set; }
} 