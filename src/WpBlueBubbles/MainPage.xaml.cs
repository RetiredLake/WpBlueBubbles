using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.ApplicationModel.Contacts;
using Windows.Foundation;
using Windows.UI.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using WpBlueBubbles.Models;
using WpBlueBubbles.Services;

namespace WpBlueBubbles
{
    public sealed partial class MainPage : Page
    {
        public bool IsClientReady { get { return _client != null; } }
        private readonly ObservableCollection<ChatItem> _chats = new ObservableCollection<ChatItem>();
        private readonly List<ChatItem> _allChats = new List<ChatItem>();
        private readonly ObservableCollection<ChatItem> _recipientMatches = new ObservableCollection<ChatItem>();
        private readonly ObservableCollection<MessageItem> _messages = new ObservableCollection<MessageItem>();
        private readonly ObservableCollection<ContactChoice> _contacts = new ObservableCollection<ContactChoice>();
        private readonly List<ContactChoice> _allContacts = new List<ContactChoice>();
        private readonly DispatcherTimer _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        private readonly DispatcherTimer _qrScanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        private BlueBubblesClient _client;
        private QrCameraScanner _qrScanner;
        private ChatItem _selectedChat;
        private bool _isRefreshing;
        private bool _isQrScanInProgress;
        private int _messagesPerChat = 25;
        private int _syncTimeframeDays;
        private IReadOnlyDictionary<string, string> _contactNames = new Dictionary<string, string>();
        private ShareOperation _shareOperation;
        private StorageFile _sharedFile;
        private string _sharedText;
        private bool _loadingNotificationSetting;
        private bool _showArchived;
        private readonly InputPane _inputPane;

        public MainPage()
        {
            InitializeComponent();
            _inputPane = InputPane.GetForCurrentView();
            ChatsList.ItemsSource = _chats;
            MessagesList.ItemsSource = _messages;
            RecipientMatchesList.ItemsSource = _recipientMatches;
            ContactsList.ItemsSource = _contacts;
            _pollTimer.Tick += PollTimer_Tick;
            _qrScanTimer.Tick += QrScanTimer_Tick;
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
            SizeChanged += MainPage_SizeChanged;
            SystemNavigationManager.GetForCurrentView().BackRequested += MainPage_BackRequested;
            _inputPane.Showing += InputPane_Showing;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateResponsiveLayout();
            var settings = SettingsStore.Load();
            ServerAddressBox.Text = settings.Address ?? string.Empty;
            ServerPasswordBox.Password = settings.Password ?? string.Empty;
            _messagesPerChat = settings.MessagesPerChat;
            _syncTimeframeDays = settings.SyncTimeframeDays;
            MessagesPerChatSlider.Value = _messagesPerChat;
            SelectTimeframe(_syncTimeframeDays);
            UpdateSyncDescription();
            _loadingNotificationSetting = true;
            NotificationsToggle.IsOn = NotificationService.IsEnabled;
            _loadingNotificationSetting = false;
            UpdateNotificationStatus();
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
            _qrScanTimer.Stop();
            _qrScanner?.Dispose();
            if (_client != null) _client.Dispose();
            SystemNavigationManager.GetForCurrentView().BackRequested -= MainPage_BackRequested;
            _inputPane.Showing -= InputPane_Showing;
        }

        private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e) { UpdateResponsiveLayout(); }

        private void UpdateResponsiveLayout()
        {
            if (ActualWidth >= 720)
            {
                ChatColumn.Width = new GridLength(360);
                DividerColumn.Width = new GridLength(1);
                ChatsPane.Visibility = Visibility.Visible;
                ConversationPane.Visibility = Visibility.Visible;
                MenuButton.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
                SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
            }
            else if (_selectedChat == null || ChatsPane.Visibility == Visibility.Visible)
            {
                ChatColumn.Width = new GridLength(1, GridUnitType.Star);
                DividerColumn.Width = new GridLength(0);
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
                SettingsStore.SaveSyncOptions(_messagesPerChat, _syncTimeframeDays);
                SetSyncing(true, "Syncing chats...");
                await LoadContactsAsync();
                await RefreshChatsAsync();
                _pollTimer.Start();
                if (closeSettings) SettingsOverlay.Visibility = Visibility.Collapsed;
                OpenPendingActivation();
                ShowStatus(string.Empty, false);
            }
            catch (Exception ex)
            {
                ConnectError.Text = ex.Message;
                SettingsOverlay.Visibility = Visibility.Visible;
                ShowStatus("Could not connect", true);
            }
            finally
            {
                SetSyncing(false, null);
                ConnectButton.IsEnabled = true;
                ConnectProgress.IsActive = false;
            }
        }

