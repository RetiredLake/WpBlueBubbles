using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using WpBlueBubbles.Models;
using WpBlueBubbles.Services;

namespace WpBlueBubbles
{
    public sealed partial class MainPage : Page
    {
        private readonly ObservableCollection<ChatItem> _chats = new ObservableCollection<ChatItem>();
        private readonly ObservableCollection<MessageItem> _messages = new ObservableCollection<MessageItem>();
        private readonly DispatcherTimer _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        private BlueBubblesClient _client;
        private ChatItem _selectedChat;
        private bool _isRefreshing;

        public MainPage()
        {
            InitializeComponent();
            ChatsList.ItemsSource = _chats;
            MessagesList.ItemsSource = _messages;
            _pollTimer.Tick += PollTimer_Tick;
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
            SizeChanged += MainPage_SizeChanged;
            SystemNavigationManager.GetForCurrentView().BackRequested += MainPage_BackRequested;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateResponsiveLayout();
            var settings = SettingsStore.Load();
            ServerAddressBox.Text = settings.Address ?? string.Empty;
            ServerPasswordBox.Password = settings.Password ?? string.Empty;
            if (!settings.IsComplete)
            {
                SettingsOverlay.Visibility = Visibility.Visible;
                return;
            }
            await StartClientAsync(settings.Address, settings.Password, false);
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _pollTimer.Stop();
            if (_client != null) _client.Dispose();
            SystemNavigationManager.GetForCurrentView().BackRequested -= MainPage_BackRequested;
        }

        private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e) { UpdateResponsiveLayout(); }

        private void UpdateResponsiveLayout()
        {
            if (ActualWidth >= 720)
            {
                ChatsPane.Visibility = Visibility.Visible;
                ConversationPane.Visibility = Visibility.Visible;
                MenuButton.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
                SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
            }
            else if (_selectedChat == null || ChatsPane.Visibility == Visibility.Visible)
            {
                ChatsPane.Visibility = Visibility.Visible;
                ConversationPane.Visibility = Visibility.Collapsed;
                MenuButton.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
            }
        }

        private async Task StartClientAsync(string address, string password, bool closeSettings)
        {
            ConnectButton.IsEnabled = false;
            ConnectProgress.IsActive = true;
            ConnectError.Text = string.Empty;
            try
            {
                if (_client != null) _client.Dispose();
                _client = new BlueBubblesClient(address, password);
                await _client.TestConnectionAsync();
                SettingsStore.Save(address, password);
                await RefreshChatsAsync();
                _pollTimer.Start();
                if (closeSettings) SettingsOverlay.Visibility = Visibility.Collapsed;
                ShowStatus("Connected", false);
            }
            catch (Exception ex)
            {
                ConnectError.Text = ex.Message;
                SettingsOverlay.Visibility = Visibility.Visible;
                ShowStatus("Could not connect", true);
            }
            finally
            {
                ConnectButton.IsEnabled = true;
                ConnectProgress.IsActive = false;
            }
        }

        private async Task RefreshChatsAsync()
        {
            if (_client == null) return;
            var chats = await _client.GetChatsAsync();
            var selectedGuid = _selectedChat == null ? null : _selectedChat.Guid;
            _chats.Clear();
            foreach (var chat in chats) _chats.Add(chat);
            if (selectedGuid != null) _selectedChat = _chats.FirstOrDefault(c => c.Guid == selectedGuid) ?? _selectedChat;
        }

        private async Task RefreshMessagesAsync(bool forceScroll)
        {
            if (_client == null || _selectedChat == null) return;
            var received = await _client.GetMessagesAsync(_selectedChat.Guid, 100);
            var priorLastGuid = _messages.Count == 0 ? null : _messages[_messages.Count - 1].Guid;
            var newLastGuid = received.Count == 0 ? null : received[received.Count - 1].Guid;
            if (!forceScroll && priorLastGuid == newLastGuid && _messages.Count == received.Count) return;
            _messages.Clear();
            foreach (var message in received) _messages.Add(message);
            if (_messages.Count > 0) MessagesList.ScrollIntoView(_messages[_messages.Count - 1]);
        }

