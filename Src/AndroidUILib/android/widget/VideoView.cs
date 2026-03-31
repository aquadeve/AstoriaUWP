using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Displays a video file. Maps to UWP MediaElement control.
    /// </summary>
    public class VideoView : View
    {
        private MediaElement mediaElement = new MediaElement();

        public VideoView(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            mediaElement.HorizontalAlignment = HorizontalAlignment.Stretch;
            mediaElement.VerticalAlignment = VerticalAlignment.Stretch;
            WinUI.Content = mediaElement;
        }

        public void setVideoURI(Uri uri)
        {
            mediaElement.Source = uri;
        }

        public void start() { mediaElement.Play(); }

        public void pause() { mediaElement.Pause(); }

        public void stopPlayback() { mediaElement.Stop(); }

        public void resume() { mediaElement.Play(); }

        public bool isPlaying()
        {
            return mediaElement.CurrentState == Windows.UI.Xaml.Media.MediaElementState.Playing;
        }

        public int getDuration()
        {
            if (mediaElement.NaturalDuration.HasTimeSpan)
                return (int)mediaElement.NaturalDuration.TimeSpan.TotalMilliseconds;
            return 0;
        }

        public int getCurrentPosition()
        {
            return (int)mediaElement.Position.TotalMilliseconds;
        }

        public void seekTo(int msec)
        {
            mediaElement.Position = TimeSpan.FromMilliseconds(msec);
        }
    }
}