        private async Task RefreshChatsAsync()
        {
            if (_client == null) return;
            var chats = await _client.GetChatsAsync();
            var selectedGuid = _selectedChat == null ? null : _selectedChat.Guid;
            _allChats.Clear();
            _allChats.AddRange(chats);
            foreach (var chat in _allChats) chat.ApplyContactNames(_contactNames);
            ApplyChatSearch();
            if (selectedGuid != null) _selectedChat = _allChats.FirstOrDefault(c => c.Guid == selectedGuid) ?? _selectedChat;
            if (NotificationService.IsEnabled) await NotificationService.ObserveChatsAsync(_allChats, false);
        }

        private async Task RefreshMessagesAsync(bool forceScroll)
        {
            if (_client == null || _selectedChat == null) return;
            var received = await _client.GetMessagesAsync(_selectedChat.Guid, _messagesPerChat, _syncTimeframeDays);
            var priorLastGuid = _messages.Count == 0 ? null : _messages[_messages.Count - 1].Guid;
            var newLastGuid = received.Count == 0 ? null : received[received.Count - 1].Guid;
            if (!forceScroll && priorLastGuid == newLastGuid && _messages.Count == received.Count) return;
            _messages.Clear();
            var total = received.Count;
            for (var i = 0; i < total; i++)
            {
                var message = received[i];
                if (message.IsImageAttachment) message.SetAttachmentUri(_client.GetAttachmentDownloadUri(message.AttachmentGuid));
                _messages.Add(message);
            }
            SetSyncing(true, "Syncing message " + total + " of " + Math.Max(total, _messagesPerChat) + "...");
            if (_messages.Count > 0) MessagesList.ScrollIntoView(_messages[_messages.Count - 1]);
            NotificationStateStore.MarkRead(_selectedChat.Guid);
            await NotificationService.UpdateTileAsync();
        }

