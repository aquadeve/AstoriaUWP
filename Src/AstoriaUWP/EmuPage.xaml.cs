// EmuPage

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Foundation.Metadata;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using AndroidInteropLib;
using AndroidInteropLib.android.view;
using AndroidXml;

using DalvikUWPCSharp.Applet;
using DalvikUWPCSharp.Classes;
using DalvikUWPCSharp.Reassembly;
using DalvikUWPCSharp.Reassembly.UI;


// DalvikUWPCSharp
namespace DalvikUWPCSharp
{
    // EmuPage class
    public sealed partial class EmuPage : Page
    {
        public DroidApp RunningApp; //

        private Renderer UIRenderer; // 

        private DalvikCPU cpu; //


        // EmuPage
        public EmuPage()
        {
            this.InitializeComponent();
            this.Loaded += EmuPage_Loaded;

            //RnD
            Windows.UI.Xaml.Window.Current.SizeChanged += Current_SizeChanged;

            // Xbox One: apply TV safe area margins and handle gamepad input
            XboxPlatform.ApplyTvSafeArea(this);
            this.KeyDown += EmuPage_KeyDown;
            
        }//EmuPage end


        // Current_SizeChanged
        private void Current_SizeChanged(object sender, Windows.UI.Core.WindowSizeChangedEventArgs e)
        {
            //UserControl appView = (UserControl)RenderTargetBox.Child;
            //appView.Width = (this.ActualWidth) * (40/37);
            //appView.Height = (this.ActualHeight - 48) * (40 / 37);

        }//Current_SizeChanged end 


        // OnNavigatedTo
        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            try
            {
                //base.OnNavigatedTo(e);
                if (e.Parameter.GetType().Equals(typeof(DroidApp)))
                {
                    RunningApp = (DroidApp)e.Parameter;
                    appImage.Source = RunningApp.appIcon;

                    // RnD start
                    UIRenderer = new Renderer((DroidApp)e.Parameter);
                    cpu = default;
                    if ( ((DroidApp)e.Parameter).metadata != null )
                    {
                        try
                        {
                            cpu = new DalvikCPU(((DroidApp)e.Parameter).dex,
                                ((DroidApp)e.Parameter).metadata.packageName,
                                this);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[ex] Dalvik CPU create error: " + ex.Message);
                        }
                    }
                    
                    if (cpu != null)
                    {
                        cpu.Start();
                        //await 
                        Render();
                    }
                    else
                    {
                        // Plan B - go home 
                        Frame.Navigate(typeof(MainPage));
                    }
                    
                }
                else if (e.Parameter.GetType().Equals(typeof(StorageFolder)))
                {
                    setPreloadStatusText("Setting up app environment");
                    RunningApp = await DroidApp.CreateAsync((StorageFolder)e.Parameter);
                }
            }
            catch
            {
                // Plan B - go home 
                Frame.Navigate(typeof(MainPage));
            }

        }//OnNavigatedTo end



        // setPreloadStatusText
        public void setPreloadStatusText(string text)
        {
            statusTextblock.Text = text;

        }//setPreloadStatusText end


        // preloadDone
        public void preloadDone()
        {
            PreSplashGrid.Visibility = Visibility.Collapsed;
        }//preloadDone end