        private async void PollTimer_Tick(object sender, object e)
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            try
            {
                await RefreshChatsAsync();
                await RefreshMessagesAsync(false);
            }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
            finally { _isRefreshing = false; }
        }

        private async void ChatsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            _selectedChat = e.ClickedItem as ChatItem;
            if (_selectedChat == null) return;
            PageTitle.Text = _selectedChat.Title;
            EmptyConversation.Visibility = Visibility.Collapsed;
            MessagesList.Visibility = Visibility.Visible;
            Composer.Visibility = Visibility.Visible;
            if (ActualWidth < 720)
            {
                ChatsPane.Visibility = Visibility.Collapsed;
                ConversationPane.Visibility = Visibility.Visible;
                MenuButton.Visibility = Visibility.Collapsed;
                BackButton.Visibility = Visibility.Visible;
                SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
            }
            ShowStatus("Loading messages...", false);
            try { await RefreshMessagesAsync(true); ShowStatus(string.Empty, false); }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
        }

        private async Task SendCurrentMessageAsync()
        {
            var text = MessageBox.Text.Trim();
            if (_client == null || _selectedChat == null || text.Length == 0) return;
            SendButton.IsEnabled = false;
            MessageBox.IsEnabled = false;
            try
            {
                await _client.SendTextAsync(_selectedChat.Guid, text);
                MessageBox.Text = string.Empty;
                await RefreshMessagesAsync(true);
            }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
            finally { SendButton.IsEnabled = true; MessageBox.IsEnabled = true; MessageBox.Focus(FocusState.Programmatic); }
        }

        private async void Send_Click(object sender, RoutedEventArgs e) { await SendCurrentMessageAsync(); }
        private async void MessageBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter && Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down) == false)
            {
                e.Handled = true;
                await SendCurrentMessageAsync();
            }
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            await StartClientAsync(ServerAddressBox.Text, ServerPasswordBox.Password, true);
        }

        private void ManualSetup_Click(object sender, RoutedEventArgs e)
        {
            ManualSetupPanel.Visibility = Visibility.Visible;
            QrSetupPanel.Visibility = Visibility.Collapsed;
            ConnectError.Text = string.Empty;
        }

        private void QrSetup_Click(object sender, RoutedEventArgs e)
        {
            ManualSetupPanel.Visibility = Visibility.Collapsed;
            QrSetupPanel.Visibility = Visibility.Visible;
            ConnectError.Text = string.Empty;
        }

        private async void ConnectQr_Click(object sender, RoutedEventArgs e)
        {
            QrSetupPayload payload;
            string error;
            if (!QrSetupPayload.TryParse(QrPayloadBox.Text, out payload, out error))
            {
                ConnectError.Text = error;
                return;
            }

            ServerAddressBox.Text = payload.Address;
            ServerPasswordBox.Password = payload.Password;
            await StartClientAsync(payload.Address, payload.Password, true);
        }

        private void Menu_Click(object sender, RoutedEventArgs e) { NavigationSplitView.IsPaneOpen = !NavigationSplitView.IsPaneOpen; }
        private void Settings_Click(object sender, RoutedEventArgs e) { NavigationSplitView.IsPaneOpen = false; SettingsOverlay.Visibility = Visibility.Visible; }
        private void CloseSettings_Click(object sender, RoutedEventArgs e) { if (_client != null) SettingsOverlay.Visibility = Visibility.Collapsed; }
        private void Chats_Click(object sender, RoutedEventArgs e) { NavigationSplitView.IsPaneOpen = false; ReturnToChats(); }
        private void Back_Click(object sender, RoutedEventArgs e) { ReturnToChats(); }
        private void MainPage_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (SettingsOverlay.Visibility == Visibility.Visible && _client != null) { SettingsOverlay.Visibility = Visibility.Collapsed; e.Handled = true; }
            else if (ActualWidth < 720 && ChatsPane.Visibility == Visibility.Collapsed) { ReturnToChats(); e.Handled = true; }
        }

        private void ReturnToChats()
        {
            if (ActualWidth < 720)
            {
                ChatsPane.Visibility = Visibility.Visible;
                ConversationPane.Visibility = Visibility.Collapsed;
                MenuButton.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
                PageTitle.Text = "Chats";
                SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Server-side chat search is deferred; keep the field ready for the next iteration.
        }

        private void ShowStatus(string message, bool isError)
        {
            StatusText.Text = message;
            StatusText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(isError ? Windows.UI.Color.FromArgb(255, 255, 140, 130) : Windows.UI.Colors.White);
            StatusBar.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
