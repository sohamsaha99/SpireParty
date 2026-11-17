// M0 frame-read spike (throwaway). Armed only with --frameread.
// Timeline from arming: 0-20 s settle (window set to 1280x720 windowed at 5 s),
// 20-30 s baseline (frame times only), 30-60 s capture every frame
// (viewport GetImage + SaveJpgToBuffer q=0.70), then a report in the game log.
using System.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

[ModInitializer(nameof(Init))]
public static class FrameReadSpike
{
    const string Tag = "[FrameReadSpike]";
    static readonly Stopwatch Clock = new();
    static double _lastFrameMs = -1;
    static bool _resized, _sampleSaved;

    static readonly List<double> BaselineFrameMs = new(), CaptureFrameMs = new();
    static readonly List<double> ReadMs = new(), EncodeMs = new();
    static readonly List<int> JpegBytes = new();
    static string _imageSize = "?";

    public static void Init()
    {
        if (!CommandLineHelper.HasArg("frameread")) return;
        Log.Info($"{Tag} armed");
        Clock.Start();
        RenderingServer.FramePostDraw += OnFramePostDraw;
    }

    static void OnFramePostDraw()
    {
        try
        {
            double now = Clock.Elapsed.TotalMilliseconds;
            double frame = _lastFrameMs < 0 ? 0 : now - _lastFrameMs;
            _lastFrameMs = now;
            double t = now / 1000.0;

            if (!_resized && t >= 5)
            {
                _resized = true;
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                Log.Info($"{Tag} window set to 1280x720 windowed");
            }
            if (t < 20) return;
            if (t < 30) { BaselineFrameMs.Add(frame); return; }
            if (t < 60) { Capture(frame); return; }

            RenderingServer.FramePostDraw -= OnFramePostDraw;
            Report();
        }
        catch (Exception e)
        {
            RenderingServer.FramePostDraw -= OnFramePostDraw;
            Log.Error($"{Tag} failed: {e}");
        }
    }

    static void Capture(double frame)
    {
        CaptureFrameMs.Add(frame);
        var tree = (SceneTree)Engine.GetMainLoop();

        long t0 = Stopwatch.GetTimestamp();
        using Image image = tree.Root.GetTexture().GetImage();
        long t1 = Stopwatch.GetTimestamp();
        byte[] jpeg = image.SaveJpgToBuffer(0.70f);
        long t2 = Stopwatch.GetTimestamp();

        ReadMs.Add(Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds);
        EncodeMs.Add(Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds);
        JpegBytes.Add(jpeg.Length);
        _imageSize = $"{image.GetWidth()}x{image.GetHeight()} {image.GetFormat()}";

        if (!_sampleSaved)
        {
            _sampleSaved = true;
            string dir = Path.Combine(OS.GetUserDataDir(), "mods", "FrameReadSpike");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "sample.jpg"), jpeg);
        }
    }

    static string Stats(List<double> v)
    {
        if (v.Count == 0) return "n/a";
        var s = v.OrderBy(x => x).ToList();
        double P(double q) => s[(int)Math.Min(s.Count - 1, Math.Round(q * (s.Count - 1)))];
        return $"n={s.Count} mean={s.Average():F2} p50={P(0.5):F2} p95={P(0.95):F2} max={s[^1]:F2} ms";
    }

    static void Report()
    {
        double baseFps = BaselineFrameMs.Count / 10.0, capFps = CaptureFrameMs.Count / 30.0;
        Log.Info($"{Tag} image {_imageSize}");
        Log.Info($"{Tag} baseline frame: {Stats(BaselineFrameMs)} ({baseFps:F1} fps)");
        Log.Info($"{Tag} capture frame:  {Stats(CaptureFrameMs)} ({capFps:F1} fps)");
        Log.Info($"{Tag} GetImage:       {Stats(ReadMs)}");
        Log.Info($"{Tag} SaveJpgToBuffer:{Stats(EncodeMs)}");
        Log.Info($"{Tag} jpeg bytes: mean={JpegBytes.DefaultIfEmpty().Average():F0} max={JpegBytes.DefaultIfEmpty().Max()}");
        Log.Info($"{Tag} done");
    }
}
