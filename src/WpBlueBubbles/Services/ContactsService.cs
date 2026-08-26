using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Contacts;

namespace WpBlueBubbles.Services
{
    public sealed class ContactsService
    {
        public async Task<IReadOnlyDictionary<string, string>> LoadNamesAsync()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var store = await ContactManager.RequestStoreAsync(ContactStoreAccessType.AllContactsReadOnly);
            if (store == null) return names;

            var contacts = await store.FindContactsAsync();
            foreach (var contact in contacts)
            {
                var name = contact.DisplayName;
                if (string.IsNullOrWhiteSpace(name)) continue;
                foreach (var phone in contact.Phones) AddName(names, phone.Number, name);
                foreach (var email in contact.Emails) AddName(names, email.Address, name);
            }
            return names;
        }

        private static void AddName(IDictionary<string, string> names, string address, string name)
        {
            if (string.IsNullOrWhiteSpace(address) || names.ContainsKey(address)) return;
            names[address] = name;
            var normalized = NormalizePhone(address);
            if (!string.IsNullOrWhiteSpace(normalized)) names[normalized] = name;
        }

        public static string Lookup(IReadOnlyDictionary<string, string> names, string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return string.Empty;
            string name;
            return names.TryGetValue(address, out name) || names.TryGetValue(NormalizePhone(address), out name) ? name : string.Empty;
        }

        private static string NormalizePhone(string value)
        {
            return new string(value.Where(char.IsDigit).ToArray());
        }
    }
}