        private async void PollTimer_Tick(object sender, object e)
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            SetSyncing(true, "Syncing chats...");
            try
            {
                await RefreshChatsAsync();
                await RefreshMessagesAsync(false);
            }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
            finally { _isRefreshing = false; SetSyncing(false, null); }
        }

        private async void ChatsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            _selectedChat = e.ClickedItem as ChatItem;
            if (_selectedChat == null) return;
            UpdateHeaderActions(true);
            NotificationStateStore.MarkRead(_selectedChat.Guid);
            await NotificationService.UpdateTileAsync();
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
            try { SetSyncing(true, "Syncing messages..."); await RefreshMessagesAsync(true); ShowStatus(string.Empty, false); }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
            finally { SetSyncing(false, null); }
        }

        private async Task SendCurrentMessageAsync()
        {
            var text = MessageBox.Text.Trim();
            if (_client == null || _selectedChat == null || (text.Length == 0 && _sharedFile == null)) return;
            SendButton.IsEnabled = false;
            MessageBox.IsEnabled = false;
            try
            {
                if (_sharedFile != null) await _client.SendPhotoAsync(_selectedChat.Guid, _sharedFile);
                if (text.Length > 0) await _client.SendTextAsync(_selectedChat.Guid, text);
                MessageBox.Text = string.Empty;
                CompleteSharedContent();
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
            QrPayloadBox.Text = string.Empty;
            QrPayloadBox.Visibility = Visibility.Collapsed;
            await StartClientAsync(payload.Address, payload.Password, true);
        }

        private async void ScanQr_Click(object sender, RoutedEventArgs e)
        {
            ConnectError.Text = string.Empty;
            try
            {
                _qrScanner?.Dispose();
                _qrScanner = new QrCameraScanner();
                QrScannerOverlay.Visibility = Visibility.Visible;
                await _qrScanner.StartAsync(QrCameraPreview);
                _qrScanTimer.Start();
            }
            catch (Exception ex)
            {
                await StopQrScannerAsync();
                ConnectError.Text = "Camera access is needed to scan a setup QR code: " + ex.Message;
            }
        }

        private async void QrScanTimer_Tick(object sender, object e)
        {
            if (_isQrScanInProgress || _qrScanner == null) return;
            _isQrScanInProgress = true;
            try
            {
                var payloadText = await _qrScanner.TryReadAsync();
                if (string.IsNullOrWhiteSpace(payloadText)) return;
                QrPayloadBox.Text = payloadText;
                await StopQrScannerAsync();
                ConnectQr_Click(this, null);
            }
            catch { }
            finally { _isQrScanInProgress = false; }
        }

        private async void CancelQrScan_Click(object sender, RoutedEventArgs e) { await StopQrScannerAsync(); }

        private async Task StopQrScannerAsync()
        {
            _qrScanTimer.Stop();
            if (_qrScanner != null) await _qrScanner.StopAsync(QrCameraPreview);
            QrScannerOverlay.Visibility = Visibility.Collapsed;
        }

        private async void Attach_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || _selectedChat == null) return;
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".heic");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".pdf");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            AttachButton.IsEnabled = false;
            try
            {
                ShowStatus("Uploading photo...", false);
                await _client.SendPhotoAsync(_selectedChat.Guid, file);
                await RefreshMessagesAsync(true);
                ShowStatus(string.Empty, false);
            }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
            finally { AttachButton.IsEnabled = true; }
        }

        private async void Message_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState != HoldingState.Started) return;
            var element = sender as FrameworkElement;
            var message = element?.DataContext as MessageItem;
            if (message == null || string.IsNullOrWhiteSpace(message.Text)) return;
            var menu = new PopupMenu();
            menu.Commands.Add(new UICommand("Copy"));
            var point = element.TransformToVisual(null).TransformPoint(new Point());
            var chosen = await menu.ShowForSelectionAsync(new Rect(point, element.RenderSize));
            if (chosen == null) return;
            var data = new DataPackage();
            data.SetText(message.Text);
            Clipboard.SetContent(data);
        }

        private void Compose_Click(object sender, RoutedEventArgs e)
        {
            OpenCompose();
        }

        private void OpenCompose()
        {
            RecipientBox.Text = string.Empty;
            _recipientMatches.Clear();
            foreach (var chat in _allChats.Take(12)) _recipientMatches.Add(chat);
            ComposeOverlay.Visibility = Visibility.Visible;
            SharedComposePreview.Text = BuildSharedPreview();
            SharedComposePreview.Visibility = string.IsNullOrWhiteSpace(SharedComposePreview.Text) ? Visibility.Collapsed : Visibility.Visible;
            RecipientBox.Focus(FocusState.Programmatic);
        }

        public void OpenComposeForRecipient(string recipient)
        {
            if (string.IsNullOrWhiteSpace(recipient)) return;
            OpenCompose();
            RecipientBox.Text = recipient;
        }

        public async void OpenChatFromNotification(string chatGuid)
        {
            if (string.IsNullOrWhiteSpace(chatGuid)) return;
            var chat = _allChats.FirstOrDefault(item => item.Guid == chatGuid);
            if (chat == null) return;
            _selectedChat = chat;
            UpdateHeaderActions(true);
            NotificationStateStore.MarkRead(chat.Guid);
            await NotificationService.UpdateTileAsync();
            PageTitle.Text = chat.Title;
            EmptyConversation.Visibility = Visibility.Collapsed;
            MessagesList.Visibility = Visibility.Visible;
            Composer.Visibility = Visibility.Visible;
            if (ActualWidth < 720) ReturnToConversation();
            try { await RefreshMessagesAsync(true); }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
        }

        private void CloseCompose_Click(object sender, RoutedEventArgs e)
        {
            ComposeOverlay.Visibility = Visibility.Collapsed;
            if (_shareOperation != null) CompleteSharedContent();
        }

        private async void RecipientMatchesList_ItemClick(object sender, ItemClickEventArgs e)
        {
            ComposeOverlay.Visibility = Visibility.Collapsed;
            var chat = e.ClickedItem as ChatItem;
            if (chat == null) return;
            _selectedChat = chat;
            UpdateHeaderActions(true);
            NotificationStateStore.MarkRead(chat.Guid);
            await NotificationService.UpdateTileAsync();
            PageTitle.Text = chat.Title;
            EmptyConversation.Visibility = Visibility.Collapsed;
            MessagesList.Visibility = Visibility.Visible;
            Composer.Visibility = Visibility.Visible;
            if (ActualWidth < 720) ReturnToConversation();
            StageSharedContentInComposer();
            try { SetSyncing(true, "Syncing messages..."); await RefreshMessagesAsync(true); }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
            finally { SetSyncing(false, null); }
        }

        private void RecipientBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = RecipientBox.Text.Trim();
            _recipientMatches.Clear();
            foreach (var chat in _allChats.Where(chat => string.IsNullOrWhiteSpace(query) || chat.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || chat.ParticipantSummary.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).Take(20)) _recipientMatches.Add(chat);
            ComposeHint.Text = _recipientMatches.Count == 0 && !string.IsNullOrWhiteSpace(query) ? "No existing conversation found. Sending to a new recipient will be added after the recipient flow is confirmed on your server." : "Choose an existing conversation.";
        }

        private async void ChooseContact_Click(object sender, RoutedEventArgs e)
        {
            ComposeOverlay.Visibility = Visibility.Collapsed;
            await OpenContactsAsync();
        }

        private async void Contacts_Click(object sender, RoutedEventArgs e)
        {
            NavigationSplitView.IsPaneOpen = false;
            await OpenContactsAsync();
        }

        private async Task OpenContactsAsync()
        {
            if (_allContacts.Count == 0) await LoadContactsAsync();
            if (_allContacts.Count == 0)
            {
                ShowStatus("Contacts permission is needed to show your phone contacts.", true);
                return;
            }
            ContactsSearchBox.Text = string.Empty;
            ApplyContactsSearch();
            ContactsOverlay.Visibility = Visibility.Visible;
        }

        private void CloseContacts_Click(object sender, RoutedEventArgs e)
        {
            ContactsOverlay.Visibility = Visibility.Collapsed;
        }

        private void ContactsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyContactsSearch();
        }

        private void ApplyContactsSearch()
        {
            var query = ContactsSearchBox == null ? string.Empty : ContactsSearchBox.Text.Trim();
            _contacts.Clear();
            foreach (var contact in _allContacts.Where(contact => string.IsNullOrWhiteSpace(query) || contact.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || contact.Address.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).Take(100)) _contacts.Add(contact);
        }

        private void ContactsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var contact = e.ClickedItem as ContactChoice;
            if (contact == null) return;
            ContactsOverlay.Visibility = Visibility.Collapsed;
            OpenComposeForRecipient(contact.Address);
        }

        private async void MessagesPerChatSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            _messagesPerChat = Math.Max(1, (int)Math.Round(e.NewValue));
            SettingsStore.SaveSyncOptions(_messagesPerChat, _syncTimeframeDays);
            UpdateSyncDescription();
            if (_selectedChat != null) await RefreshMessagesAsync(true);
        }

        private async void SyncTimeframeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = SyncTimeframeBox.SelectedItem as ComboBoxItem;
            if (item?.Tag != null) _syncTimeframeDays = int.Parse(item.Tag.ToString());
            SettingsStore.SaveSyncOptions(_messagesPerChat, _syncTimeframeDays);
            UpdateSyncDescription();
            if (_selectedChat != null) await RefreshMessagesAsync(true);
        }

        private async void NotificationsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingNotificationSetting) return;
            try
            {
                if (NotificationsToggle.IsOn) await NotificationService.EnableAsync();
                else await NotificationService.DisableAsync();
            }
            catch (Exception ex)
            {
                NotificationsToggle.IsOn = false;
                ShowStatus("Notifications could not be changed: " + ex.Message, true);
            }
            UpdateNotificationStatus();
        }

        private void SelectTimeframe(int days)
        {
            for (var i = 0; i < SyncTimeframeBox.Items.Count; i++)
            {
                var item = SyncTimeframeBox.Items[i] as ComboBoxItem;
                if (item?.Tag?.ToString() == days.ToString()) { SyncTimeframeBox.SelectedIndex = i; return; }
            }
            SyncTimeframeBox.SelectedIndex = 0;
        }

        private string BuildSyncDescription()
        {
            var range = _syncTimeframeDays == 0 ? "all time" : _syncTimeframeDays == 30 ? "the last 30 days" : "the last year";
            return "Loading the latest " + _messagesPerChat + " messages per chat from " + range + ".";
        }

        private void UpdateSyncDescription()
        {
            if (SyncDescription != null) SyncDescription.Text = BuildSyncDescription();
        }

        private void SetSyncing(bool syncing, string detail)
        {
            try
            {
                var statusBarType = Type.GetType("Windows.UI.ViewManagement.StatusBar, Windows, ContentType=WindowsRuntime");
                if (statusBarType == null) return;
                var phoneStatus = statusBarType.GetMethod("GetForCurrentView").Invoke(null, null);
                var indicator = statusBarType.GetProperty("ProgressIndicator").GetValue(phoneStatus);
                var indicatorType = indicator.GetType();
                indicatorType.GetProperty("Text").SetValue(indicator, detail ?? string.Empty);
                indicatorType.GetProperty("ProgressValue").SetValue(indicator, syncing ? (double?)0.5 : null);
                indicatorType.GetMethod(syncing ? "ShowAsync" : "HideAsync").Invoke(indicator, null);
            }
            catch { }
        }

        private void Menu_Click(object sender, RoutedEventArgs e) { NavigationSplitView.IsPaneOpen = !NavigationSplitView.IsPaneOpen; }
        private void Settings_Click(object sender, RoutedEventArgs e) { NavigationSplitView.IsPaneOpen = false; SettingsOverlay.Visibility = Visibility.Visible; }
        private void CloseSettings_Click(object sender, RoutedEventArgs e) { if (_client != null) SettingsOverlay.Visibility = Visibility.Collapsed; }
        private void Chats_Click(object sender, RoutedEventArgs e) { ShowChatPage(false); }
        private void Archived_Click(object sender, RoutedEventArgs e) { ShowChatPage(true); }
        private void Back_Click(object sender, RoutedEventArgs e) { ReturnToChats(); }

        private void ShowChatPage(bool archived)
        {
            NavigationSplitView.IsPaneOpen = false;
            _showArchived = archived;
            PageTitle.Text = archived ? "Archived" : "Chats";
            ApplyChatSearch();
            _selectedChat = null;
            EmptyConversation.Visibility = Visibility.Visible;
            MessagesList.Visibility = Visibility.Collapsed;
            Composer.Visibility = Visibility.Collapsed;
            UpdateHeaderActions(false);
            ReturnToChats();
        }

        private void UpdateHeaderActions(bool conversationOpen)
        {
            if (ComposeButton != null) ComposeButton.Visibility = conversationOpen ? Visibility.Collapsed : Visibility.Visible;
            if (ChatActionsButton != null) ChatActionsButton.Visibility = conversationOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ChatActions_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChat == null) return;
            var archive = new UICommand(_selectedChat.IsArchived ? "Unarchive" : "Archive");
            var delete = new UICommand("Delete chat");
            var menu = new PopupMenu();
            menu.Commands.Add(archive);
            menu.Commands.Add(delete);
            var point = ChatActionsButton.TransformToVisual(null).TransformPoint(new Point());
            var selected = await menu.ShowForSelectionAsync(new Rect(point, ChatActionsButton.RenderSize));
            if (selected == archive)
            {
                await ShowArchiveAvailabilityAsync();
            }
            else if (selected == delete)
            {
                await DeleteSelectedChatAsync();
            }
        }

        private async Task ShowArchiveAvailabilityAsync()
        {
            var action = _selectedChat != null && _selectedChat.IsArchived ? "unarchive" : "archive";
            var dialog = new MessageDialog("The current BlueBubbles REST API reports archive state but does not expose a way for this client to " + action + " a chat. Change the archive state in Messages or an official BlueBubbles client, then refresh here.", "Archive unavailable");
            dialog.Commands.Add(new UICommand("OK"));
            await dialog.ShowAsync();
        }

        private async Task DeleteSelectedChatAsync()
        {
            if (_client == null || _selectedChat == null) return;
            var title = _selectedChat.Title;
            var dialog = new MessageDialog("Delete \"" + title + "\" permanently from the BlueBubbles server? This cannot be undone.", "Delete conversation?");
            var confirm = new UICommand("Delete permanently");
            dialog.Commands.Add(confirm);
            dialog.Commands.Add(new UICommand("Cancel"));
            dialog.DefaultCommandIndex = 1;
            dialog.CancelCommandIndex = 1;
            var selected = await dialog.ShowAsync();
            if (selected != confirm) return;

            try
            {
                SetSyncing(true, "Deleting conversation...");
                await _client.DeleteChatAsync(_selectedChat.Guid);
                ShowChatPage(_showArchived);
                await RefreshChatsAsync();
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message, true);
            }
            finally
            {
                SetSyncing(false, null);
            }
        }

        private void InputPane_Showing(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            if (_messages.Count > 0) MessagesList.ScrollIntoView(_messages[_messages.Count - 1]);
        }
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
            UpdateHeaderActions(false);
        }

        private void ReturnToConversation()
        {
            ChatsPane.Visibility = Visibility.Collapsed;
            ConversationPane.Visibility = Visibility.Visible;
            MenuButton.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Visible;
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyChatSearch();
        }

        private void ApplyChatSearch()
        {
            var query = SearchBox == null ? string.Empty : SearchBox.Text.Trim();
            _chats.Clear();
            foreach (var chat in _allChats.Where(chat => chat.IsArchived == _showArchived && (string.IsNullOrWhiteSpace(query) || chat.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || chat.Preview.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || chat.ParticipantSummary.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))) _chats.Add(chat);
        }

        private async Task LoadContactsAsync()
        {
            try
            {
                var service = new ContactsService();
                var contacts = await service.LoadContactsAsync();
                _allContacts.Clear();
                _allContacts.AddRange(contacts);
                ApplyContactsSearch();
                _contactNames = await service.LoadNamesAsync();
            }
            catch
            {
                _allContacts.Clear();
                _contacts.Clear();
                _contactNames = new Dictionary<string, string>();
            }
        }

        private void OpenPendingActivation()
        {
            var app = Application.Current as App;
            var chatGuid = app?.TakePendingChatGuid();
            if (!string.IsNullOrWhiteSpace(chatGuid))
            {
                var chat = _allChats.FirstOrDefault(item => item.Guid == chatGuid);
                if (chat != null) OpenChatFromNotification(chatGuid);
            }
            var recipient = app?.TakePendingRecipient();
            if (!string.IsNullOrWhiteSpace(recipient)) OpenComposeForRecipient(recipient);
        }

        private void UpdateNotificationStatus()
        {
            if (NotificationStatusText != null) NotificationStatusText.Text = NotificationService.Status;
        }

        public async void PrepareSharedContent(ShareOperation operation)
        {
            try
            {
                var view = operation.Data;
                _shareOperation = operation;
                operation.ReportStarted();
                if (view.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await view.GetStorageItemsAsync();
                    _sharedFile = items.OfType<StorageFile>().FirstOrDefault();
                    if (_sharedFile == null) { ShowStatus("No supported file was provided.", true); CompleteSharedContent(); return; }
                }
                if (view.Contains(StandardDataFormats.Text))
                {
                    _sharedText = await view.GetTextAsync();
                }
                if (_sharedFile == null && string.IsNullOrWhiteSpace(_sharedText)) { ShowStatus("No shareable item was provided.", true); CompleteSharedContent(); return; }
                OpenCompose();
            }
            catch
            {
                operation.ReportError("BlueBubbles could not read the shared item.");
                ClearSharedContent();
            }
        }

        private string BuildSharedPreview()
        {
            if (_sharedFile != null && !string.IsNullOrWhiteSpace(_sharedText)) return "Sharing " + _sharedFile.Name + " and text.";
            if (_sharedFile != null) return "Sharing " + _sharedFile.Name + ".";
            return string.IsNullOrWhiteSpace(_sharedText) ? string.Empty : "Sharing text.";
        }

        private void StageSharedContentInComposer()
        {
            if (_sharedFile == null && string.IsNullOrWhiteSpace(_sharedText)) return;
            if (!string.IsNullOrWhiteSpace(_sharedText)) MessageBox.Text = _sharedText;
            SharedAttachmentBanner.Text = BuildSharedPreview();
            SharedAttachmentBanner.Visibility = Visibility.Visible;
        }

        private void CompleteSharedContent()
        {
            try { _shareOperation?.ReportCompleted(); } catch { }
            ClearSharedContent();
        }

        private void ClearSharedContent()
        {
            _shareOperation = null;
            _sharedFile = null;
            _sharedText = null;
            SharedAttachmentBanner.Visibility = Visibility.Collapsed;
            SharedComposePreview.Visibility = Visibility.Collapsed;
        }

        private void AttachmentImage_Failed(object sender, ExceptionRoutedEventArgs e)
        {
            var image = sender as Image;
            var message = image?.DataContext as MessageItem;
            if (message != null) message.MarkAttachmentFailed();
        }

        private void ShowStatus(string message, bool isError)
        {
            StatusText.Text = message;
            StatusText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(isError ? Windows.UI.Color.FromArgb(255, 255, 140, 130) : Windows.UI.Colors.White);
            StatusBar.Visibility = isError && !string.IsNullOrWhiteSpace(message) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
