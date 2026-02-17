using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.Storage;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            if (args.Length < 2)
                return;

            string firma = args[0];
            string metin = FixEncoding(args[1]);
            string? wavDosya = args.Length >= 3 ? args[2] : null;

            // 🔔 Dingdong çal
            if (!string.IsNullOrEmpty(wavDosya) && File.Exists(wavDosya))
            {
                await PlayWavAsync(wavDosya);
            }

            var synth = new SpeechSynthesizer();

            var turkishVoice = SpeechSynthesizer.AllVoices
                .FirstOrDefault(v => v.Language.StartsWith("tr"));

            if (turkishVoice != null)
                synth.Voice = turkishVoice;

            synth.Options.SpeakingRate = 1.0;
            synth.Options.AudioVolume = 1.0; // MAX

            string ssml = BuildSsml(metin);

            try
            {
                var stream = await synth.SynthesizeSsmlToStreamAsync(ssml);
                await PlayStreamAsync(stream);
            }
            catch
            {
                var stream = await synth.SynthesizeTextToStreamAsync(metin);
                await PlayStreamAsync(stream);
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText("tts_error.log", ex.ToString());
        }
    }

    static async Task PlayWavAsync(string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        var stream = await file.OpenAsync(FileAccessMode.Read);

        using var player = new MediaPlayer();
        player.Volume = 1.0; // MAX
        player.Source = MediaSource.CreateFromStream(stream, file.ContentType);
        player.Play();

        await Task.Delay(1200);
    }

    static async Task PlayStreamAsync(SpeechSynthesisStream stream)
    {
        using var player = new MediaPlayer();
        player.Volume = 1.0; // MAX
        player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
        player.Play();

        await Task.Delay((int)(stream.Size / 25)); // biraz daha uzun bekleme
    }

    static string BuildSsml(string text)
    {
        text = SecurityElement.Escape(text);

        text = text.Replace(",", ",<break time='200ms'/>");
        text = text.Replace(".", ".<break time='300ms'/>");

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
        {
            text = text.Replace(wrong, correct);
        }

        return text;
    }
}
