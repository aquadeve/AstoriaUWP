// Reassembly - UI - Renderer

using AndroidInteropLib.android.content;
using AndroidInteropLib.android.support.design.widget;
using AndroidInteropLib.ticomware.interop;
using AndroidXml;
using DalvikUWPCSharp.Applet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;


namespace DalvikUWPCSharp.Reassembly.UI
{
    public class Renderer
    {
        public DroidApp CurrentApp;
        //public Frame CurrentFrame; //For statusbar color emulation

        private string p1nspace = "{http://schemas.android.com/apk/res/android}";

        public Renderer(DroidApp da)
        {
            CurrentApp = da;
        }

        public async Task<UIElement> RenderXmlFile(StorageFile sf)
        {
            byte[] fileBytes = await Disassembly.Util.ReadFile(sf);
            XDocument testdocument = null;

            // First attempt: plain-text XML (for files already decoded by DroidApp.Install())
            try
            {
                using (MemoryStream stream = new MemoryStream(fileBytes))
                    testdocument = XDocument.Load(stream);
            }
            catch
            {
                Debug.WriteLine("[Renderer] Plain-text XML load failed for " + sf.Name + ", trying AndroidXmlReader...");
            }

            // Second attempt: binary Android XML (AXML) – for apps not yet decoded by Install()
            if (testdocument == null)
            {
                try
                {
                    using (MemoryStream stream = new MemoryStream(fileBytes))
                    using (AndroidXmlReader reader = new AndroidXmlReader(stream))
                    {
                        reader.MoveToContent();
                        testdocument = XDocument.Load(reader);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[Renderer] AndroidXmlReader also failed for " + sf.Name + ": " + ex.Message);
                }
            }

            if (testdocument == null)
            {
                Debug.WriteLine("! CRITICAL ERROR: Invalid XML File " + sf.DisplayName + " !");
                return null;
            }

            try
            {
                foreach (XElement xe in testdocument.Elements())
                {
                    //Should only be 1 element
                    return await RenderObject(xe);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("! CRITICAL ERROR: Invalid XML File " + sf.DisplayName + " ! Exception: " + ex.GetType().Name + ": " + ex.Message);
            }

            return null;

        }//RenderXMLFile


        /// <summary>
        /// Applies common Android layout/view attributes to a UWP FrameworkElement.
        /// Handles: layout_width, layout_height, background, visibility, padding,
        /// layout_margin and their directional variants.
        /// </summary>
        private void ApplyCommonAttributes(FrameworkElement element, XElement xe)
        {
            // layout_width
            // Android binary XML encodes match_parent as -1 and wrap_content as -2.
            // After decoding these appear as the literal strings "-1" and "-2".
            if (xe.Attribute(p1nspace + "layout_width") != null)
            {
                string val = xe.Attribute(p1nspace + "layout_width").Value;
                if (val == "match_parent" || val == "fill_parent" || val == "-1")
                    element.HorizontalAlignment = HorizontalAlignment.Stretch;
                else if (val == "wrap_content" || val == "-2")
                    element.Width = double.NaN;
                else if (double.TryParse(val, out var w) && w >= 0)
                    element.Width = w;
            }

            // layout_height
            if (xe.Attribute(p1nspace + "layout_height") != null)
            {
                string val = xe.Attribute(p1nspace + "layout_height").Value;
                if (val == "match_parent" || val == "fill_parent" || val == "-1")
                    element.VerticalAlignment = VerticalAlignment.Stretch;
                else if (val == "wrap_content" || val == "-2")
                    element.Height = double.NaN;
                else if (double.TryParse(val, out var h) && h >= 0)
                    element.Height = h;
            }

            // background
            if (xe.Attribute(p1nspace + "background") != null)
            {
                try
                {
                    var brush = new SolidColorBrush(ColorUtil.FromString(xe.Attribute(p1nspace + "background").Value));
                    if (element is Panel panel) panel.Background = brush;
                    else if (element is Control ctrl) ctrl.Background = brush;
                    else if (element is Border border) border.Background = brush;
                }
                catch { }
            }

            // visibility
            // Plain-text layout files use "visible"/"invisible"/"gone".
            // Binary-decoded XML (via AndroidXmlReader) uses "0"/"1"/"2" instead.
            if (xe.Attribute(p1nspace + "visibility") != null)
            {
                string vis = xe.Attribute(p1nspace + "visibility").Value.ToLower();
                element.Visibility = (vis == "gone" || vis == "invisible" || vis == "1" || vis == "2")
                    ? Visibility.Collapsed : Visibility.Visible;
            }

            // alpha
            if (xe.Attribute(p1nspace + "alpha") != null
                && double.TryParse(xe.Attribute(p1nspace + "alpha").Value, out var alpha))
                element.Opacity = alpha;

            // minWidth / minHeight
            if (xe.Attribute(p1nspace + "minWidth") != null
                && double.TryParse(xe.Attribute(p1nspace + "minWidth").Value, out var minW) && minW >= 0)
                element.MinWidth = minW;
            if (xe.Attribute(p1nspace + "minHeight") != null
                && double.TryParse(xe.Attribute(p1nspace + "minHeight").Value, out var minH) && minH >= 0)
                element.MinHeight = minH;

            // padding (only applies to Control)
            if (element is Control ctrlPad)
            {
                Thickness padding = ctrlPad.Padding;
                bool hasPadding = false;
                if (xe.Attribute(p1nspace + "padding") != null
                    && double.TryParse(xe.Attribute(p1nspace + "padding").Value, out var p))
                { padding = new Thickness(p); hasPadding = true; }
                if (xe.Attribute(p1nspace + "paddingLeft") != null
                    && double.TryParse(xe.Attribute(p1nspace + "paddingLeft").Value, out var pl))
                { padding.Left = pl; hasPadding = true; }
                if (xe.Attribute(p1nspace + "paddingRight") != null
                    && double.TryParse(xe.Attribute(p1nspace + "paddingRight").Value, out var pr))
                { padding.Right = pr; hasPadding = true; }
                if (xe.Attribute(p1nspace + "paddingTop") != null
                    && double.TryParse(xe.Attribute(p1nspace + "paddingTop").Value, out var pt))
                { padding.Top = pt; hasPadding = true; }
                if (xe.Attribute(p1nspace + "paddingBottom") != null
                    && double.TryParse(xe.Attribute(p1nspace + "paddingBottom").Value, out var pb))
                { padding.Bottom = pb; hasPadding = true; }
                if (hasPadding) ctrlPad.Padding = padding;
            }

            // layout_margin
            {
                Thickness margin = element.Margin;
                bool hasMargin = false;
                if (xe.Attribute(p1nspace + "layout_margin") != null
                    && double.TryParse(xe.Attribute(p1nspace + "layout_margin").Value, out var m))
                { margin = new Thickness(m); hasMargin = true; }
                if (xe.Attribute(p1nspace + "layout_marginLeft") != null
                    && double.TryParse(xe.Attribute(p1nspace + "layout_marginLeft").Value, out var ml))
                { margin.Left = ml; hasMargin = true; }
                if (xe.Attribute(p1nspace + "layout_marginRight") != null
                    && double.TryParse(xe.Attribute(p1nspace + "layout_marginRight").Value, out var mr))
                { margin.Right = mr; hasMargin = true; }
                if (xe.Attribute(p1nspace + "layout_marginTop") != null
                    && double.TryParse(xe.Attribute(p1nspace + "layout_marginTop").Value, out var mt))
                { margin.Top = mt; hasMargin = true; }
                if (xe.Attribute(p1nspace + "layout_marginBottom") != null
                    && double.TryParse(xe.Attribute(p1nspace + "layout_marginBottom").Value, out var mb))
                { margin.Bottom = mb; hasMargin = true; }
                if (hasMargin) element.Margin = margin;
            }

            // layout_gravity / gravity → HorizontalAlignment / VerticalAlignment
            string gravityAttr = xe.Attribute(p1nspace + "layout_gravity")?.Value
                              ?? xe.Attribute(p1nspace + "gravity")?.Value;
            if (!string.IsNullOrEmpty(gravityAttr))
            {
                string g = gravityAttr.ToLower();
                if (g.Contains("center_horizontal") || g.Contains("center"))
                    element.HorizontalAlignment = HorizontalAlignment.Center;
                else if (g.Contains("right") || g.Contains("end"))
                    element.HorizontalAlignment = HorizontalAlignment.Right;
                else if (g.Contains("left") || g.Contains("start"))
                    element.HorizontalAlignment = HorizontalAlignment.Left;

                if (g.Contains("center_vertical") || g.Contains("center"))
                    element.VerticalAlignment = VerticalAlignment.Center;
                else if (g.Contains("bottom"))
                    element.VerticalAlignment = VerticalAlignment.Bottom;
                else if (g.Contains("top"))
                    element.VerticalAlignment = VerticalAlignment.Top;
            }
        }

        // RenderObject
        public async Task<UIElement> RenderObject(XElement xe)
        {
            string xeName = xe.Name.ToString();

            bool nestedObjs = xe.HasElements;

            // ── AppBarLayout ──────────────────────────────────────────────────────
            if (xeName == "android.support.design.widget.AppBarLayout"
             || xeName == "com.google.android.material.appbar.AppBarLayout")
            {
                Grid container = new Grid();
                container.VerticalAlignment = VerticalAlignment.Top;
                container.HorizontalAlignment = HorizontalAlignment.Stretch;
                ApplyCommonAttributes(container, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) container.Children.Add(child);
                    }
                return container;
            }

            // ── CoordinatorLayout ────────────────────────────────────────────────
            else if (xeName == "android.support.design.widget.CoordinatorLayout"
                  || xeName == "androidx.coordinatorlayout.widget.CoordinatorLayout")
            {
                CoordinatorLayout cl = new CoordinatorLayout();
                ApplyCommonAttributes(cl, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                        cl.Add(await RenderObject(xe1));
                return cl;
            }

            // ── FloatingActionButton ──────────────────────────────────────────────
            else if (xeName == "android.support.design.widget.FloatingActionButton"
                  || xeName == "com.google.android.material.floatingactionbutton.FloatingActionButton")
            {
                Button fab = new Button();
                fab.Width = 56; fab.Height = 56;
                fab.HorizontalAlignment = HorizontalAlignment.Right;
                fab.VerticalAlignment = VerticalAlignment.Bottom;
                fab.Margin = new Thickness(16);
                fab.Content = "+";
                fab.Background = new SolidColorBrush(Windows.UI.Colors.DeepSkyBlue);
                fab.Foreground = new SolidColorBrush(Windows.UI.Colors.White);
                fab.CornerRadius = new CornerRadius(28);
                ApplyCommonAttributes(fab, xe);
                return fab;
            }

            // ── Toolbar ───────────────────────────────────────────────────────────
            else if (xeName == "android.support.v7.widget.Toolbar"
                  || xeName == "androidx.appcompat.widget.Toolbar")
            {
                AndroidToolbar at = new AndroidToolbar();
                if (xe.Attribute(p1nspace + "title") != null)
                    at.SetTitle(xe.Attribute(p1nspace + "title").Value);
                else
                    at.SetTitle(CurrentApp.metadata.label);
                if (xe.Attribute(p1nspace + "background") != null)
                    try { at.Background = new SolidColorBrush(ColorUtil.FromString(xe.Attribute(p1nspace + "background").Value)); } catch { }
                ApplyCommonAttributes(at, xe);
                return at;
            }

            // ── include ───────────────────────────────────────────────────────────
            else if (xeName == "include")
            {
                try
                {
                    string relUri = xe.Attribute("layout").Value;
                    string path = CurrentApp.resFolder.Path + relUri.Replace('@', '\\').Replace('/', '\\') + ".xml";
                    StorageFile sf = await StorageFile.GetFileFromPathAsync(path);
                    return await RenderXmlFile(sf);
                }
                catch
                {
                    Debug.WriteLine("[Renderer] Failed to include layout: " + xe);
                    return null;
                }
            }

            // ── ConstraintLayout ──────────────────────────────────────────────────
            else if (xeName == "androidx.constraintlayout.widget.ConstraintLayout"
                  || xeName == "android.support.constraint.ConstraintLayout"
                  || xeName == "ConstraintLayout"
                  || xeName == "android.widget.ConstraintLayout")
            {
                Grid container = new Grid();
                ApplyCommonAttributes(container, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) container.Children.Add(child);
                    }
                return container;
            }

            // ── LinearLayout ──────────────────────────────────────────────────────
            else if (xeName == "LinearLayout" || xeName == "android.widget.LinearLayout")
            {
                StackPanel panel = new StackPanel();
                string orientation = xe.Attribute(p1nspace + "orientation")?.Value?.ToLower() ?? "vertical";
                // Binary XML encodes orientation as 0=horizontal, 1=vertical
                panel.Orientation = (orientation == "horizontal" || orientation == "0")
                    ? Orientation.Horizontal : Orientation.Vertical;
                ApplyCommonAttributes(panel, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) panel.Children.Add(child);
                    }
                return panel;
            }

            // ── RelativeLayout ────────────────────────────────────────────────────
            else if (xeName == "RelativeLayout" || xeName == "android.widget.RelativeLayout")
            {
                Grid container = new Grid();
                ApplyCommonAttributes(container, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) container.Children.Add(child);
                    }
                return container;
            }

