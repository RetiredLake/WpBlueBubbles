using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WpBlueBubbles
{
    sealed partial class App : Application
    {
        public App()
        {
            UnhandledException += App_UnhandledException;
            try
            {
                InitializeComponent();
            }
            catch (System.Exception ex)
            {
                WriteStartupError(ex);
                throw;
            }
            Suspending += OnSuspending;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null)
            {
                frame = new Frame();
                Window.Current.Content = frame;
            }
            if (frame.Content == null)
            {
                try
                {
                    frame.Navigate(typeof(MainPage));
                }
                catch (System.Exception ex)
                {
                    WriteStartupError(ex);
                    frame.Content = new TextBlock { Text = "BlueBubbles could not start:\r\n\r\n" + ex, TextWrapping = TextWrapping.Wrap };
                }
            }
            Window.Current.Activate();
        }

        protected override void OnShareTargetActivated(ShareTargetActivatedEventArgs args)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null)
            {
                frame = new Frame();
                Window.Current.Content = frame;
            }
            if (frame.Content == null) frame.Navigate(typeof(MainPage));
            var page = frame.Content as MainPage;
            page?.PrepareSharedContent(args.ShareOperation);
            Window.Current.Activate();
        }

        private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            WriteStartupError(e.Exception);
        }

        private static void WriteStartupError(System.Exception exception)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values["StartupError"] = exception.ToString();
            }
            catch { }
        }

        private void OnSuspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e) { }
    }
}
