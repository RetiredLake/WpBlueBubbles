using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Contacts;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace WpBlueBubbles.Services
{
    public sealed class ContactsService
    {
        public async Task<IReadOnlyList<ContactChoice>> LoadContactsAsync()
        {
            var results = new List<ContactChoice>();
            var store = await ContactManager.RequestStoreAsync(ContactStoreAccessType.AllContactsReadOnly);
            if (store == null) return results;
            var contacts = await store.FindContactsAsync();
            foreach (var contact in contacts)
            {
                var name = string.IsNullOrWhiteSpace(contact.DisplayName) ? "Contact" : contact.DisplayName;
                var avatar = await LoadAvatarAsync(contact);
                foreach (var phone in contact.Phones.Where(phone => !string.IsNullOrWhiteSpace(phone.Number)))
                    results.Add(new ContactChoice { DisplayName = name, Address = phone.Number, Kind = "Mobile", AvatarSource = avatar });
                foreach (var email in contact.Emails.Where(email => !string.IsNullOrWhiteSpace(email.Address)))
                    results.Add(new ContactChoice { DisplayName = name, Address = email.Address, Kind = "Email", AvatarSource = avatar });
            }
            return results.OrderBy(contact => contact.DisplayName).ThenBy(contact => contact.Address).ToList();
        }

        private static async Task<ImageSource> LoadAvatarAsync(Contact contact)
        {
            if (contact.Thumbnail == null) return null;
            try
            {
                using (var stream = await contact.Thumbnail.OpenReadAsync())
                {
                    var image = new BitmapImage();
                    await image.SetSourceAsync(stream);
                    return image;
                }
            }
            catch
            {
                return null;
            }
        }

        public async Task<IReadOnlyDictionary<string, string>> LoadNamesAsync()
        {
            var contacts = await LoadContactsAsync();
            return BuildNames(contacts);
        }

        public static IReadOnlyDictionary<string, string> BuildNames(IEnumerable<ContactChoice> contacts)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var contact in contacts) AddName(names, contact.Address, contact.DisplayName);
            return names;
        }

        public static IReadOnlyDictionary<string, ImageSource> BuildImages(IEnumerable<ContactChoice> contacts)
        {
            var images = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var contact in contacts)
            {
                if (contact.AvatarSource == null || string.IsNullOrWhiteSpace(contact.Address)) continue;
                if (!images.ContainsKey(contact.Address)) images[contact.Address] = contact.AvatarSource;
                var normalized = NormalizeAddress(contact.Address);
                if (!string.IsNullOrWhiteSpace(normalized) && !images.ContainsKey(normalized)) images[normalized] = contact.AvatarSource;
            }
            return images;
        }

        private static void AddName(IDictionary<string, string> names, string address, string name)
        {
            if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(name)) return;
            if (!names.ContainsKey(address)) names[address] = name;
            var normalized = NormalizeAddress(address);
            if (!string.IsNullOrWhiteSpace(normalized) && !names.ContainsKey(normalized)) names[normalized] = name;
        }

        public static string Lookup(IReadOnlyDictionary<string, string> names, string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return string.Empty;
            string name;
            return names.TryGetValue(address, out name) || names.TryGetValue(NormalizeAddress(address), out name) ? name : string.Empty;
        }

        public static ImageSource LookupImage(IReadOnlyDictionary<string, ImageSource> images, string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            ImageSource image;
            return images.TryGetValue(address, out image) || images.TryGetValue(NormalizeAddress(address), out image) ? image : null;
        }

        private static string NormalizeAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            if (value.IndexOf('@') >= 0) return value.Trim().ToLowerInvariant();
            var digits = new string(value.Where(char.IsDigit).ToArray());
            // BlueBubbles normally reports E.164 while Lumia contacts commonly omit +1.
            return digits.Length == 11 && digits.StartsWith("1") ? digits.Substring(1) : digits.Length > 10 ? digits.Substring(digits.Length - 10) : digits;
        }
    }

    public sealed class ContactChoice
    {
        public string DisplayName { get; set; }
        public string Address { get; set; }
        public string Kind { get; set; }
        public ImageSource AvatarSource { get; set; }
        public bool HasAvatar { get { return AvatarSource != null; } }
        public bool ShowInitials { get { return AvatarSource == null; } }
        public string Initials
        {
            get
            {
                var pieces = (DisplayName ?? string.Empty).Split(new[] { ' ', ',', '+', '@' }, StringSplitOptions.RemoveEmptyEntries);
                if (pieces.Length == 0) return "?";
                return pieces[0].Substring(0, 1).ToUpperInvariant() + (pieces.Length > 1 ? pieces[1].Substring(0, 1).ToUpperInvariant() : string.Empty);
            }
        }
        public string Title { get { return DisplayName + "  " + Address; } }
    }
}
