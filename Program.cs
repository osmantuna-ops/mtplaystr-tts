using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.Storage;
using System.Security;

class Program
{
    private static readonly string ApiUrl = "https://ses.metasoft.com.tr/api/tts/speak";
    private static readonly string ApiKey = "METASOFT_2026_SECRET";

    private static readonly HttpClient client = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    static async Task Main(string[] args)
    {
        try
        {
            if (args.Length < 2)
                return;

            string firma = args[0];
            string metin = args[1];
            string? wavDosya = args.Length >= 3 ? args[2] : null;

            metin = CleanPrefix(metin);
            metin = FixEncoding(metin);

            // 🔔 Ding
            if (!string.IsNullOrEmpty(wavDosya) && File.Exists(wavDosya))
            {
                if (IsWin10OrGreater())
                    await SafePlayWavAsync(wavDosya);
            }

            // ==============================
            // 🔥 ÖNCELİK 1 → TÜRKÇE LOCAL
            // ==============================
            if (IsWin10OrGreater() && HasTurkishVoiceSafe())
            {
                bool localOk = await LocalSpeakSafe(metin);
                if (localOk)
                    return;
            }

            // ==============================
            // 🔥 ÖNCELİK 2 → API
            // ==============================
            string apiMetin = PrepareApiText(metin);
            bool apiSuccess = await TryApiSpeak(apiMetin);
            if (apiSuccess)
                return;

            // ==============================
            // 🔥 ÖNCELİK 3 → SESSİZ
            // ==============================
        }
        catch (Exception ex)
        {
            File.WriteAllText("tts_error.log", ex.ToString());
        }
    }

    // ==============================
    // OS KONTROL
    // ==============================
    static bool IsWin10OrGreater()
    {
        return Environment.OSVersion.Version.Major >= 10;
    }

    // ==============================
    // TÜRKÇE SES VAR MI (SAFE)
    // ==============================
    static bool HasTurkishVoiceSafe()
    {
        try
        {
            return SpeechSynthesizer.AllVoices
                .Any(v => v.Language.StartsWith("tr", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    // ==============================
    // LOCAL TTS (SAFE)
    // ==============================
    static async Task<bool> LocalSpeakSafe(string metin)
    {
        try
        {
            var synth = new SpeechSynthesizer();

            var turkishVoice = SpeechSynthesizer.AllVoices
                .FirstOrDefault(v => v.Language.StartsWith("tr", StringComparison.OrdinalIgnoreCase));

            if (turkishVoice == null)
                return false;

            synth.Voice = turkishVoice;
            synth.Options.SpeakingRate = 0.95;
            synth.Options.AudioVolume = 1.0;

            string ssml = BuildSsml(metin);

            var stream = await synth.SynthesizeSsmlToStreamAsync(ssml);
            await PlayStreamAsync(stream);

            return true;
        }
        catch
        {
            return false;
        }
    }

    // ==============================
    // API
    // ==============================
    static async Task<bool> TryApiSpeak(string text)
    {
        try
        {
            client.DefaultRequestHeaders.Remove("x-api-key");
            client.DefaultRequestHeaders.Add("x-api-key", ApiKey);

            var json = JsonSerializer.Serialize(new
            {
                text = text,
                ding = false
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            var response = await client.PostAsync(ApiUrl, content, cts.Token);

            if (!response.IsSuccessStatusCode)
                return false;

            var bytes = await response.Content.ReadAsByteArrayAsync();

            string tempPath = Path.Combine(Path.GetTempPath(), "tts_temp.wav");
            File.WriteAllBytes(tempPath, bytes);

            if (IsWin10OrGreater())
                await SafePlayWavAsync(tempPath);

            return true;
        }
        catch
        {
            return false;
        }
    }

    // ==============================
    // WAV ÇAL (SAFE)
    // ==============================
    static async Task SafePlayWavAsync(string path)
    {
        try
        {
            var tcs = new TaskCompletionSource<bool>();

            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            var stream = await file.OpenAsync(FileAccessMode.Read);

            using var player = new MediaPlayer();
            player.Volume = 1.0;
            player.Source = MediaSource.CreateFromStream(stream, file.ContentType);

            player.MediaEnded += (s, e) =>
            {
                tcs.TrySetResult(true);
            };

            player.MediaFailed += (s, e) =>
            {
                tcs.TrySetResult(true);
            };

            player.Play();

            await tcs.Task;
        }
        catch
        {
        }
    }

    static async Task PlayStreamAsync(SpeechSynthesisStream stream)
    {
        var tcs = new TaskCompletionSource<bool>();

        using var player = new MediaPlayer();
        player.Volume = 1.0;
        player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);

        player.MediaEnded += (s, e) =>
        {
            tcs.TrySetResult(true);
        };

        player.MediaFailed += (s, e) =>
        {
            tcs.TrySetResult(true);
        };

        player.Play();

        await tcs.Task;
    }

    // ==============================
    // CLEAN PREFIX (AYNEN)
    // ==============================
    static string CleanPrefix(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        input = input.Trim();
        bool hasPipe = input.Contains("|");

        if (hasPipe)
        {
            input = input.Split('|').Last().Trim();

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
        else
        {
            var commaIndex = input.IndexOf(',');

            if (commaIndex > 0)
            {
                var beforeComma = input.Substring(0, commaIndex).Trim();
                var rest = input.Substring(commaIndex + 1).Trim();

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
    static string PrepareApiText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        string lower = text.ToLower();

        if (!lower.Contains("lütfen içeriye giriniz"))
        {
            text = text.Trim().TrimEnd(',');
            text += ", lütfen içeriye giriniz";
        }

        return text;
    }
}
