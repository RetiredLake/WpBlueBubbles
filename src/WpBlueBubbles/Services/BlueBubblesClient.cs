using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using WpBlueBubbles.Models;

namespace WpBlueBubbles.Services
{
    public sealed class BlueBubblesClient : IDisposable
    {
        private readonly HttpClient _http = new HttpClient();
        private readonly string _apiRoot;
        private readonly string _password;

        public BlueBubblesClient(string address, string password)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Server address is required.");
            var root = address.Trim().TrimEnd('/');
            if (!root.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !root.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                root = "http://" + root;
            _apiRoot = root.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase) ? root : root + "/api/v1";
            _password = password == null ? string.Empty : password.Trim();
            _http.Timeout = TimeSpan.FromSeconds(20);
        }

        public async Task TestConnectionAsync()
        {
            await GetRootAsync("ping");
        }

        public async Task<IReadOnlyList<ChatItem>> GetChatsAsync()
        {
            var body = new JsonObject
            {
                ["with"] = new JsonArray { JsonValue.CreateStringValue("participants"), JsonValue.CreateStringValue("lastmessage") },
                ["offset"] = JsonValue.CreateNumberValue(0),
                ["limit"] = JsonValue.CreateNumberValue(100),
                ["sort"] = JsonValue.CreateStringValue("lastmessage")
            };
            var root = await PostRootAsync("chat/query", body);
            var data = GetDataArray(root);
            var result = new List<ChatItem>();
            foreach (var value in data) if (value.ValueType == JsonValueType.Object) result.Add(ChatItem.FromJson(value.GetObject()));
            return result;
        }

        public async Task<IReadOnlyList<MessageItem>> GetMessagesAsync(string chatGuid, int limit)
        {
            var route = "chat/" + Uri.EscapeDataString(chatGuid) + "/message?sort=DESC&offset=0&limit=" + limit;
            var root = await GetRootAsync(route);
            var data = GetDataArray(root);
            var result = new List<MessageItem>();
            foreach (var value in data) if (value.ValueType == JsonValueType.Object) result.Add(MessageItem.FromJson(value.GetObject()));
            result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return result;
        }

        public async Task<MessageItem> SendTextAsync(string chatGuid, string text)
        {
            var body = new JsonObject
            {
                ["chatGuid"] = JsonValue.CreateStringValue(chatGuid),
                ["tempGuid"] = JsonValue.CreateStringValue("temp-" + Guid.NewGuid().ToString()),
                ["message"] = JsonValue.CreateStringValue(text)
            };
            var root = await PostRootAsync("message/text", body);
            JsonObject data;
            return JsonValueReader.TryObject(root, "data", out data) ? MessageItem.FromJson(data) : null;
        }

        private string BuildUrl(string route)
        {
            var separator = route.Contains("?") ? "&guid=" : "?guid=";
            return _apiRoot + "/" + route + separator + Uri.EscapeDataString(_password);
        }

        private async Task<JsonObject> GetRootAsync(string route)
        {
            var response = await _http.GetAsync(BuildUrl(route));
            return await ReadResponseAsync(response);
        }

        private async Task<JsonObject> PostRootAsync(string route, JsonObject body)
        {
            var content = new StringContent(body.Stringify(), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(BuildUrl(route), content);
            return await ReadResponseAsync(response);
        }

        private static async Task<JsonObject> ReadResponseAsync(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            JsonObject root;
            if (!JsonObject.TryParse(text, out root)) throw new InvalidOperationException("The server returned an unreadable response (HTTP " + (int)response.StatusCode + ").");
            if (!response.IsSuccessStatusCode)
            {
                var error = JsonValueReader.String(root, "error");
                if (string.IsNullOrWhiteSpace(error)) error = JsonValueReader.String(root, "message");
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Server request failed (HTTP " + (int)response.StatusCode + ")." : error);
            }
            return root;
        }

        private static JsonArray GetDataArray(JsonObject root)
        {
            JsonArray result;
            return JsonValueReader.TryArray(root, "data", out result) ? result : new JsonArray();
        }

        public void Dispose() { _http.Dispose(); }
    }
}
