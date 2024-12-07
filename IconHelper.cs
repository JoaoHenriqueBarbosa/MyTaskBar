using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;

public static class IconHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, 
        IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("psapi.dll")]
    private static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, 
        [Out] StringBuilder lpBaseName, uint nSize);

    [DllImport("gdi32.dll")]
    private static extern bool GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, 
        uint cScanLines, [Out] byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    public static Icon? ExtractIconFromFile(string filePath, int opacity = 128, int maxAttempts = 10)
    {
        opacity = Math.Max(0, Math.Min(255, opacity));
        
        int attempt = 1;
        while (attempt <= maxAttempts)
        {
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(filePath);
                if (icon != null)
                {
                    const int targetSize = 24;
                    const int maxIconSize = 16;
                    const int cornerRadius = 6;
                    var resultBitmap = new Bitmap(targetSize, targetSize);
                    
                    using (var g = Graphics.FromImage(resultBitmap))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        
                        // Criar retângulo arredondado
                        using (var path = new GraphicsPath())
                        {
                            path.AddArc(0, 0, cornerRadius * 2, cornerRadius * 2, 180, 90);
                            path.AddArc(targetSize - cornerRadius * 2, 0, cornerRadius * 2, cornerRadius * 2, 270, 90);
                            path.AddArc(targetSize - cornerRadius * 2, targetSize - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 0, 90);
                            path.AddArc(0, targetSize - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 90, 90);
                            path.CloseFigure();

                            // Desenhar fundo semitransparente com bordas arredondadas
                            using (var brush = new SolidBrush(Color.FromArgb(opacity, 255, 255, 255)))
                            {
                                g.FillPath(brush, path);
                            }
                        }
                        
                        using var originalBitmap = icon.ToBitmap();
                        
                        float scale = Math.Min((float)maxIconSize / originalBitmap.Width, 
                                            (float)maxIconSize / originalBitmap.Height);
                        
                        int newWidth = (int)(originalBitmap.Width * scale);
                        int newHeight = (int)(originalBitmap.Height * scale);
                        
                        // Criar versão em escala de cinza com preto limitado
                        using var grayscaleBitmap = new Bitmap(newWidth, newHeight);
                        using (var gGray = Graphics.FromImage(grayscaleBitmap))
                        {
                            // Matriz para escala de cinza com limite no preto
                            var colorMatrix = new ColorMatrix(
                                new float[][] 
                                {
                                    new float[] {.3f, .3f, .3f, 0, 0},
                                    new float[] {.59f, .59f, .59f, 0, 0},
                                    new float[] {.11f, .11f, .11f, 0, 0},
                                    new float[] {0, 0, 0, 1, 0},
                                    new float[] {.08f, .08f, .08f, 0, 1}  // Adiciona 15% de branco
                                });

                            using var attributes = new ImageAttributes();
                            attributes.SetColorMatrix(colorMatrix);

                            gGray.DrawImage(originalBitmap,
                                new Rectangle(0, 0, newWidth, newHeight),
                                0, 0, originalBitmap.Width, originalBitmap.Height,
                                GraphicsUnit.Pixel, attributes);
                        }
                        
                        // Centralizar e desenhar o ícone
                        int x = (targetSize - newWidth) / 2;
                        int y = (targetSize - newHeight) / 2;
                        g.DrawImage(grayscaleBitmap, x, y);

                        // Adicionar overlay semitransparente branco (5%)
                        using (var overlayPath = new GraphicsPath())
                        {
                            overlayPath.AddArc(0, 0, cornerRadius * 2, cornerRadius * 2, 180, 90);
                            overlayPath.AddArc(targetSize - cornerRadius * 2, 0, cornerRadius * 2, cornerRadius * 2, 270, 90);
                            overlayPath.AddArc(targetSize - cornerRadius * 2, targetSize - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 0, 90);
                            overlayPath.AddArc(0, targetSize - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 90, 90);
                            overlayPath.CloseFigure();

                            using var overlayBrush = new SolidBrush(Color.FromArgb(13, 255, 255, 255));  // 5% branco
                            g.FillPath(overlayBrush, overlayPath);
                        }
                    }
                    
                    IntPtr hIcon = resultBitmap.GetHicon();
                    return Icon.FromHandle(hIcon);
                }
            }
            catch (OverflowException)
            {
                // Retry específico para OverflowError
                if (attempt < maxAttempts)
                {
                    Thread.Sleep(1);
                    attempt++;
                    continue;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting icon from {filePath}: {ex.Message}");
                break;
            }
            attempt++;
        }
        
        // Fallback: retorna um ��cone padrão ou null
        return null;
    }

    public static string GetProcessExecutablePath(int processId)
    {
        const int PROCESS_QUERY_INFORMATION = 0x0400;
        const int PROCESS_VM_READ = 0x0010;

        IntPtr processHandle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
        if (processHandle != IntPtr.Zero)
        {
            try
            {
                StringBuilder path = new StringBuilder(1024);
                if (GetModuleFileNameEx(processHandle, IntPtr.Zero, path, (uint)path.Capacity) > 0)
                {
                    return path.ToString();
                }
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }
        return string.Empty;
    }

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public RGBQUAD[] bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RGBQUAD
    {
        public byte rgbBlue;
        public byte rgbGreen;
        public byte rgbRed;
        public byte rgbReserved;
    }
} 