        // Render
        private async void Render()
        {
            StorageFile sf = null;
            bool hasLayout = false;

            // Try to find the main layout XML. Check several common names used by Android apps.
            // If the app calls setContentView() at runtime, this fallback may not matter, but it
            // provides a best-effort static render for apps that declare a simple main layout.
            try
            {
                var layout = await UIRenderer.CurrentApp.resFolder.GetFolderAsync("layout");

                // Candidate layout file names in priority order
                string[] candidateLayouts = { "activity_main.xml", "main.xml", "main_activity.xml",
                                              "content_main.xml", "fragment_main.xml" };
                foreach (string candidate in candidateLayouts)
                {
                    try
                    {
                        sf = await layout.GetFileAsync(candidate);
                        hasLayout = true;
                        Debug.WriteLine("[EmuPage] [Render] Found layout: " + candidate);
                        break;
                    }
                    catch { /* try next */ }
                }

                if (!hasLayout)
                    Debug.WriteLine("[EmuPage] [Render] No standard layout XML found in res/layout.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[EmuPage] [Render] res/layout folder not found: " + ex.Message);
                Debug.WriteLine("[EmuPage] [Render] App has no XML layout - using native render surface.");
            }

            if (hasLayout && sf != null)
            {
                // App has an XML layout: inflate and display it
                UIElement renderedLayout = null;
                try
                {
                    renderedLayout = await UIRenderer.RenderXmlFile(sf);
                }
                catch (Exception ex2)
                {
                    Debug.WriteLine("[EmuPage] [Render] RenderXmlFile exception: " + ex2.Message);
                }

                if (renderedLayout != null)
                {
                    try
                    {
                        RenderTargetGrid.Children.Clear();
                        RenderTargetGrid.Children.Add(renderedLayout);
                        Debug.WriteLine("[EmuPage] [Render] XML layout added to RenderTargetGrid.");
                    }
                    catch (Exception ex3)
                    {
                        Debug.WriteLine("[EmuPage] [Render] RenderTargetGrid.Children.Add exception: " + ex3.Message);
                    }
                }
            }
            else
            {
                // No XML layout found - app likely renders via native OpenGL (e.g. Angry Birds).
                // Only create the native surface if setContentView has not already populated the grid.
                // All UI modifications happen on the UI thread (UWP serializes all UI updates),
                // so Children.Count is safe to read here without additional synchronisation.
                if (RenderTargetGrid.Children.Count == 0)
                {
                    Debug.WriteLine("[EmuPage] [Render] Creating full-screen AndroidRenderSurface for native rendering.");
                    var nativeSurface = new DalvikUWPCSharp.Reassembly.UI.AndroidRenderSurface();
                    nativeSurface.HorizontalAlignment = HorizontalAlignment.Stretch;
                    nativeSurface.VerticalAlignment = VerticalAlignment.Stretch;
                    RenderTargetGrid.Children.Add(nativeSurface);
                    Debug.WriteLine("[EmuPage] [Render] Native render surface ready.");
                }
                else
                {
                    Debug.WriteLine("[EmuPage] [Render] RenderTargetGrid already populated by setContentView, skipping native surface creation.");
                }
            }

            SetTitleBarColor(attr.colorPrimaryDark);
            Windows.UI.ViewManagement.ApplicationView.GetForCurrentView();
        }


        // SetContentView
        public void SetContentView(View v)
        {
            //TEST
            RenderTargetGrid.Children.Clear();
            RenderTargetGrid.Children.Add(v);

        }//SetContentView end


        // SetNativeRenderSurface - used by DalvikCPU when setContentView is called with
        // a native GLSurfaceView (no managed equivalent). Sets a full-screen render surface.
        // Must be called on the UI thread.
        public void SetNativeRenderSurface(DalvikUWPCSharp.Reassembly.UI.AndroidRenderSurface surface)
        {
            Debug.WriteLine("[EmuPage] SetNativeRenderSurface called.");
            RenderTargetGrid.Children.Clear();
            RenderTargetGrid.Children.Add(surface);
        }//SetNativeRenderSurface end


        // SetTitleBarColor 
        public void SetTitleBarColor(Color color)
        {
            var appView = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView();
            var titleBar = appView.TitleBar;
            titleBar.BackgroundColor = color;
            titleBar.ButtonBackgroundColor = color;
            titleBar.ButtonInactiveBackgroundColor = color;
            titleBar.InactiveBackgroundColor = color;

            Color hover = ColorUtil.HoverColor(color);
            Color pressed = ColorUtil.PressedColor(color);
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonPressedBackgroundColor = pressed;

            //appView.Title = UIRenderer.CurrentApp.metadata.label;
            //titleBar.ButtonHoverBackgroundColor = Color.FromArgb(10, 255, 255, 255);

            if (ApiInformation.IsTypePresent("Windows.UI.ViewManagement.StatusBar"))
            {
                Windows.UI.ViewManagement.StatusBar.GetForCurrentView().BackgroundColor = color;
                Windows.UI.ViewManagement.StatusBar.GetForCurrentView().BackgroundOpacity = 1;
                Windows.UI.ViewManagement.StatusBar.GetForCurrentView().ForegroundColor = Colors.White;
            }

        }//SetTitleBarColor end

        // SetNavBarColor
        public void SetNavBarColor(Color color)
        {
            NavBarBackgroundGrid.Background = new SolidColorBrush(color);

        }//SetNavBarColor end