            // ── FrameLayout (also catches android.widget.FrameLayout, etc.) ───────
            else if (xeName == "FrameLayout"
                  || xeName == "android.widget.FrameLayout"
                  || xeName == "androidx.legacy.app.AppCompatFrameLayout"
                  || xeName.StartsWith("android.widget.FrameLayout"))
            {
                Grid container = new Grid();
                ApplyCommonAttributes(container, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) container.Children.Add(child);
                    }
                return container;
            }

            // ── GridLayout ────────────────────────────────────────────────────────
            else if (xeName == "GridLayout"
                  || xeName == "android.widget.GridLayout"
                  || xeName == "androidx.gridlayout.widget.GridLayout")
            {
                Grid grid = new Grid();
                ApplyCommonAttributes(grid, xe);
                if (xe.Attribute(p1nspace + "rowCount") != null
                    && int.TryParse(xe.Attribute(p1nspace + "rowCount").Value, out var rc))
                    for (int i = 0; i < rc; i++)
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (xe.Attribute(p1nspace + "columnCount") != null
                    && int.TryParse(xe.Attribute(p1nspace + "columnCount").Value, out var cc))
                    for (int i = 0; i < cc; i++)
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) grid.Children.Add(child);
                    }
                return grid;
            }

            // ── ScrollView ────────────────────────────────────────────────────────
            else if (xeName == "ScrollView" || xeName == "android.widget.ScrollView")
            {
                ScrollViewer sv = new ScrollViewer();
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                StackPanel content = new StackPanel();
                sv.Content = content;
                ApplyCommonAttributes(sv, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) content.Children.Add(child);
                    }
                return sv;
            }

            // ── HorizontalScrollView ──────────────────────────────────────────────
            else if (xeName == "HorizontalScrollView" || xeName == "android.widget.HorizontalScrollView")
            {
                ScrollViewer sv = new ScrollViewer();
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                StackPanel content = new StackPanel { Orientation = Orientation.Horizontal };
                sv.Content = content;
                ApplyCommonAttributes(sv, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) content.Children.Add(child);
                    }
                return sv;
            }

            // ── TextView ──────────────────────────────────────────────────────────
            else if (xeName == "TextView" || xeName == "android.widget.TextView")
            {
                TextBlock tv = new TextBlock();
                tv.Margin = new Thickness(14.8, 7.4, 14.8, 7.4);
                if (xe.Attribute(p1nspace + "text") != null)
                    tv.Text = xe.Attribute(p1nspace + "text").Value;
                if (xe.Attribute(p1nspace + "hint") != null && string.IsNullOrEmpty(tv.Text))
                    tv.Text = xe.Attribute(p1nspace + "hint").Value;
                if (xe.Attribute(p1nspace + "textColor") != null)
                    try { tv.Foreground = new SolidColorBrush(ColorUtil.FromString(xe.Attribute(p1nspace + "textColor").Value)); } catch { }
                if (xe.Attribute(p1nspace + "textSize") != null
                    && double.TryParse(xe.Attribute(p1nspace + "textSize").Value, out var sz))
                    tv.FontSize = sz;
                if (xe.Attribute(p1nspace + "textStyle") != null)
                {
                    string style = xe.Attribute(p1nspace + "textStyle").Value.ToLower();
                    if (style.Contains("bold")) tv.FontWeight = Windows.UI.Text.FontWeights.Bold;
                    if (style.Contains("italic")) tv.FontStyle = Windows.UI.Text.FontStyle.Italic;
                }
                if (xe.Attribute(p1nspace + "gravity") != null)
                {
                    string gravity = xe.Attribute(p1nspace + "gravity").Value.ToLower();
                    if (gravity.Contains("center")) tv.TextAlignment = TextAlignment.Center;
                    else if (gravity.Contains("right") || gravity.Contains("end")) tv.TextAlignment = TextAlignment.Right;
                    else tv.TextAlignment = TextAlignment.Left;
                }
                if (xe.Attribute(p1nspace + "maxLines") != null
                    && int.TryParse(xe.Attribute(p1nspace + "maxLines").Value, out var maxl))
                    tv.MaxLines = maxl;
                ApplyCommonAttributes(tv, xe);
                return tv;
            }

            // ── EditText ──────────────────────────────────────────────────────────
            else if (xeName == "EditText"
                  || xeName == "android.widget.EditText"
                  || xeName == "android.support.design.widget.TextInputEditText"
                  || xeName == "com.google.android.material.textfield.TextInputEditText")
            {
                string inputType = xe.Attribute(p1nspace + "inputType")?.Value?.ToLower() ?? "";
                if (inputType.Contains("textpassword") || inputType.Contains("numberpassword"))
                {
                    PasswordBox pb = new PasswordBox();
                    if (xe.Attribute(p1nspace + "hint") != null)
                        pb.PlaceholderText = xe.Attribute(p1nspace + "hint").Value;
                    ApplyCommonAttributes(pb, xe);
                    return pb;
                }
                TextBox tb = new TextBox();
                if (xe.Attribute(p1nspace + "text") != null)
                    tb.Text = xe.Attribute(p1nspace + "text").Value;
                if (xe.Attribute(p1nspace + "hint") != null)
                    tb.PlaceholderText = xe.Attribute(p1nspace + "hint").Value;
                if (inputType.Contains("textmultiline") || inputType.Contains("textnosuggestions"))
                    tb.AcceptsReturn = true;
                if (xe.Attribute(p1nspace + "textSize") != null
                    && double.TryParse(xe.Attribute(p1nspace + "textSize").Value, out var etsz))
                    tb.FontSize = etsz;
                if (xe.Attribute(p1nspace + "textColor") != null)
                    try { tb.Foreground = new SolidColorBrush(ColorUtil.FromString(xe.Attribute(p1nspace + "textColor").Value)); } catch { }
                if (xe.Attribute(p1nspace + "maxLines") != null
                    && int.TryParse(xe.Attribute(p1nspace + "maxLines").Value, out var etmaxl))
                    tb.MaxLength = etmaxl * 200; // ~200 chars per line as rough approximation
                ApplyCommonAttributes(tb, xe);
                return tb;
            }

            // ── Button / MaterialButton ───────────────────────────────────────────
            else if (xeName == "Button"
                  || xeName == "android.widget.Button"
                  || xeName == "com.google.android.material.button.MaterialButton")
            {
                Button btn = new Button();
                if (xe.Attribute(p1nspace + "text") != null)
                    btn.Content = xe.Attribute(p1nspace + "text").Value;
                if (xe.Attribute(p1nspace + "textColor") != null)
                    try { btn.Foreground = new SolidColorBrush(ColorUtil.FromString(xe.Attribute(p1nspace + "textColor").Value)); } catch { }
                if (xe.Attribute(p1nspace + "background") != null)
                    try { btn.Background = new SolidColorBrush(ColorUtil.FromString(xe.Attribute(p1nspace + "background").Value)); } catch { }
                if (xe.Attribute(p1nspace + "enabled") != null)
                    btn.IsEnabled = xe.Attribute(p1nspace + "enabled").Value != "false";
                ApplyCommonAttributes(btn, xe);
                return btn;
            }

            // ── ImageButton ───────────────────────────────────────────────────────
            else if (xeName == "ImageButton" || xeName == "android.widget.ImageButton")
            {
                Button btn = new Button();
                Windows.UI.Xaml.Controls.Image img = new Windows.UI.Xaml.Controls.Image();
                img.Width = 24; img.Height = 24;
                img.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri("ms-appx:///Assets/Square150x150Logo.png"));
                btn.Content = img;
                btn.Padding = new Thickness(8);
                ApplyCommonAttributes(btn, xe);
                return btn;
            }

            // ── ImageView ─────────────────────────────────────────────────────────
            else if (xeName == "ImageView" || xeName == "android.widget.ImageView")
            {
                Windows.UI.Xaml.Controls.Image img = new Windows.UI.Xaml.Controls.Image();
                img.Stretch = Windows.UI.Xaml.Media.Stretch.Uniform;
                img.Margin = new Thickness(4);
                if (xe.Attribute(p1nspace + "src") != null)
                {
                    try
                    {
                        img.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                            new Uri("ms-appx:///Assets/Square150x150Logo.png"));
                    }
                    catch { }
                }
                if (xe.Attribute(p1nspace + "scaleType") != null)
                {
                    switch (xe.Attribute(p1nspace + "scaleType").Value.ToLower())
                    {
                        case "fitxy": img.Stretch = Windows.UI.Xaml.Media.Stretch.Fill; break;
                        case "centercrop":
                        case "fitstart":
                        case "fitend": img.Stretch = Windows.UI.Xaml.Media.Stretch.UniformToFill; break;
                        case "centerinside":
                        case "matrix": img.Stretch = Windows.UI.Xaml.Media.Stretch.None; break;
                        default: img.Stretch = Windows.UI.Xaml.Media.Stretch.Uniform; break;
                    }
                }
                ApplyCommonAttributes(img, xe);
                return img;
            }

            // ── CheckBox ──────────────────────────────────────────────────────────
            else if (xeName == "CheckBox" || xeName == "android.widget.CheckBox")
            {
                CheckBox cb = new CheckBox();
                if (xe.Attribute(p1nspace + "text") != null)
                    cb.Content = xe.Attribute(p1nspace + "text").Value;
                if (xe.Attribute(p1nspace + "checked") != null)
                    cb.IsChecked = xe.Attribute(p1nspace + "checked").Value == "true";
                if (xe.Attribute(p1nspace + "enabled") != null)
                    cb.IsEnabled = xe.Attribute(p1nspace + "enabled").Value != "false";
                ApplyCommonAttributes(cb, xe);
                return cb;
            }

            // ── RadioButton ───────────────────────────────────────────────────────
            else if (xeName == "RadioButton" || xeName == "android.widget.RadioButton")
            {
                RadioButton rb = new RadioButton();
                if (xe.Attribute(p1nspace + "text") != null)
                    rb.Content = xe.Attribute(p1nspace + "text").Value;
                if (xe.Attribute(p1nspace + "checked") != null)
                    rb.IsChecked = xe.Attribute(p1nspace + "checked").Value == "true";
                if (xe.Attribute(p1nspace + "enabled") != null)
                    rb.IsEnabled = xe.Attribute(p1nspace + "enabled").Value != "false";
                ApplyCommonAttributes(rb, xe);
                return rb;
            }

            // ── RadioGroup ────────────────────────────────────────────────────────
            else if (xeName == "RadioGroup" || xeName == "android.widget.RadioGroup")
            {
                StackPanel panel = new StackPanel();
                string orientation = xe.Attribute(p1nspace + "orientation")?.Value?.ToLower() ?? "vertical";
                // Binary XML encodes orientation as 0=horizontal, 1=vertical
                panel.Orientation = (orientation == "horizontal" || orientation == "0")
                    ? Orientation.Horizontal : Orientation.Vertical;
                ApplyCommonAttributes(panel, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) panel.Children.Add(child);
                    }
                return panel;
            }

            // ── Switch / SwitchCompat ─────────────────────────────────────────────
            else if (xeName == "Switch"
                  || xeName == "android.widget.Switch"
                  || xeName == "androidx.appcompat.widget.SwitchCompat")
            {
                ToggleSwitch ts = new ToggleSwitch();
                if (xe.Attribute(p1nspace + "text") != null)
                    ts.Header = xe.Attribute(p1nspace + "text").Value;
                if (xe.Attribute(p1nspace + "textOn") != null)
                    ts.OnContent = xe.Attribute(p1nspace + "textOn").Value;
                if (xe.Attribute(p1nspace + "textOff") != null)
                    ts.OffContent = xe.Attribute(p1nspace + "textOff").Value;
                if (xe.Attribute(p1nspace + "checked") != null)
                    ts.IsOn = xe.Attribute(p1nspace + "checked").Value == "true";
                if (xe.Attribute(p1nspace + "enabled") != null)
                    ts.IsEnabled = xe.Attribute(p1nspace + "enabled").Value != "false";
                ApplyCommonAttributes(ts, xe);
                return ts;
            }

            // ── ToggleButton ──────────────────────────────────────────────────────
            else if (xeName == "ToggleButton" || xeName == "android.widget.ToggleButton")
            {
                ToggleButton tb = new ToggleButton();
                if (xe.Attribute(p1nspace + "text") != null)
                    tb.Content = xe.Attribute(p1nspace + "text").Value;
                if (xe.Attribute(p1nspace + "textOn") != null)
                    tb.Content = xe.Attribute(p1nspace + "textOn").Value;
                if (xe.Attribute(p1nspace + "checked") != null)
                    tb.IsChecked = xe.Attribute(p1nspace + "checked").Value == "true";
                ApplyCommonAttributes(tb, xe);
                return tb;
            }

            // ── ProgressBar ───────────────────────────────────────────────────────
            else if (xeName == "ProgressBar" || xeName == "android.widget.ProgressBar")
            {
                string style = xe.Attribute("style")?.Value ?? "";
                bool isHorizontal = style.ToLower().Contains("horizontal")
                    || xe.Attribute(p1nspace + "max") != null;
                if (isHorizontal)
                {
                    Windows.UI.Xaml.Controls.ProgressBar pb = new Windows.UI.Xaml.Controls.ProgressBar();
                    if (xe.Attribute(p1nspace + "max") != null
                        && double.TryParse(xe.Attribute(p1nspace + "max").Value, out var pbmax))
                        pb.Maximum = pbmax;
                    if (xe.Attribute(p1nspace + "progress") != null
                        && double.TryParse(xe.Attribute(p1nspace + "progress").Value, out var pbprog))
                        pb.Value = pbprog;
                    if (xe.Attribute(p1nspace + "indeterminate") != null)
                        pb.IsIndeterminate = xe.Attribute(p1nspace + "indeterminate").Value == "true";
                    ApplyCommonAttributes(pb, xe);
                    return pb;
                }
                else
                {
                    ProgressRing pr = new ProgressRing();
                    pr.IsActive = true;
                    ApplyCommonAttributes(pr, xe);
                    return pr;
                }
            }

            // ── SeekBar ───────────────────────────────────────────────────────────
            else if (xeName == "SeekBar"
                  || xeName == "android.widget.SeekBar"
                  || xeName == "android.widget.AbsSeekBar")
            {
                Slider slider = new Slider();
                if (xe.Attribute(p1nspace + "max") != null
                    && double.TryParse(xe.Attribute(p1nspace + "max").Value, out var sbmax))
                    slider.Maximum = sbmax;
                if (xe.Attribute(p1nspace + "min") != null
                    && double.TryParse(xe.Attribute(p1nspace + "min").Value, out var sbmin))
                    slider.Minimum = sbmin;
                if (xe.Attribute(p1nspace + "progress") != null
                    && double.TryParse(xe.Attribute(p1nspace + "progress").Value, out var sbprog))
                    slider.Value = sbprog;
                ApplyCommonAttributes(slider, xe);
                return slider;
            }

            // ── RatingBar ─────────────────────────────────────────────────────────
            else if (xeName == "RatingBar" || xeName == "android.widget.RatingBar")
            {
                StackPanel ratingPanel = new StackPanel { Orientation = Orientation.Horizontal };
                int numStars = 5;
                double rating = 0;
                if (xe.Attribute(p1nspace + "numStars") != null
                    && int.TryParse(xe.Attribute(p1nspace + "numStars").Value, out var ns))
                    numStars = ns;
                if (xe.Attribute(p1nspace + "rating") != null
                    && double.TryParse(xe.Attribute(p1nspace + "rating").Value, out var rv))
                    rating = rv;
                for (int i = 1; i <= numStars; i++)
                    ratingPanel.Children.Add(new TextBlock
                    {
                        Text = i <= rating ? "★" : "☆",
                        FontSize = 24,
                        Margin = new Thickness(2)
                    });
                ApplyCommonAttributes(ratingPanel, xe);
                return ratingPanel;
            }

            // ── Spinner ───────────────────────────────────────────────────────────
            else if (xeName == "Spinner" || xeName == "android.widget.Spinner")
            {
                ComboBox cb = new ComboBox();
                cb.HorizontalAlignment = HorizontalAlignment.Stretch;
                if (xe.Attribute(p1nspace + "prompt") != null)
                    cb.PlaceholderText = xe.Attribute(p1nspace + "prompt").Value;
                ApplyCommonAttributes(cb, xe);
                return cb;
            }

            // ── ListView ──────────────────────────────────────────────────────────
            else if (xeName == "ListView" || xeName == "android.widget.ListView")
            {
                Windows.UI.Xaml.Controls.ListView lv = new Windows.UI.Xaml.Controls.ListView();
                lv.HorizontalAlignment = HorizontalAlignment.Stretch;
                lv.VerticalAlignment = VerticalAlignment.Stretch;
                ApplyCommonAttributes(lv, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) lv.Items.Add(child);
                    }
                return lv;
            }

            // ── GridView ──────────────────────────────────────────────────────────
            else if (xeName == "GridView" || xeName == "android.widget.GridView")
            {
                Windows.UI.Xaml.Controls.GridView gv = new Windows.UI.Xaml.Controls.GridView();
                gv.HorizontalAlignment = HorizontalAlignment.Stretch;
                gv.VerticalAlignment = VerticalAlignment.Stretch;
                ApplyCommonAttributes(gv, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) gv.Items.Add(child);
                    }
                return gv;
            }

            // ── RecyclerView ──────────────────────────────────────────────────────
            else if (xeName == "android.support.v7.widget.RecyclerView"
                  || xeName == "androidx.recyclerview.widget.RecyclerView"
                  || xeName == "RecyclerView")
            {
                Windows.UI.Xaml.Controls.ListView lv = new Windows.UI.Xaml.Controls.ListView();
                lv.HorizontalAlignment = HorizontalAlignment.Stretch;
                lv.VerticalAlignment = VerticalAlignment.Stretch;
                ApplyCommonAttributes(lv, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) lv.Items.Add(child);
                    }
                return lv;
            }

            // ── CardView ──────────────────────────────────────────────────────────
            else if (xeName == "android.support.v7.widget.CardView"
                  || xeName == "androidx.cardview.widget.CardView"
                  || xeName == "CardView")
            {
                Border card = new Border();
                card.CornerRadius = new CornerRadius(4);
                card.Margin = new Thickness(8);
                card.Padding = new Thickness(8);
                card.Background = new SolidColorBrush(Windows.UI.Colors.White);
                if (xe.Attribute(p1nspace + "background") != null)
                    try { card.Background = new SolidColorBrush(ColorUtil.FromString(xe.Attribute(p1nspace + "background").Value)); } catch { }
                if (xe.Attribute("{http://schemas.android.com/apk/res-auto}cardCornerRadius") != null
                    && double.TryParse(xe.Attribute("{http://schemas.android.com/apk/res-auto}cardCornerRadius").Value, out var cr))
                    card.CornerRadius = new CornerRadius(cr);
                Grid cardContent = new Grid();
                card.Child = cardContent;
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) cardContent.Children.Add(child);
                    }
                ApplyCommonAttributes(card, xe);
                return card;
            }

            // ── TextInputLayout ───────────────────────────────────────────────────
            else if (xeName == "android.support.design.widget.TextInputLayout"
                  || xeName == "com.google.android.material.textfield.TextInputLayout")
            {
                StackPanel panel = new StackPanel();
                if (xe.Attribute(p1nspace + "hint") != null)
                    panel.Children.Add(new TextBlock
                    {
                        Text = xe.Attribute(p1nspace + "hint").Value,
                        FontSize = 12,
                        Opacity = 0.7
                    });
                ApplyCommonAttributes(panel, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) panel.Children.Add(child);
                    }
                return panel;
            }

            // ── TabLayout ─────────────────────────────────────────────────────────
            else if (xeName == "android.support.design.widget.TabLayout"
                  || xeName == "com.google.android.material.tabs.TabLayout")
            {
                Pivot pivot = new Pivot();
                ApplyCommonAttributes(pivot, xe);
                return pivot;
            }

            // ── BottomNavigationView ──────────────────────────────────────────────
            else if (xeName == "android.support.design.widget.BottomNavigationView"
                  || xeName == "com.google.android.material.bottomnavigation.BottomNavigationView")
            {
                CommandBar cb = new CommandBar();
                cb.VerticalAlignment = VerticalAlignment.Bottom;
                cb.HorizontalAlignment = HorizontalAlignment.Stretch;
                ApplyCommonAttributes(cb, xe);
                return cb;
            }

            // ── NavigationView ────────────────────────────────────────────────────
            else if (xeName == "android.support.design.widget.NavigationView"
                  || xeName == "com.google.android.material.navigation.NavigationView")
            {
                Windows.UI.Xaml.Controls.NavigationView nav = new Windows.UI.Xaml.Controls.NavigationView();
                ApplyCommonAttributes(nav, xe);
                return nav;
            }

            // ── Chip ──────────────────────────────────────────────────────────────
            else if (xeName == "android.support.design.widget.Chip"
                  || xeName == "com.google.android.material.chip.Chip")
            {
                Button chip = new Button();
                chip.CornerRadius = new CornerRadius(16);
                chip.Margin = new Thickness(4, 2, 4, 2);
                chip.Padding = new Thickness(12, 4, 12, 4);
                chip.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 224, 224, 224));
                if (xe.Attribute(p1nspace + "text") != null)
                    chip.Content = xe.Attribute(p1nspace + "text").Value;
                ApplyCommonAttributes(chip, xe);
                return chip;
            }

            // ── ChipGroup ─────────────────────────────────────────────────────────
            else if (xeName == "android.support.design.widget.ChipGroup"
                  || xeName == "com.google.android.material.chip.ChipGroup")
            {
                StackPanel chipGroup = new StackPanel { Orientation = Orientation.Horizontal };
                chipGroup.Margin = new Thickness(4);
                ApplyCommonAttributes(chipGroup, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) chipGroup.Children.Add(child);
                    }
                return chipGroup;
            }

            // ── ViewPager ─────────────────────────────────────────────────────────
            else if (xeName == "androidx.viewpager.widget.ViewPager"
                  || xeName == "androidx.viewpager2.widget.ViewPager2"
                  || xeName == "android.support.v4.view.ViewPager"
                  || xeName == "ViewPager")
            {
                FlipView fv = new FlipView();
                ApplyCommonAttributes(fv, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) fv.Items.Add(child);
                    }
                return fv;
            }

            // ── DrawerLayout ──────────────────────────────────────────────────────
            else if (xeName == "androidx.drawerlayout.widget.DrawerLayout"
                  || xeName == "android.support.v4.widget.DrawerLayout"
                  || xeName == "DrawerLayout")
            {
                SplitView sv = new SplitView();
                sv.DisplayMode = SplitViewDisplayMode.Overlay;
                ApplyCommonAttributes(sv, xe);
                if (nestedObjs)
                {
                    bool firstChild = true;
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null)
                        {
                            if (firstChild) { sv.Content = child; firstChild = false; }
                            else sv.Pane = child;
                        }
                    }
                }
                return sv;
            }

            // ── ShapeView ─────────────────────────────────────────────────────────
            else if (xeName == "ShapeView"
                  || xeName == "com.example.shapeviewdemo.ShapeView")
            {
                Debug.WriteLine($"[Renderer] Creating ShapeView for element: {xe}");
                ShapeView shapeView = new ShapeView(new AstoriaContext(), new AstoriaAttrSet(xe));
                try
                {
                    string primitive = xe.Attribute("primitive")?.Value?.ToLower() ?? "rectangle";
                    Windows.UI.Color color = Windows.UI.Colors.White;
                    if (xe.Attribute("color") != null)
                        color = ColorUtil.FromString(xe.Attribute("color").Value);
                    switch (primitive)
                    {
                        case "rectangle":
                        {
                            double width = xe.Attribute("width") != null ? double.Parse(xe.Attribute("width").Value) : 100;
                            double height = xe.Attribute("height") != null ? double.Parse(xe.Attribute("height").Value) : 100;
                            shapeView.DrawRectangle(width, height, color);
                            break;
                        }
                        case "ellipse":
                        {
                            double width = xe.Attribute("width") != null ? double.Parse(xe.Attribute("width").Value) : 100;
                            double height = xe.Attribute("height") != null ? double.Parse(xe.Attribute("height").Value) : 100;
                            shapeView.DrawEllipse(width, height, color);
                            break;
                        }
                        case "line":
                        {
                            double x1 = xe.Attribute("x1") != null ? double.Parse(xe.Attribute("x1").Value) : 0;
                            double y1 = xe.Attribute("y1") != null ? double.Parse(xe.Attribute("y1").Value) : 0;
                            double x2 = xe.Attribute("x2") != null ? double.Parse(xe.Attribute("x2").Value) : 100;
                            double y2 = xe.Attribute("y2") != null ? double.Parse(xe.Attribute("y2").Value) : 100;
                            double thickness = xe.Attribute("thickness") != null ? double.Parse(xe.Attribute("thickness").Value) : 2;
                            shapeView.DrawLine(x1, y1, x2, y2, thickness, color);
                            break;
                        }
                        case "point":
                        {
                            double x = xe.Attribute("x") != null ? double.Parse(xe.Attribute("x").Value) : 50;
                            double y = xe.Attribute("y") != null ? double.Parse(xe.Attribute("y").Value) : 50;
                            double diameter = xe.Attribute("diameter") != null ? double.Parse(xe.Attribute("diameter").Value) : 8;
                            shapeView.DrawPoint(x, y, diameter, color);
                            break;
                        }
                        default:
                            Debug.WriteLine($"[Renderer] Unknown primitive type: {primitive}, defaulting to rectangle.");
                            shapeView.DrawRectangle(100, 100, color);
                            break;
                    }
                    if (xe.Attribute("background") != null)
                        shapeView.SetBackgroundColor(ColorUtil.FromString(xe.Attribute("background").Value));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Renderer] Error rendering ShapeView primitive: {ex.Message}");
                }
                return shapeView;
            }

            // ── GLSurfaceView / SurfaceView ───────────────────────────────────────
            // Native OpenGL games (e.g. Angry Birds) use GLSurfaceView for rendering.
            // Map to AndroidRenderSurface so the game has a surface to draw on.
            else if (xeName == "android.opengl.GLSurfaceView"
                  || xeName == "GLSurfaceView"
                  || xeName == "android.view.SurfaceView"
                  || xeName == "SurfaceView"
                  || xeName == "android.opengl.GLTextureView"
                  || xeName == "android.view.TextureView"
                  || xeName == "TextureView")
            {
                Debug.WriteLine("[Renderer] Creating AndroidRenderSurface for " + xeName);
                var surface = new AndroidRenderSurface();
                surface.HorizontalAlignment = HorizontalAlignment.Stretch;
                surface.VerticalAlignment = VerticalAlignment.Stretch;
                ApplyCommonAttributes(surface, xe);
                return surface;
            }

            // ── HorizontalScrollView ──────────────────────────────────────────────
            else if (xeName == "HorizontalScrollView" || xeName == "android.widget.HorizontalScrollView")
            {
                ScrollViewer sv = new ScrollViewer();
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                StackPanel content = new StackPanel { Orientation = Orientation.Horizontal };
                sv.Content = content;
                ApplyCommonAttributes(sv, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) content.Children.Add(child);
                    }
                return sv;
            }

            // ── NestedScrollView ──────────────────────────────────────────────────
            else if (xeName == "androidx.core.widget.NestedScrollView"
                  || xeName == "android.support.v4.widget.NestedScrollView"
                  || xeName == "NestedScrollView")
            {
                ScrollViewer sv = new ScrollViewer();
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                StackPanel content = new StackPanel();
                sv.Content = content;
                ApplyCommonAttributes(sv, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) content.Children.Add(child);
                    }
                return sv;
            }

            // ── TableLayout ───────────────────────────────────────────────────────
            else if (xeName == "TableLayout" || xeName == "android.widget.TableLayout")
            {
                Grid tbl = new Grid();
                ApplyCommonAttributes(tbl, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) tbl.Children.Add(child);
                    }
                return tbl;
            }

            // ── TableRow ──────────────────────────────────────────────────────────
            else if (xeName == "TableRow" || xeName == "android.widget.TableRow")
            {
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
                ApplyCommonAttributes(row, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) row.Children.Add(child);
                    }
                return row;
            }

            // ── WebView ───────────────────────────────────────────────────────────
            else if (xeName == "WebView" || xeName == "android.webkit.WebView")
            {
                WebView wv = new WebView();
                wv.HorizontalAlignment = HorizontalAlignment.Stretch;
                wv.VerticalAlignment = VerticalAlignment.Stretch;
                ApplyCommonAttributes(wv, xe);
                return wv;
            }

            // ── VideoView ─────────────────────────────────────────────────────────
            else if (xeName == "VideoView" || xeName == "android.widget.VideoView")
            {
                MediaElement me = new MediaElement();
                me.HorizontalAlignment = HorizontalAlignment.Stretch;
                me.VerticalAlignment = VerticalAlignment.Stretch;
                ApplyCommonAttributes(me, xe);
                return me;
            }

            // ── Space ─────────────────────────────────────────────────────────────
            else if (xeName == "Space" || xeName == "android.widget.Space")
            {
                var spacer = new Border();
                ApplyCommonAttributes(spacer, xe);
                return spacer;
            }

            // ── ViewStub ──────────────────────────────────────────────────────────
            else if (xeName == "ViewStub" || xeName == "android.view.ViewStub")
            {
                // ViewStub is lazy-inflated at runtime; emit an invisible placeholder
                var stub = new Border { Visibility = Visibility.Collapsed };
                return stub;
            }

            // ── ViewAnimator / ViewSwitcher / ViewFlipper ─────────────────────────
            else if (xeName == "ViewAnimator" || xeName == "ViewSwitcher"
                  || xeName == "ViewFlipper"  || xeName == "android.widget.ViewAnimator"
                  || xeName == "android.widget.ViewSwitcher"
                  || xeName == "android.widget.ViewFlipper")
            {
                Grid container = new Grid();
                ApplyCommonAttributes(container, xe);
                if (nestedObjs)
                    foreach (XElement xe1 in xe.Elements())
                    {
                        var child = await RenderObject(xe1);
                        if (child != null) container.Children.Add(child);
                    }
                return container;
            }

            // ── ImageButton ───────────────────────────────────────────────────────
            else if (xeName == "ImageButton" || xeName == "android.widget.ImageButton")
            {
                Button btn = new Button();
                btn.Padding = new Thickness(4);
                if (xe.Attribute(p1nspace + "src") != null)
                {
                    // Use a TextBlock fallback since we may not have the drawable
                    btn.Content = xe.Attribute(p1nspace + "src").Value;
                }
                ApplyCommonAttributes(btn, xe);
                return btn;
            }

            // ── RatingBar ─────────────────────────────────────────────────────────
            else if (xeName == "RatingBar" || xeName == "android.widget.RatingBar")
            {
                Slider ratingSlider = new Slider();
                ratingSlider.Minimum = 0;
                ratingSlider.Maximum = 5;
                ratingSlider.StepFrequency = 1;
                if (xe.Attribute(p1nspace + "numStars") != null
                    && double.TryParse(xe.Attribute(p1nspace + "numStars").Value, out var stars))
                    ratingSlider.Maximum = stars;
                ApplyCommonAttributes(ratingSlider, xe);
                return ratingSlider;
            }

            // ── TimePicker / DatePicker ────────────────────────────────────────────
            else if (xeName == "TimePicker" || xeName == "android.widget.TimePicker")
            {
                TimePicker tp = new TimePicker();
                ApplyCommonAttributes(tp, xe);
                return tp;
            }
            else if (xeName == "DatePicker" || xeName == "android.widget.DatePicker")
            {
                CalendarDatePicker cdp = new CalendarDatePicker();
                ApplyCommonAttributes(cdp, xe);
                return cdp;
            }

            // ── NumberPicker ──────────────────────────────────────────────────────
            else if (xeName == "NumberPicker" || xeName == "android.widget.NumberPicker")
            {
                Slider slider = new Slider();
                ApplyCommonAttributes(slider, xe);
                return slider;
            }

            // ── CalendarView ──────────────────────────────────────────────────────
            else if (xeName == "CalendarView" || xeName == "android.widget.CalendarView")
            {
                CalendarView cal = new CalendarView();
                ApplyCommonAttributes(cal, xe);
                return cal;
            }

            // ── AutoCompleteTextView / MultiAutoCompleteTextView ──────────────────
            else if (xeName == "AutoCompleteTextView"
                  || xeName == "MultiAutoCompleteTextView"
                  || xeName == "android.widget.AutoCompleteTextView"
                  || xeName == "android.widget.MultiAutoCompleteTextView")
            {
                AutoSuggestBox asb = new AutoSuggestBox();
                if (xe.Attribute(p1nspace + "hint") != null)
                    asb.PlaceholderText = xe.Attribute(p1nspace + "hint").Value;
                ApplyCommonAttributes(asb, xe);
                return asb;
            }

            // ── Unrecognised element placeholder ─────────────────────────────────
            else
            {
                Debug.WriteLine($"[Renderer] UIElement {xe.Name} is not currently implemented on this renderer.");
                return new Border
                {
                    Background = new SolidColorBrush(Colors.Red),
                    Child = new TextBlock
                    {
                        Text = $"Not implemented: {xe.Name}",
                        Foreground = new SolidColorBrush(Colors.White)
                    },
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(4)
                };
            }

        }//RenderObject end



        // DPtoEP(int i)
        public static double DPtoEP(int i)
        {
            //TODO: scale android dp sizes to windows dp sizes

            //Android dp size = dp = (width in pixels * 160) / screen density in dpi; 1dp = 1px on a 160dpi screen; px = dp * (dpi / 160)
            //Android DP is equivelent to the current screen size at 160dpi
            //Windows Effictive Pixels (ep) = 146.86 dpi (phone) ~150 (Tablet) ~110 (Desktop)
            //Let's make it 148 dpi for simplicity sake.

            //ep = (dp/160) * 148
            return (i / 160) * 148;

        }//DPtoEP end
    
    }//Renderer class end

}// namespace end

