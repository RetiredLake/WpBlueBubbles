using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Contacts;

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
                foreach (var phone in contact.Phones.Where(phone => !string.IsNullOrWhiteSpace(phone.Number)))
                    results.Add(new ContactChoice { DisplayName = name, Address = phone.Number, Kind = "Mobile" });
                foreach (var email in contact.Emails.Where(email => !string.IsNullOrWhiteSpace(email.Address)))
                    results.Add(new ContactChoice { DisplayName = name, Address = email.Address, Kind = "Email" });
            }
            return results.OrderBy(contact => contact.DisplayName).ThenBy(contact => contact.Address).ToList();
        }

        public async Task<IReadOnlyDictionary<string, string>> LoadNamesAsync()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var contacts = await LoadContactsAsync();
            foreach (var contact in contacts) AddName(names, contact.Address, contact.DisplayName);
            return names;
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
        public string Title { get { return DisplayName + "  " + Address; } }
    }
}