        // SetWinBackColor
        public void SetWinBackColor(Color color)
        {
            //TEST
            RenderTargetGrid.Background = new SolidColorBrush(color);

        }//SetWinBackColor end


        // * RenderPage *
        private async Task RenderPage()
        {
            
            //Take content_main.xml and render it (for now)
            //var resFolder doc = RunningApp.localAppRoot.GetFolderAsync()

            // RnD start
            var layout = await UIRenderer.CurrentApp.resFolder.GetFolderAsync("layout");

            StorageFile sf = await layout.GetFileAsync("content_main.xml");

            using (MemoryStream stream = new MemoryStream(await Disassembly.Util.ReadFile(sf)))
            {
                AndroidXmlReader reader = new AndroidXmlReader(stream);

                reader.MoveToContent();
                XDocument document = XDocument.Load(reader);

                string p1nspace = "{http://schemas.android.com/apk/res/android}";

                foreach (XElement xe in document.Element("RelativeLayout").Elements())
                {
                    if(xe.Name.ToString().Equals("TextView"))
                    {
                        TextBlock tv = new TextBlock();

                        //default position for android app

                        tv.HorizontalAlignment = HorizontalAlignment.Center;
                        tv.VerticalAlignment = VerticalAlignment.Center;
                        
                        string content = "";
                        foreach(XAttribute xa in xe.Attributes())
                        {
                            content += $"Attribute: {xa.Name}\nValue: {xa.Value}\nIsNamespaceDeclaration: {xa.IsNamespaceDeclaration}\n\n";
                        } 
                        
                        //
                        foreach(XAttribute attr in xe.Attributes())
                        {
                            //attr.
                        }
                        //
                        //var ns = document.Root.Name.Namespace;
                        
                        //This is a hack
                        string[] content1 = xe.ToString().Split('"');
                        
                        // ...
                        foreach(string s in content1)
                        {
                            if(!s.Contains("p1") && !s.Contains("-2"))
                            {
                                tv.Text = s;
                                break;
                            }
                        }
                        string content2 = xe.Attribute(p1nspace+"text").Value;

                        //-2 represents "wrap_content", essentially "autosize"
                        int width = int.Parse(xe.Attribute(p1nspace + "layout_width").Value);
                        int height = int.Parse(xe.Attribute(p1nspace + "layout_height").Value);
                        
                        //string content2 = "null";
                        tv.Text = content2;

                        RenderTargetGrid.Children.Add(tv);
                    }
                }
                
                //decoded = document.ToString();
            }
           
        }//RenderPage end


        // * EmuPage_Loaded *
        private void EmuPage_Loaded(object sender, RoutedEventArgs e)
        {
            var appView = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView();

            try
            {
                appView.Title = RunningApp.metadata.label;
            }
            catch (Exception ex1)
            {
                Debug.WriteLine("[ex] EmuPage_Loaded problems: " + ex1.Message);
                Frame.Navigate(typeof(MainPage));
                return;
            }

            // CPU was already created and started in OnNavigatedTo; do not recreate it here.
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
            SystemNavigationManager.GetForCurrentView().BackRequested += EmuPage_BackRequested;

        }// EmuPage_Loaded end


        // EmuPage_BackRequested
        private void EmuPage_BackRequested(object sender, BackRequestedEventArgs e)
        {
            GoBack(sender, null);

        }//EmuPage_BackRequested end


        // Xbox gamepad input handler - maps gamepad buttons to Android input actions
        private void EmuPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var action = XboxPlatform.MapGamepadToAndroid(e.Key);
            switch (action)
            {
                case AndroidInputAction.Back:
                    GoBack(sender, null);
                    e.Handled = true;
                    break;
                case AndroidInputAction.Home:
                    GoHome(sender, null);
                    e.Handled = true;
                    break;
                case AndroidInputAction.Menu:
                    // Future: trigger Android options menu
                    Debug.WriteLine("[EmuPage] Menu button pressed (gamepad)");
                    break;
            }
        }


        // GoHome
        private void GoHome(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));

        }//GoHome end


        // GoBack
        private void GoBack(object sender, RoutedEventArgs e)
        {
            try
            {
                cpu.GoBack();
            }
            catch
            {
                // Plan B - go home
                Frame.Navigate(typeof(MainPage));
            }
        }//GoBack end

    }//EmuPage class end

}//namespace end
