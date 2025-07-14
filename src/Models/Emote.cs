using System.Text.Json.Serialization;

namespace MultiChatViewer
{
    public enum EmotePlatform
    {
        BTTV,
        FFZ,
        [JsonPropertyName("7TV")]
        Seventv,
        Kick
    }

    public class Emote
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public EmotePlatform Platform { get; set; }
        public string Url { get; private set; }

        public void GenerateUrl()
        {
            Url = Platform switch
            {
                EmotePlatform.BTTV => $"https://cdn.betterttv.net/emote/{Id}/1x",
                EmotePlatform.FFZ => $"https://cdn.frankerfacez.com/emote/{Id}/1",
                EmotePlatform.Seventv => $"https://cdn.7tv.app/emote/{Id}/1x.webp",
                EmotePlatform.Kick => $"https://files.kick.com/emotes/{Id}/fullsize",
                _ => string.Empty,
            };
        }
    }
}
