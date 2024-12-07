namespace MyTaskBar.Components;

using System.ComponentModel;
using System.Diagnostics;

public class TaskButton : Button
{
    private readonly string? m_ExePath;
    private readonly string? m_Title;
    private const int IconSize = 24;
    private ToolTip? m_Tooltip;
    private const int MaxTooltipLength = 30;
    private ContextMenuStrip? contextMenu;

    // Delegate para obter a lista de apps ignorados
    public delegate IEnumerable<string> GetIgnoredAppsDelegate(object sender, EventArgs e);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public GetIgnoredAppsDelegate? GetIgnoredApps { get; set; }

    // Constantes de opacidade
    private const int DefaultOpacity = 70;      // Opacidade padrão/normal
    private const int ActiveOpacity = 120;      // Opacidade quando ativo
    private const int HoverOpacity = 95;        // Opacidade durante hover
    
    private bool m_IsActive;
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsActive
    {
        get => m_IsActive;
        set
        {
            if (m_IsActive != value)
            {
                m_IsActive = value;
                LoadIcon(m_IsActive ? ActiveOpacity : DefaultOpacity);
                Invalidate();
            }
        }
    }

    public TaskButton(string exePath, string title)
    {
        m_ExePath = exePath;
        m_Title = title;
        m_IsActive = false;
        
        contextMenu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(240, 240, 240),
            RenderMode = ToolStripRenderMode.Professional,
            Renderer = new CustomRenderer(),
            ShowImageMargin = false,
            Padding = new(4),
            Font = new("Segoe UI", 9f, FontStyle.Regular)
        };

        var ignoreItem = new ToolStripMenuItem("Ignorar App")
        {
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(240, 240, 240)
        };
        ignoreItem.Click += (s, e) => 
        {
            if (m_Title != null)
            {
                IgnoreAppRequested?.Invoke(this, m_Title);
            }
        };

        var ignoredAppsMenu = new ToolStripMenuItem("Apps Ignorados")
        {
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(240, 240, 240)
        };
        
        // Evento para atualizar a lista quando o menu é aberto
        contextMenu.Opening += (s, e) => 
        {
            UpdateIgnoredAppsList(ignoredAppsMenu);
        };

        var exitItem = new ToolStripMenuItem("Sair do MyTaskBar")
        {
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(240, 240, 240)
        };
        exitItem.Click += (s, e) => Application.Exit();
        
        contextMenu.Items.AddRange([ignoreItem, ignoredAppsMenu, exitItem]);
        this.ContextMenuStrip = contextMenu;
        
        Size = new Size(32, 32);
        Text = null;
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        Margin = new Padding(0, 0, 0, 0);
        ImageAlign = ContentAlignment.MiddleCenter;
        TextAlign = ContentAlignment.MiddleCenter;
        
