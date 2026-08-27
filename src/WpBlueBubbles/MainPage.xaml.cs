using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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
using Windows.UI.StartScreen;
using Windows.System.Profile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Documents;
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
        private readonly DispatcherTimer _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        private readonly DispatcherTimer _qrScanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        private readonly DispatcherTimer _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private BlueBubblesClient _client;
        private QrCameraScanner _qrScanner;
        private ChatItem _selectedChat;
        private bool _isRefreshing;
        private bool _isQrScanInProgress;
        private int _messagesPerChat = 15;
        private int _syncTimeframeDays = 7;
        private IReadOnlyDictionary<string, string> _contactNames = new Dictionary<string, string>();
        private ShareOperation _shareOperation;
        private StorageFile _sharedFile;
        private string _sharedText;
        private bool _showArchived;
        private ChatItem _composeSelectedChat;
        private bool _updatingRecipient;
        private bool _statusIsError;
        private readonly InputPane _inputPane;
        private bool _settingsLoaded;
        private string _chatStateSignature;
        private int _messageLoadGeneration;
        private static readonly bool UseLegacyInAppSyncStatus = false;
        private static readonly bool UsePhoneSyncStatus = false;

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
            _statusTimer.Tick += StatusTimer_Tick;
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
            SizeChanged += MainPage_SizeChanged;
            SystemNavigationManager.GetForCurrentView().BackRequested += MainPage_BackRequested;
            _inputPane.Showing += InputPane_Showing;
            _inputPane.Hiding += InputPane_Hiding;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            ApplicationView.GetForCurrentView().SetDesiredBoundsMode(ApplicationViewBoundsMode.UseVisible);
            UpdateResponsiveLayout();
            SettingsStore.EnsureVersion019Defaults();
            var settings = SettingsStore.Load();
            ServerAddressBox.Text = settings.Address ?? string.Empty;
            ServerPasswordBox.Password = string.Empty;
            _messagesPerChat = settings.MessagesPerChat;
            _syncTimeframeDays = settings.SyncTimeframeDays;
            MessagesPerChatSlider.Value = _messagesPerChat;
            SelectTimeframe(_syncTimeframeDays);
            _settingsLoaded = true;
            UpdateSyncDescription();
            await NotificationService.DisableAsync();
            if (!settings.IsComplete)
            {
                SetSettingsMode(false);
                SettingsOverlay.Visibility = Visibility.Visible;
                return;
            }
            await StartClientAsync(settings.Address, settings.Password, false);
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _pollTimer.Stop();
            _qrScanTimer.Stop();
            _statusTimer.Stop();
            _qrScanner?.Dispose();
            if (_client != null) _client.Dispose();
            SystemNavigationManager.GetForCurrentView().BackRequested -= MainPage_BackRequested;
            _inputPane.Showing -= InputPane_Showing;
            _inputPane.Hiding -= InputPane_Hiding;
        }

        private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e) { UpdateResponsiveLayout(); }

        private bool UseSinglePaneLayout
        {
            get
            {
                return string.Equals(AnalyticsInfo.DeviceForm, "Phone", StringComparison.OrdinalIgnoreCase) || ActualWidth < 1000;
            }
        }

        private void UpdateResponsiveLayout()
        {
            if (!UseSinglePaneLayout)
            {
                ChatColumn.Width = new GridLength(360);
                DividerColumn.Width = new GridLength(1);
                ConversationColumn.Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(ChatsPane, 0);
                Grid.SetColumnSpan(ChatsPane, 1);
                Grid.SetColumn(ConversationPane, 2);
                Grid.SetColumnSpan(ConversationPane, 1);
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
                ConversationColumn.Width = new GridLength(0);
                Grid.SetColumn(ChatsPane, 0);
                Grid.SetColumnSpan(ChatsPane, 3);
                Grid.SetColumn(ConversationPane, 0);
                Grid.SetColumnSpan(ConversationPane, 3);
                ChatsPane.Visibility = Visibility.Visible;
                ConversationPane.Visibility = Visibility.Collapsed;
                MenuButton.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<bool> StartClientAsync(string address, string password, bool closeSettings)
        {
            _messageLoadGeneration++;
            ResetMessageItems();
            ConnectButton.IsEnabled = false;
            ConnectProgress.IsActive = true;
            ConnectError.Text = string.Empty;
            try
            {
                if (_client != null) _client.Dispose();
                _client = new BlueBubblesClient(address, password);
                _chatStateSignature = null;
                await _client.TestConnectionAsync();
                try { NavigationIdentityText.Text = await _client.GetRegisteredPhoneNumberAsync(); }
                catch { NavigationIdentityText.Text = "BlueBubbles"; }
                SettingsStore.Save(address, password);
                SettingsStore.SaveSyncOptions(_messagesPerChat, _syncTimeframeDays);
                SetSyncing(true, "Requesting chat list from BlueBubbles...");
                await LoadContactsAsync();
                await RefreshChatsAsync();
                _pollTimer.Start();
                SetSettingsMode(true);
                if (closeSettings) SettingsOverlay.Visibility = Visibility.Collapsed;
                OpenPendingActivation();
                ShowStatus(string.Empty, false);
                return true;
            }
            catch (Exception ex)
            {
                ConnectError.Text = ex.Message;
                SetSettingsMode(false);
                SettingsOverlay.Visibility = Visibility.Visible;
                ShowStatus("Could not connect", true);
                return false;
            }
            finally
            {
                SetSyncing(false, null);
                ConnectButton.IsEnabled = true;
                ConnectProgress.IsActive = false;
            }
        }

        private async Task<bool> RefreshChatsAsync()
        {
            if (_client == null) return false;
            var chats = await _client.GetChatsAsync();
            foreach (var chat in chats) chat.ApplyContactNames(_contactNames);
            var signature = BuildChatStateSignature(chats);
            if (signature == _chatStateSignature) return false;
            var selectedGuid = _selectedChat == null ? null : _selectedChat.Guid;
            _allChats.Clear();
            _allChats.AddRange(chats);
            _chatStateSignature = signature;
            var activeCount = _allChats.Count(chat => !chat.IsArchived);
            var archivedCount = _allChats.Count - activeCount;
            for (var index = 0; index < _allChats.Count; index++)
            {
                var current = _allChats[index];
                SetSyncing(true, "Indexing " + (index + 1) + " of " + _allChats.Count + " chats (" + activeCount + " active, " + archivedCount + " archived): " + current.Title);
            }
            ApplyChatSearch();
            if (selectedGuid != null) _selectedChat = _allChats.FirstOrDefault(c => c.Guid == selectedGuid) ?? _selectedChat;
            return true;
        }

        private static string BuildChatStateSignature(IEnumerable<ChatItem> chats)
        {
            return string.Join("|", chats.OrderBy(chat => chat.Guid).Select(chat => chat.Guid + ":" + chat.LastMessageGuid + ":" + chat.LastMessageTimestamp + ":" + chat.IsArchived + ":" + chat.Title));
        }

        private void ResetMessageItems()
        {
            // Tear down message visuals so no recycled text or media survives a chat change.
            MessagesList.ItemsSource = null;
            _messages.Clear();
            MessagesList.ItemsSource = _messages;
        }

        private async Task RefreshMessagesAsync(bool forceScroll)
        {
            var client = _client;
            var selectedChat = _selectedChat;
            if (client == null || selectedChat == null) return;
            var selectedGuid = selectedChat.Guid;
            // A newer refresh supersedes every older request, even for the same chat.
            var loadGeneration = ++_messageLoadGeneration;
            var received = await client.GetMessagesAsync(selectedGuid, _messagesPerChat, _syncTimeframeDays);
            if (loadGeneration != _messageLoadGeneration || _selectedChat == null || !string.Equals(_selectedChat.Guid, selectedGuid, StringComparison.OrdinalIgnoreCase)) return;
            var priorLastGuid = _messages.Count == 0 ? null : _messages[_messages.Count - 1].Guid;
            var newLastGuid = received.Count == 0 ? null : received[received.Count - 1].Guid;
            if (!forceScroll && priorLastGuid == newLastGuid && _messages.Count == received.Count) return;
            var total = received.Count;
            SetSyncing(true, "Syncing messages: 0 of " + total);
            ResetMessageItems();
            for (var i = 0; i < total; i++)
            {
                var message = received[i];
                if (message.IsImageAttachment || message.IsVideoAttachment) message.SetAttachmentUri(client.GetAttachmentDownloadUri(message.AttachmentGuid));
                _messages.Add(message);
                SetSyncing(true, "Syncing messages: " + (i + 1) + " of " + total);
            }
            if (_messages.Count > 0) MessagesList.ScrollIntoView(_messages[_messages.Count - 1]);
        }

        private async void PollTimer_Tick(object sender, object e)
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            SetSyncing(true, "Refreshing chat list from BlueBubbles...");
            try
            {
                var chatsChanged = await RefreshChatsAsync();
                if (chatsChanged) await RefreshMessagesAsync(false);
            }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
            finally { _isRefreshing = false; SetSyncing(false, null); }
        }

        private async void ChatsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            _messageLoadGeneration++;
            ResetMessageItems();
            _selectedChat = e.ClickedItem as ChatItem;
            if (_selectedChat == null) return;
            UpdateHeaderActions(true);
            PageTitle.Text = _selectedChat.Title;
            EmptyConversation.Visibility = Visibility.Collapsed;
            MessagesList.Visibility = Visibility.Visible;
            Composer.Visibility = Visibility.Visible;
            if (UseSinglePaneLayout)
            {
                ReturnToConversation();
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
                if (_sharedFile != null) await _client.SendAttachmentAsync(_selectedChat.Guid, _sharedFile);
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

        private async Task<bool> ConnectQrPayloadAsync(string payloadText)
        {
            QrSetupPayload payload;
            string error;
            if (!QrSetupPayload.TryParse(payloadText, out payload, out error))
            {
                ConnectError.Text = error;
                return false;
            }

            ServerAddressBox.Text = payload.Address;
            ServerPasswordBox.Password = payload.Password;
            return await StartClientAsync(payload.Address, payload.Password, true);
        }

        private async void ScanQr_Click(object sender, RoutedEventArgs e)
        {
            ConnectError.Text = string.Empty;
            try
            {
                _qrScanner?.Dispose();
                _qrScanner = new QrCameraScanner();
                QrCameraPreview.Visibility = Visibility.Visible;
                QrConnectProgress.IsActive = false;
                QrScannerStatus.Text = "Point the camera at the BlueBubbles setup QR code";
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
                _qrScanTimer.Stop();
                await _qrScanner.StopAsync(QrCameraPreview);
                QrCameraPreview.Visibility = Visibility.Collapsed;
                QrScannerStatus.Text = "QR code found. Connecting to BlueBubbles...";
                QrConnectProgress.IsActive = true;
                await ConnectQrPayloadAsync(payloadText);
                QrConnectProgress.IsActive = false;
                QrScannerOverlay.Visibility = Visibility.Collapsed;
            }
            catch { }
            finally { _isQrScanInProgress = false; }
        }

        private async void CancelQrScan_Click(object sender, RoutedEventArgs e) { await StopQrScannerAsync(); }

        private async Task StopQrScannerAsync()
        {
            _qrScanTimer.Stop();
            if (_qrScanner != null) await _qrScanner.StopAsync(QrCameraPreview);
            QrCameraPreview.Visibility = Visibility.Visible;
            QrConnectProgress.IsActive = false;
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
            picker.FileTypeFilter.Add(".mp4");
            picker.FileTypeFilter.Add(".m4v");
            picker.FileTypeFilter.Add(".mov");
            picker.FileTypeFilter.Add(".wmv");
            picker.FileTypeFilter.Add(".pdf");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            _sharedFile = file;
            StageSharedContentInComposer();
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
            ComposeMessageBox.Text = string.Empty;
            _composeSelectedChat = null;
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
            _messageLoadGeneration++;
            ResetMessageItems();
            _selectedChat = chat;
            UpdateHeaderActions(true);
            PageTitle.Text = chat.Title;
            EmptyConversation.Visibility = Visibility.Collapsed;
            MessagesList.Visibility = Visibility.Visible;
            Composer.Visibility = Visibility.Visible;
            if (UseSinglePaneLayout) ReturnToConversation();
            try { await RefreshMessagesAsync(true); }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
        }

        private void CloseCompose_Click(object sender, RoutedEventArgs e)
        {
            ComposeOverlay.Visibility = Visibility.Collapsed;
            if (_shareOperation != null) CompleteSharedContent();
        }

        private void RecipientMatchesList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var chat = e.ClickedItem as ChatItem;
            if (chat == null) return;
            _updatingRecipient = true;
            RecipientBox.Text = chat.Title;
            _updatingRecipient = false;
            _composeSelectedChat = chat;
            ComposeHint.Text = "Selected conversation: " + chat.Title;
            ComposeMessageBox.Focus(FocusState.Programmatic);
        }

        private void RecipientBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = RecipientBox.Text.Trim();
            if (!_updatingRecipient) _composeSelectedChat = null;
            _recipientMatches.Clear();
            foreach (var chat in _allChats.Where(chat => string.IsNullOrWhiteSpace(query) || chat.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || chat.ParticipantSummary.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || (chat.ParticipantAddresses != null && chat.ParticipantAddresses.Any(address => address.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))).Take(20)) _recipientMatches.Add(chat);
            ComposeHint.Text = string.IsNullOrWhiteSpace(query) ? "Enter a phone number or email address, then write a message." : "Select an existing conversation or send to the address you entered.";
        }

        private async void ComposeSend_Click(object sender, RoutedEventArgs e) { await SendComposedMessageAsync(); }

        private async void ComposeMessageBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter || Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down)) return;
            e.Handled = true;
            await SendComposedMessageAsync();
        }

        private async Task SendComposedMessageAsync()
        {
            if (_client == null) return;
            var message = ComposeMessageBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message) && _sharedFile == null) { ShowStatus("Write a message or choose an attachment before sending.", true); return; }
            var recipient = RecipientBox.Text.Trim();
            if (_composeSelectedChat == null && string.IsNullOrWhiteSpace(recipient)) { ShowStatus("Enter a phone number or email address.", true); return; }
            if (_composeSelectedChat == null && string.IsNullOrWhiteSpace(message)) { ShowStatus("Write a message before starting a new conversation with an attachment.", true); return; }

            try
            {
                SetSyncing(true, "Sending message...");
                ChatItem chat = _composeSelectedChat;
                if (chat == null) chat = await _client.CreateChatAsync(recipient, message);
                else if (!string.IsNullOrWhiteSpace(message)) await _client.SendTextAsync(chat.Guid, message);
                if (chat == null) throw new InvalidOperationException("BlueBubbles did not return the new conversation.");
                if (_sharedFile != null) await _client.SendAttachmentAsync(chat.Guid, _sharedFile);
                ComposeOverlay.Visibility = Visibility.Collapsed;
                if (_shareOperation != null) CompleteSharedContent();
                await RefreshChatsAsync();
                _messageLoadGeneration++;
                ResetMessageItems();
                _selectedChat = _allChats.FirstOrDefault(item => item.Guid == chat.Guid) ?? chat;
                PageTitle.Text = _selectedChat.Title;
                EmptyConversation.Visibility = Visibility.Collapsed;
                MessagesList.Visibility = Visibility.Visible;
                Composer.Visibility = Visibility.Visible;
                UpdateHeaderActions(true);
                if (UseSinglePaneLayout) ReturnToConversation();
                await RefreshMessagesAsync(true);
            }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
            finally { SetSyncing(false, null); }
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
            ContactsSearchBox.Text = string.Empty;
            ApplyContactsSearch();
            ContactsOverlay.Visibility = Visibility.Visible;
            if (_allContacts.Count == 0) ShowStatus("No phone contacts are available. Check the contacts permission in Settings.", true);
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
            if (!_settingsLoaded) return;
            _messagesPerChat = Math.Max(1, (int)Math.Round(e.NewValue));
            SettingsStore.SaveSyncOptions(_messagesPerChat, _syncTimeframeDays);
            UpdateSyncDescription();
            if (_selectedChat != null) await RefreshMessagesAsync(true);
        }

        private async void SyncTimeframeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsLoaded) return;
            var item = SyncTimeframeBox.SelectedItem as ComboBoxItem;
            if (item?.Tag != null) _syncTimeframeDays = int.Parse(item.Tag.ToString());
            SettingsStore.SaveSyncOptions(_messagesPerChat, _syncTimeframeDays);
            UpdateSyncDescription();
            if (_selectedChat != null) await RefreshMessagesAsync(true);
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
            var range = _syncTimeframeDays == 0 ? "all time" : _syncTimeframeDays == 7 ? "the last 7 days" : _syncTimeframeDays == 30 ? "the last 30 days" : "the last year";
            return "Loading the latest " + _messagesPerChat + " messages per chat from " + range + ".";
        }

        private void UpdateSyncDescription()
        {
            if (SyncDescription != null) SyncDescription.Text = BuildSyncDescription();
        }

        private void SetSyncing(bool syncing, string detail)
        {
            if (!UsePhoneSyncStatus) return;
            if (UseLegacyInAppSyncStatus && syncing)
            {
                StatusText.Text = detail ?? "Syncing...";
                StatusText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White);
                StatusBar.Visibility = Visibility.Visible;
            }
            else if (UseLegacyInAppSyncStatus && !_statusIsError)
            {
                StatusBar.Visibility = Visibility.Collapsed;
            }
            UpdatePhoneSyncStatus(syncing, detail);
            if (UseLegacyInAppSyncStatus) UpdateLegacyInAppSyncStatus(syncing, detail);
        }

        private static async Task EnsurePhoneStatusBarAsync()
        {
            try
            {
                var statusBarType = Type.GetType("Windows.UI.ViewManagement.StatusBar, Windows, ContentType=WindowsRuntime");
                var phoneStatus = statusBarType?.GetMethod("GetForCurrentView").Invoke(null, null);
                var action = phoneStatus?.GetType().GetMethod("ShowAsync").Invoke(phoneStatus, null) as Windows.Foundation.IAsyncAction;
                if (action != null) await action;
            }
            catch { }
        }

        private static void UpdatePhoneSyncStatus(bool syncing, string detail)
        {
            try
            {
                // The W10M status bar metadata is absent from the desktop SDK reference set.
                // This still invokes the native StatusBar.ProgressIndicator API on phone at runtime.
                var statusBarType = Type.GetType("Windows.UI.ViewManagement.StatusBar, Windows, ContentType=WindowsRuntime");
                if (statusBarType == null) return;
                var phoneStatus = statusBarType.GetMethod("GetForCurrentView").Invoke(null, null);
                var indicator = statusBarType.GetProperty("ProgressIndicator").GetValue(phoneStatus);
                var indicatorType = indicator.GetType();
                indicatorType.GetProperty("Text").SetValue(indicator, detail ?? string.Empty);
                indicatorType.GetProperty("ProgressValue").SetValue(indicator, null);
                indicatorType.GetMethod(syncing ? "ShowAsync" : "HideAsync").Invoke(indicator, null);
            }
            catch { }
        }

        // The old bottom in-app status implementation remains available for diagnostics.
        private static void UpdateLegacyInAppSyncStatus(bool syncing, string detail)
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
        private void Settings_Click(object sender, RoutedEventArgs e) { NavigationSplitView.IsPaneOpen = false; SetSettingsMode(true); SettingsOverlay.Visibility = Visibility.Visible; }
        private void CloseSettings_Click(object sender, RoutedEventArgs e) { if (_client != null) SettingsOverlay.Visibility = Visibility.Collapsed; }
        private void Chats_Click(object sender, RoutedEventArgs e) { ShowChatPage(false); }
        private void Archived_Click(object sender, RoutedEventArgs e) { ShowChatPage(true); }
        private void Back_Click(object sender, RoutedEventArgs e) { ReturnToChats(); }

        private void SetSettingsMode(bool connected)
        {
            SetupOnlyPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            ConnectedSettingsHeader.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            ConnectedSettingsActions.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            SettingsBackButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            SettingsHeader.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void SignOut_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new MessageDialog("Reset BlueBubbles on this device? Messages will not be deleted from the server.", "Sign out and reset?");
            var confirm = new UICommand("Sign out and reset");
            dialog.Commands.Add(confirm);
            dialog.Commands.Add(new UICommand("Cancel"));
            dialog.DefaultCommandIndex = 1;
            dialog.CancelCommandIndex = 1;
            if (await dialog.ShowAsync() != confirm) return;
            SignOutLocally();
        }

        private void SignOutLocally()
        {
            _messageLoadGeneration++;
            _pollTimer.Stop();
            _client?.Dispose();
            _client = null;
            _selectedChat = null;
            _allChats.Clear();
            _chats.Clear();
            ResetMessageItems();
            _chatStateSignature = null;
            SettingsStore.Clear();
            ServerAddressBox.Text = string.Empty;
            ServerPasswordBox.Password = string.Empty;
            SetSettingsMode(false);
            SettingsOverlay.Visibility = Visibility.Visible;
            ReturnToChats();
        }

        private void ShowChatPage(bool archived)
        {
            NavigationSplitView.IsPaneOpen = false;
            _messageLoadGeneration++;
            ResetMessageItems();
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
            var pin = new UICommand("Pin to Start");
            var delete = new UICommand("Delete chat");
            var menu = new PopupMenu();
            menu.Commands.Add(pin);
            menu.Commands.Add(delete);
            var point = ChatActionsButton.TransformToVisual(null).TransformPoint(new Point());
            var selected = await menu.ShowForSelectionAsync(new Rect(point, ChatActionsButton.RenderSize));
            if (selected == pin)
            {
                await PinSelectedChatAsync();
            }
            else if (selected == delete)
            {
                await DeleteSelectedChatAsync();
            }
        }

        private async Task PinSelectedChatAsync()
        {
            if (_selectedChat == null) return;
            var tileId = "chat-" + Convert.ToBase64String(Encoding.UTF8.GetBytes(_selectedChat.Guid)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (SecondaryTile.Exists(tileId)) { ShowStatus("This conversation is already pinned to Start.", false); return; }
            var tile = new SecondaryTile(tileId, _selectedChat.Title, "chat=" + Uri.EscapeDataString(_selectedChat.Guid), new Uri("ms-appx:///Assets/Square150x150Logo.png"), TileSize.Square150x150);
            tile.VisualElements.ShowNameOnSquare150x150Logo = true;
            await tile.RequestCreateAsync();
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
            // Fit the entire app between its current top edge and the keyboard's top edge.
            // This is absolute, so it cannot double-apply an inset if Windows already resized us.
            args.EnsuredFocusedElementInView = true;
            RootGrid.Margin = new Thickness(0);
            var rootTop = RootGrid.TransformToVisual(null).TransformPoint(new Point(0, 0)).Y;
            var availableHeight = args.OccludedRect.Top - rootTop;
            if (availableHeight > 0)
            {
                RootGrid.VerticalAlignment = VerticalAlignment.Top;
                RootGrid.Height = availableHeight;
            }
            PrimaryHeader.Visibility = Visibility.Visible;
            if (_messages.Count > 0) MessagesList.ScrollIntoView(_messages[_messages.Count - 1]);
        }

        private void InputPane_Hiding(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            RootGrid.Margin = new Thickness(0);
            RootGrid.Height = double.NaN;
            RootGrid.VerticalAlignment = VerticalAlignment.Stretch;
        }
        private void MainPage_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (QrScannerOverlay.Visibility == Visibility.Visible) { CancelQrScan_Click(this, null); e.Handled = true; }
            else if (ComposeOverlay.Visibility == Visibility.Visible) { CloseCompose_Click(this, null); e.Handled = true; }
            else if (ContactsOverlay.Visibility == Visibility.Visible) { CloseContacts_Click(this, null); e.Handled = true; }
            else if (SettingsOverlay.Visibility == Visibility.Visible && _client != null) { SettingsOverlay.Visibility = Visibility.Collapsed; e.Handled = true; }
            else if (UseSinglePaneLayout && ChatsPane.Visibility == Visibility.Collapsed) { ReturnToChats(); e.Handled = true; }
        }

        private void ReturnToChats()
        {
            if (UseSinglePaneLayout)
            {
                ChatColumn.Width = new GridLength(1, GridUnitType.Star);
                DividerColumn.Width = new GridLength(0);
                ConversationColumn.Width = new GridLength(0);
                Grid.SetColumn(ChatsPane, 0);
                Grid.SetColumnSpan(ChatsPane, 3);
                Grid.SetColumn(ConversationPane, 0);
                Grid.SetColumnSpan(ConversationPane, 3);
                ChatsPane.Visibility = Visibility.Visible;
                ConversationPane.Visibility = Visibility.Collapsed;
                MenuButton.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
                PageTitle.Text = _showArchived ? "Archived" : "Chats";
                SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
            }
            UpdateHeaderActions(false);
        }

        private void ReturnToConversation()
        {
            ChatColumn.Width = new GridLength(1, GridUnitType.Star);
            DividerColumn.Width = new GridLength(0);
            ConversationColumn.Width = new GridLength(0);
            Grid.SetColumn(ChatsPane, 0);
            Grid.SetColumnSpan(ChatsPane, 3);
            Grid.SetColumn(ConversationPane, 0);
            Grid.SetColumnSpan(ConversationPane, 3);
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
            AttachmentBannerHost.Visibility = Visibility.Visible;
        }

        private void ClearAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (_shareOperation != null) CompleteSharedContent();
            else ClearSharedContent();
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
            SharedAttachmentBanner.Text = string.Empty;
            AttachmentBannerHost.Visibility = Visibility.Collapsed;
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
            _statusTimer.Stop();
            _statusIsError = isError && !string.IsNullOrWhiteSpace(message);
            StatusText.Text = message;
            StatusText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(isError ? Windows.UI.Color.FromArgb(255, 255, 140, 130) : Windows.UI.Colors.White);
            StatusBar.Visibility = isError && !string.IsNullOrWhiteSpace(message) ? Visibility.Visible : Visibility.Collapsed;
            if (_statusIsError) _statusTimer.Start();
        }

        private void StatusTimer_Tick(object sender, object e)
        {
            _statusTimer.Stop();
            ShowStatus(string.Empty, false);
        }

        private void MessageText_Loaded(object sender, RoutedEventArgs e)
        {
            var block = sender as RichTextBlock;
            RenderMessageText(block, block?.DataContext as MessageItem);
        }

        private void MessageText_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            RenderMessageText(sender as RichTextBlock, args.NewValue as MessageItem);
        }

        private static void RenderMessageText(RichTextBlock block, MessageItem message)
        {
            if (block == null) return;
            block.Blocks.Clear();
            if (message == null) return;
            var paragraph = new Paragraph();
            var index = 0;
            foreach (Match match in Regex.Matches(message.Text ?? string.Empty, @"https?://[^\s]+", RegexOptions.IgnoreCase))
            {
                if (match.Index > index) paragraph.Inlines.Add(new Run { Text = message.Text.Substring(index, match.Index - index) });
                Uri uri;
                if (Uri.TryCreate(match.Value, UriKind.Absolute, out uri))
                {
                    var link = new Hyperlink { NavigateUri = uri };
                    link.Inlines.Add(new Run { Text = match.Value });
                    paragraph.Inlines.Add(link);
                }
                else paragraph.Inlines.Add(new Run { Text = match.Value });
                index = match.Index + match.Length;
            }
            if (index < (message.Text ?? string.Empty).Length) paragraph.Inlines.Add(new Run { Text = message.Text.Substring(index) });
            block.Blocks.Add(paragraph);
        }
    }
}
