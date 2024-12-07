using System;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;

public class TaskButton : Button
{
    private bool m_DrawFocusCue = false;
    private string? m_ExePath;
    private const int IconSize = 24;
    private ToolTip? m_Tooltip;
    private string? m_Title;
    private const int MaxTooltipLength = 30;

    // Constantes de opacidade
    private const int DefaultOpacity = 70;      // Opacidade padrão/normal
    private const int ActiveOpacity = 200;      // Opacidade quando ativo
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

        // Adiciona tratamento de duplo clique direito
        this.MouseDown += TaskButton_MouseDown;

        // Carregar ícone com opacidade padrão
        LoadIcon(DefaultOpacity);
    }

    private DateTime lastRightClick = DateTime.MinValue;
    private const int DoubleClickTime = 500; // Tempo máximo entre cliques em milissegundos

    private void TaskButton_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var now = DateTime.Now;
            if ((now - lastRightClick).TotalMilliseconds <= DoubleClickTime)
            {
                // Duplo clique detectado - fecha a aplicação
                Application.Exit();
            }
            lastRightClick = now;
        }
    }

    private string GetTruncatedTitle()
    {
        if (string.IsNullOrEmpty(m_Title) || m_Title.Length <= MaxTooltipLength)
            return m_Title ?? string.Empty;
        
        return m_Title.Substring(0, MaxTooltipLength - 3) + "...";
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
        if (!m_IsActive)
        {
            LoadIcon(DefaultOpacity);
        }

        if (m_Tooltip != null)
        {
            m_Tooltip.RemoveAll();
            m_Tooltip.Dispose();
            m_Tooltip = null;
        }
    }

    private void TaskButton_Disposed(object? sender, EventArgs e)
    {
        m_Tooltip?.Dispose();
    }

    public void LoadIcon(int opacity = DefaultOpacity)
    {
        if (m_ExePath == null) return;

        try
        {
            using var icon = IconHelper.ExtractIconFromFile(m_ExePath, opacity: opacity);
            if (icon != null)
            {
                Image?.Dispose();
                var bitmap = new Bitmap(icon.ToBitmap(), new Size(IconSize, IconSize));
                Image = bitmap;
            }
        }
        catch
        {
            // Falha ao carregar ícone será tratada pelo chamador
        }
    }

    protected override bool ShowFocusCues
    {
        get
        {
            m_DrawFocusCue = !ClientRectangle.Contains(PointToClient(MousePosition));
            return !IsHandleCreated;
        }
    }

    public override void NotifyDefault(bool value) => base.NotifyDefault(false);
} 