        // Configuração padrão da aparência
        FlatAppearance.BorderSize = 0;
        FlatAppearance.BorderColor = Color.FromArgb(255, 255, 255, 255);
        FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 255, 255, 255);
        FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 255, 255, 255);

        // Configurar eventos de mouse
        this.MouseEnter += TaskButton_MouseEnter;
        this.MouseLeave += TaskButton_MouseLeave;
        this.Disposed += TaskButton_Disposed;

        // Carregar ícone com opacidade padrão
        LoadIcon(DefaultOpacity);

        // Remover handler do MouseDown já que agora usamos menu de contexto
        this.Click += (s, e) => OnButtonClick();
    }

    // Evento para notificar quando o usuário quer ignorar o app
    public event EventHandler<string>? IgnoreAppRequested;

    // Evento para notificar quando um app deve ser restaurado
    public event EventHandler<string>? RestoreAppRequested;

    private void OnButtonClick()
    {
        if (m_IsActive)
        {
            MinimizeRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ActivateRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    // Eventos para notificar ações do botão
    public event EventHandler? MinimizeRequested;
    public event EventHandler? ActivateRequested;

    private DateTime lastRightClick = DateTime.MinValue;
    private const int DoubleClickTime = 500; // Tempo máximo entre cliques em milissegundos

    private void TaskButton_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var now = DateTime.Now;
            if ((now - lastRightClick).TotalMilliseconds <= DoubleClickTime)
            {
                Application.Exit();
            }
            lastRightClick = now;
        }
    }

    private string GetTruncatedTitle()
    {
        if (string.IsNullOrEmpty(m_Title) || m_Title.Length <= MaxTooltipLength)
            return m_Title ?? string.Empty;
        
        return m_Title[..(MaxTooltipLength - 3)] + "...";
    }

    private void TaskButton_MouseEnter(object? sender, EventArgs e)
    {
        if (!m_IsActive)
        {
            LoadIcon(HoverOpacity);
        }

        if (m_Tooltip == null && !string.IsNullOrEmpty(m_Title))
        {
            var truncatedTitle = GetTruncatedTitle();
            
            m_Tooltip = new ToolTip
            {
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(240, 240, 240),
                IsBalloon = false,
                ShowAlways = true,
                UseAnimation = true,
                InitialDelay = 400,
                AutoPopDelay = 5000,
                OwnerDraw = true
            };

            m_Tooltip.Draw += (s, e) =>
            {
                e.DrawBackground();
                using var font = new Font("Segoe UI", 9f, FontStyle.Regular);
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
                TextRenderer.DrawText(e.Graphics, truncatedTitle, font, e.Bounds, Color.FromArgb(240, 240, 240), flags);
            };
            
            m_Tooltip.Popup += (s, e) =>
            {
                using var font = new Font("Segoe UI", 9f);
                var size = TextRenderer.MeasureText(truncatedTitle, font);
                e.ToolTipSize = new Size(size.Width + 16, size.Height + 8);
            };

            // Calcular o deslocamento horizontal para centralizar
            using var font = new Font("Segoe UI", 9f);
            var textSize = TextRenderer.MeasureText(truncatedTitle, font);
            int xOffset = -(textSize.Width / 2) + (Width / 2) - 8;
            
            m_Tooltip.Show(truncatedTitle, this, 
                xOffset,  // Centralizar horizontalmente
                Height + 2  // Logo abaixo do botão
            );
        }
    }

    private void TaskButton_MouseLeave(object? sender, EventArgs e)
    {
        try
        {
            if (!m_IsActive)
            {
                LoadIcon(DefaultOpacity);
            }

            if (m_Tooltip != null)
            {
                try
                {
                    m_Tooltip.RemoveAll();
                    m_Tooltip.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Tooltip já foi descartado
                }
                finally
                {
                    m_Tooltip = null;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao processar MouseLeave: {ex.Message}");
        }
    }

    private void TaskButton_Disposed(object? sender, EventArgs e)
    {
        try
        {
            if (m_Tooltip != null)
            {
                try
                {
                    m_Tooltip.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Tooltip já foi descartado
                }
                finally
                {
                    m_Tooltip = null;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao processar Disposed: {ex.Message}");
        }
    }

    public void LoadIcon(int opacity = DefaultOpacity)
    {
        if (m_ExePath == null) return;

        try
        {
            using var icon = IconHelper.ExtractIconFromFile(m_ExePath, opacity: opacity);
            if (icon != null)
            {
                try
                {
                    Image?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Imagem já foi descartada
                }

                var bitmap = new Bitmap(icon.ToBitmap(), new Size(IconSize, IconSize));
                Image = bitmap;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao carregar ícone de {m_ExePath}: {ex.Message}");
        }
    }

    protected override bool ShowFocusCues => false;

    public override void NotifyDefault(bool value) => base.NotifyDefault(false);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                try
                {
                    contextMenu?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Menu já foi descartado
                }

                try
                {
                    m_Tooltip?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Tooltip já foi descartado
                }

                try
                {
                    Image?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Imagem já foi descartada
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao descartar recursos: {ex.Message}");
            }
        }
        base.Dispose(disposing);
    }

    private void UpdateIgnoredAppsList(ToolStripMenuItem ignoredAppsMenu)
    {
        if (ignoredAppsMenu == null || GetIgnoredApps == null) return;

        ignoredAppsMenu.DropDownItems.Clear();
        var ignoredApps = GetIgnoredApps(this, EventArgs.Empty);
        
        if (!ignoredApps.Any())
        {
            var noAppsItem = new ToolStripMenuItem("Nenhum app ignorado")
            {
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(120, 120, 120),
                Enabled = false
            };
            ignoredAppsMenu.DropDownItems.Add(noAppsItem);
            return;
        }

        foreach (var app in ignoredApps)
        {
            var item = new ToolStripMenuItem(app)
            {
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(240, 240, 240)
            };
            item.Click += (s, e) => RestoreAppRequested?.Invoke(this, app);
            ignoredAppsMenu.DropDownItems.Add(item);
        }
    }

    private class CustomColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(30, 30, 30);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(30, 30, 30);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(30, 30, 30);
        public override Color MenuItemBorder => Color.FromArgb(60, 60, 60);
        public override Color MenuBorder => Color.FromArgb(60, 60, 60);
        public override Color ToolStripDropDownBackground => Color.FromArgb(20, 20, 20);
        public override Color ImageMarginGradientBegin => Color.FromArgb(20, 20, 20);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(20, 20, 20);
        public override Color ImageMarginGradientEnd => Color.FromArgb(20, 20, 20);
    }

    private class CustomRenderer : ToolStripProfessionalRenderer
    {
        public CustomRenderer() : base(new CustomColorTable()) { }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(240, 240, 240);
            base.OnRenderArrow(e);
        }
    }
} 