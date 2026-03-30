// AndroidRenderSurface - Provides a XAML-based rendering surface for Android graphics.
// Maps Android Canvas/OpenGL ES drawing operations to UWP XAML elements.
// Compatible with Xbox One UWP, Windows RT x64, and ARM platforms.

using System;
using System.Diagnostics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace DalvikUWPCSharp.Reassembly.UI
{
    /// <summary>
    /// A UWP Canvas-based surface that emulates Android's Canvas drawing operations.
    /// This provides the rendering pipeline for Android apps running on UWP,
    /// mapping Android graphics calls to XAML shape primitives.
    /// Designed to be compatible with Xbox One UWP and ARM devices.
    /// </summary>
    public class AndroidRenderSurface : UserControl
    {
        private Canvas renderCanvas;
        private Color currentColor = Colors.Black;
        private double strokeWidth = 1.0;
        private double canvasWidth;
        private double canvasHeight;

        /// <summary>
        /// The currently active render surface. Set when a surface is created for
        /// native rendering so that GL stubs can direct drawing commands here.
        /// </summary>
        public static AndroidRenderSurface Current { get; set; }

        // GL clear color stored by glClearColor, applied by glClear.
        // Stored as an int (ARGB) so it can be written from any thread.
        private volatile int glClearColorArgb = unchecked((int)0xFF000000); // default black

        // Dispatcher captured at construction time (UI thread) for safe cross-thread updates.
        private CoreDispatcher uiDispatcher;

        public AndroidRenderSurface()
        {
            renderCanvas = new Canvas();
            renderCanvas.HorizontalAlignment = HorizontalAlignment.Stretch;
            renderCanvas.VerticalAlignment = VerticalAlignment.Stretch;
            renderCanvas.Background = new SolidColorBrush(Colors.Transparent);
            this.Content = renderCanvas;
            this.SizeChanged += OnSizeChanged;
            uiDispatcher = Window.Current?.Dispatcher;
        }

        public AndroidRenderSurface(double width, double height) : this()
        {
            canvasWidth = width;
            canvasHeight = height;
            renderCanvas.Width = width;
            renderCanvas.Height = height;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            canvasWidth = e.NewSize.Width;
            canvasHeight = e.NewSize.Height;
        }

        /// <summary>
        /// Stores the GL clear color (called from the GLES20 glClearColor stub).
        /// Thread-safe: only writes an int field.
        /// </summary>
        public void SetGLClearColor(float r, float g, float b, float a)
        {
            byte ab = (byte)Math.Min(255, Math.Max(0, (int)(a * 255)));
            byte rb = (byte)Math.Min(255, Math.Max(0, (int)(r * 255)));
            byte gb = (byte)Math.Min(255, Math.Max(0, (int)(g * 255)));
            byte bb = (byte)Math.Min(255, Math.Max(0, (int)(b * 255)));
            glClearColorArgb = (ab << 24) | (rb << 16) | (gb << 8) | bb;
        }

        /// <summary>
        /// Clears the render surface with the previously set GL clear color
        /// (called from the GLES20 glClear stub). Dispatches to the UI thread.
        /// </summary>
        public void GLClear()
        {
            int argb = glClearColorArgb;
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);
            var color = Color.FromArgb(a, r, g, b);

            if (uiDispatcher != null)
            {
                _ = uiDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        renderCanvas.Children.Clear();
                        renderCanvas.Background = new SolidColorBrush(color);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[AndroidRenderSurface] GLClear dispatch error: " + ex.Message);
                    }
                });
            }
            else
            {
                Debug.WriteLine("[AndroidRenderSurface] GLClear skipped – no UI dispatcher available.");
            }
        }

        /// <summary>
        /// Sets the current drawing color (maps to Android Paint.setColor).
        /// </summary>
        public void SetColor(int androidColor)
        {
            byte a = (byte)((androidColor >> 24) & 0xFF);
            byte r = (byte)((androidColor >> 16) & 0xFF);
            byte g = (byte)((androidColor >> 8) & 0xFF);
            byte b = (byte)(androidColor & 0xFF);
            currentColor = Color.FromArgb(a, r, g, b);
        }

        /// <summary>
        /// Sets the current drawing color from a UWP Color.
        /// </summary>
        public void SetColor(Color color)
        {
            currentColor = color;
        }

        /// <summary>
        /// Sets the stroke width for subsequent drawing operations (maps to Paint.setStrokeWidth).
        /// </summary>
        public void SetStrokeWidth(double width)
        {
            strokeWidth = width;
        }

        /// <summary>
        /// Draws a rectangle (maps to Canvas.drawRect).
        /// </summary>
        public void DrawRect(double left, double top, double right, double bottom)
        {
            var rect = new Rectangle
            {
                Width = Math.Max(0, right - left),
                Height = Math.Max(0, bottom - top),
                Fill = new SolidColorBrush(currentColor)
            };
            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);
            renderCanvas.Children.Add(rect);
        }

        /// <summary>
        /// Draws a filled rectangle with a specified color.
        /// </summary>
        public void DrawRect(double left, double top, double right, double bottom, Color color)
        {
            var rect = new Rectangle
            {
                Width = Math.Max(0, right - left),
                Height = Math.Max(0, bottom - top),
                Fill = new SolidColorBrush(color)
            };
            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);
            renderCanvas.Children.Add(rect);
        }

        /// <summary>
        /// Draws a circle (maps to Canvas.drawCircle).
        /// </summary>
        public void DrawCircle(double cx, double cy, double radius)
        {
            var ellipse = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new SolidColorBrush(currentColor)
            };
            Canvas.SetLeft(ellipse, cx - radius);
            Canvas.SetTop(ellipse, cy - radius);
            renderCanvas.Children.Add(ellipse);
        }

        /// <summary>
        /// Draws an oval/ellipse (maps to Canvas.drawOval).
        /// </summary>
        public void DrawOval(double left, double top, double right, double bottom)
        {
            var ellipse = new Ellipse
            {
                Width = Math.Max(0, right - left),
                Height = Math.Max(0, bottom - top),
                Fill = new SolidColorBrush(currentColor)
            };
            Canvas.SetLeft(ellipse, left);
            Canvas.SetTop(ellipse, top);
            renderCanvas.Children.Add(ellipse);
        }

        /// <summary>
        /// Draws a line (maps to Canvas.drawLine).
        /// </summary>
        public void DrawLine(double startX, double startY, double endX, double endY)
        {
            var line = new Line
            {
                X1 = startX,
                Y1 = startY,
                X2 = endX,
                Y2 = endY,
                Stroke = new SolidColorBrush(currentColor),
                StrokeThickness = strokeWidth
            };
            renderCanvas.Children.Add(line);
        }

        /// <summary>
        /// Draws a rounded rectangle (maps to Canvas.drawRoundRect).
        /// </summary>
        public void DrawRoundRect(double left, double top, double right, double bottom,
            double rx, double ry)
        {
            var rect = new Rectangle
            {
                Width = Math.Max(0, right - left),
                Height = Math.Max(0, bottom - top),
                RadiusX = rx,
                RadiusY = ry,
                Fill = new SolidColorBrush(currentColor)
            };
            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);
            renderCanvas.Children.Add(rect);
        }

        /// <summary>
        /// Draws text at a specified position (maps to Canvas.drawText).
        /// </summary>
        public void DrawText(string text, double x, double y, double textSize)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = textSize,
                Foreground = new SolidColorBrush(currentColor)
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            renderCanvas.Children.Add(tb);
        }

        /// <summary>
        /// Draws a point/dot at the specified location.
        /// </summary>
        public void DrawPoint(double x, double y)
        {
            double size = Math.Max(strokeWidth, 2.0);
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(currentColor)
            };
            Canvas.SetLeft(dot, x - size / 2);
            Canvas.SetTop(dot, y - size / 2);
            renderCanvas.Children.Add(dot);
        }

        /// <summary>
        /// Fills the entire canvas with the current color (maps to Canvas.drawColor).
        /// </summary>
        public void DrawColor()
        {
            renderCanvas.Background = new SolidColorBrush(currentColor);
        }

        /// <summary>
        /// Clears all drawing from the canvas.
        /// </summary>
        public void Clear()
        {
            renderCanvas.Children.Clear();
            renderCanvas.Background = new SolidColorBrush(Colors.Transparent);
        }

        /// <summary>
        /// Saves the current canvas state (maps to Canvas.save).
        /// </summary>
        public int Save()
        {
            return renderCanvas.Children.Count;
        }

        /// <summary>
        /// Restores the canvas to a previous save state (maps to Canvas.restore).
        /// </summary>
        public void Restore(int saveCount)
        {
            while (renderCanvas.Children.Count > saveCount && renderCanvas.Children.Count > 0)
            {
                renderCanvas.Children.RemoveAt(renderCanvas.Children.Count - 1);
            }
        }

        /// <summary>
        /// Gets the underlying XAML Canvas element.
        /// </summary>
        public Canvas GetCanvas()
        {
            return renderCanvas;
        }

        /// <summary>
        /// Gets the current canvas width.
        /// </summary>
        public double GetWidth()
        {
            return canvasWidth > 0 ? canvasWidth : renderCanvas.ActualWidth;
        }

        /// <summary>
        /// Gets the current canvas height.
        /// </summary>
        public double GetHeight()
        {
            return canvasHeight > 0 ? canvasHeight : renderCanvas.ActualHeight;
        }
    }
}
