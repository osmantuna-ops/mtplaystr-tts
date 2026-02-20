using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.Storage;
using System.Security;

class Program
{
    private static readonly string ApiUrl = "https://ses.metasoft.com.tr/api/tts/speak";
    private static readonly string ApiKey = "xxxxxxxx";

    static async Task Main(string[] args)
    {
        try
        {
            if (args.Length < 2)
                return;

            string firma = args[0];
            string metin = args[1];
            string? wavDosya = args.Length >= 3 ? args[2] : null;

            // 🔥 PREFIX TEMİZLEME + İSİM TEKRAR
            metin = CleanPrefix(metin);

            // 🔥 Encoding düzeltme
            metin = FixEncoding(metin);

            // 🔔 Ding çal (SADECE LOCAL)
            if (!string.IsNullOrEmpty(wavDosya) && File.Exists(wavDosya))
            {
                await PlayWavAsync(wavDosya);
            }

            // 🌐 Web API dene
            bool apiSuccess = await TryApiSpeak(metin);

            // ❌ API başarısızsa LOCAL konuş
            if (!apiSuccess)
            {
                await LocalSpeak(metin);
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText("tts_error.log", ex.ToString());
        }
    }

    // ==============================
    // API ÇAĞRISI
    // ==============================
    static async Task<bool> TryApiSpeak(string text)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            client.DefaultRequestHeaders.Add("x-api-key", ApiKey);

            var json = JsonSerializer.Serialize(new
            {
                text = text,
                ding = false
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(ApiUrl, content);

            if (!response.IsSuccessStatusCode)
                return false;

            var bytes = await response.Content.ReadAsByteArrayAsync();

            string tempPath = Path.Combine(Path.GetTempPath(), "tts_temp.wav");
            File.WriteAllBytes(tempPath, bytes);

            await PlayWavAsync(tempPath);

            return true;
        }
        catch
        {
            return false;
        }
    }

    // ==============================
    // LOCAL TTS FALLBACK (SSML)
    // ==============================
    static async Task LocalSpeak(string metin)
    {
        var synth = new SpeechSynthesizer();
        var turkishVoice = SpeechSynthesizer.AllVoices
            .FirstOrDefault(v => v.Language.StartsWith("tr"));

        if (turkishVoice != null)
            synth.Voice = turkishVoice;

        synth.Options.SpeakingRate = 0.95;
        synth.Options.AudioVolume = 1.0;

        string ssml = BuildSsml(metin);

        var stream = await synth.SynthesizeSsmlToStreamAsync(ssml);
        await PlayStreamAsync(stream);
    }

    // ==============================
    // WAV ÇAL
    // ==============================
    static async Task PlayWavAsync(string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        var stream = await file.OpenAsync(FileAccessMode.Read);

        using var player = new MediaPlayer();
        player.Volume = 1.0;
        player.Source = MediaSource.CreateFromStream(stream, file.ContentType);
        player.Play();

        await Task.Delay(1500);
    }

    static async Task PlayStreamAsync(SpeechSynthesisStream stream)
    {
        using var player = new MediaPlayer();
        player.Volume = 1.0;
        player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
        player.Play();

        await Task.Delay((int)(stream.Size / 25));
    }

    // ==============================
    // PREFIX TEMİZLEME + İSİM TEKRAR + DURAKLAMA
    // ==============================
    static string CleanPrefix(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        input = input.Trim();

        bool hasPipe = input.Contains("|");

        // =========================================
        // 1️⃣ PIPE VARSA → PREFIX SİL + FORMATLA
        // =========================================
        if (hasPipe)
        {
            // | öncesini sil
            input = input.Split('|').Last().Trim();

            // başta sayın varsa kaldır
            if (input.ToLower().StartsWith("sayın "))
                input = input.Substring(6).Trim();

            var commaIndex = input.IndexOf(',');

            if (commaIndex > 0)
            {
                var namePart = input.Substring(0, commaIndex).Trim();
                var rest = input.Substring(commaIndex + 1).Trim();

                return $"Sayın {namePart}, {namePart} {rest}";
            }
            else
            {
                return $"Sayın {input}, {input}";
            }
        }

        // =========================================
        // 2️⃣ PIPE YOKSA → SADECE İSMİ TEKRAR ET
        // =========================================
        else
        {
            var commaIndex = input.IndexOf(',');

            if (commaIndex > 0)
            {
                var beforeComma = input.Substring(0, commaIndex).Trim();
                var rest = input.Substring(commaIndex + 1).Trim();

                // "sayın süleyla aktürk"
                string nameOnly = beforeComma;

                if (beforeComma.ToLower().StartsWith("sayın "))
                    nameOnly = beforeComma.Substring(6).Trim();

                return $"{beforeComma}, {nameOnly} {rest}";
            }
            else
            {
                return input;
            }
        }
    }

    static string InsertBreaks(string text, string name)
    {
        // ismi iki kere söyle: "sayın süleyla aktürk <break/> süleyla aktürk"
        if (text.Contains(name))
        {
            text = text.Replace(name + " " + name, $"{name} <break time='400ms'/> {name}");
        }
        return text;
    }

    // ==============================
    // SSML OLUŞTUR
    // ==============================
    static string BuildSsml(string text)
    {
        text = SecurityElement.Escape(text);

        return $@"
<speak version='1.0' xml:lang='tr-TR'>
  <voice xml:lang='tr-TR'>
    <prosody rate='0.9' volume='x-loud'>
      {text}
    </prosody>
  </voice>
</speak>";
    }

    // ==============================
    // TÜRKÇE KARAKTER DÜZELTME
    // ==============================
    static string FixEncoding(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var replacements = new (string wrong, string correct)[]
        {
            ("Ä±", "ı"),
            ("Ä°", "İ"),
            ("Ã¼", "ü"),
            ("Ãœ", "Ü"),
            ("Ã§", "ç"),
            ("Ã‡", "Ç"),
            ("Ã¶", "ö"),
            ("Ã–", "Ö"),
            ("ÅŸ", "ş"),
            ("Åž", "Ş"),
            ("ÄŸ", "ğ"),
            ("Äž", "Ğ")
        };

        foreach (var (wrong, correct) in replacements)
            text = text.Replace(wrong, correct);

        return text;
    }
}
