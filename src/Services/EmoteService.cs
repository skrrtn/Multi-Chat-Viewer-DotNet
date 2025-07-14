using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MultiChatViewer.Services
{
    public class EmoteService
    {
        private readonly ILogger<EmoteService> _logger;
        private readonly string _emotesFilePath = Path.Combine(Directory.GetCurrentDirectory(), "emotes.json");
        private Dictionary<string, Emote> _emotes = new Dictionary<string, Emote>(StringComparer.OrdinalIgnoreCase);

        public EmoteService(ILogger<EmoteService> logger)
        {
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            if (!File.Exists(_emotesFilePath))
            {
                _logger.LogInformation("emotes.json not found, creating a default one.");
                await CreateDefaultEmotesFile();
            }

            await LoadEmotesAsync();
        }

        private async Task CreateDefaultEmotesFile()
        {
            var defaultEmotes = new List<Emote>
            {
                new Emote { Name = "AYAYA", Platform = EmotePlatform.Seventv, Id = "60aee158f3c442a44c116998" },
                new Emote { Name = "catJAM", Platform = EmotePlatform.BTTV, Id = "5e0b2f536d4852463344ab2b" },
                new Emote { Name = "PepeHands", Platform = EmotePlatform.FFZ, Id = "231552" }
            };

            foreach (var emote in defaultEmotes)
            {
                emote.GenerateUrl();
            }

            var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            var json = JsonSerializer.Serialize(defaultEmotes, options);
            await File.WriteAllTextAsync(_emotesFilePath, json);
        }

        private async Task LoadEmotesAsync()
        {
            try
            {
                var json = await File.ReadAllTextAsync(_emotesFilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
                var emotesList = JsonSerializer.Deserialize<List<Emote>>(json, options);

                if (emotesList != null)
                {
                    foreach (var emote in emotesList)
                    {
                        emote.GenerateUrl();
                    }
                    _emotes = emotesList.ToDictionary(e => e.Name, e => e, StringComparer.OrdinalIgnoreCase);
                    _logger.LogInformation("✅ Successfully loaded {Count} emotes from emotes.json", _emotes.Count);
                }
                else
                {
                    _logger.LogWarning("❌ Failed to deserialize emotes.json - result was null");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error loading emotes from emotes.json");
            }
        }

        public Emote GetEmote(string name)
        {
            _emotes.TryGetValue(name, out var emote);
            return emote;
        }

        public bool IsEmote(string word)
        {
            return _emotes.ContainsKey(word);
        }
    }
}
