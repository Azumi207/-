using System.Runtime.InteropServices;
using System.Text.Json;

namespace Frameflow.Media;

internal enum EncodingAcceleration { Auto, CpuOnly }
internal sealed record EncoderProfile(string Codec, string Label, bool Hardware);
internal sealed record EncoderAttempt(string Codec, bool Available, int? ExitCode, string Detail);
internal sealed record EncoderSelection(EncoderProfile Profile, IReadOnlyList<EncoderAttempt> Attempts, string Notice);
internal sealed record EncoderRun<T>(T Result, EncoderProfile Profile, bool FellBack);

internal static class HardwareEncoding
{
    internal static readonly EncoderProfile Cpu = new("libx264", "CPU / x264", false);
    internal static readonly IReadOnlyList<EncoderProfile> Candidates = Array.AsReadOnly(new[] {
        new EncoderProfile("h264_nvenc", "NVIDIA NVENC", true),
        new EncoderProfile("h264_qsv", "Intel Quick Sync", true),
        new EncoderProfile("h264_amf", "AMD AMF", true)
    });

    internal static string[] VideoArguments(EncoderProfile profile, int quality)
    {
        if (quality is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(quality));
        // These presets are not numerically equivalent across encoders. Never claim equal bitrate/quality.
        string q = quality switch { 1 => "18", 2 => "24", _ => "20" };
        return profile.Codec switch {
            "libx264" => ["-c:v", "libx264", "-preset", "medium", "-crf", q, "-pix_fmt", "yuv420p"],
            "h264_nvenc" => ["-c:v", "h264_nvenc", "-gpu", "any", "-preset", "p5", "-tune", "hq", "-rc", "vbr", "-cq", q, "-b:v", "0", "-pix_fmt", "nv12"],
            "h264_qsv" => ["-c:v", "h264_qsv", "-preset", "medium", "-global_quality", q, "-look_ahead", "0", "-pix_fmt", "nv12"],
            "h264_amf" => ["-c:v", "h264_amf", "-usage", "transcoding", "-quality", "balanced", "-rc", "cqp", "-qp_i", q, "-qp_p", q, "-qp_b", q, "-pix_fmt", "nv12"],
            _ => throw new ArgumentException("未支持的编码器。")
        };
    }

    internal static EncoderSelection Select(IReadOnlyList<EncoderAttempt> attempts)
    {
        var available = Candidates.Where(p => attempts.Any(a => a.Codec == p.Codec && a.Available)).ToArray();
        return available.Length == 0
            ? new(Cpu, attempts, "显卡试编码未通过，自动使用 CPU（无需手动配置）。")
            : new(available[0], attempts, "试编码可用：" + string.Join("、", available.Select(p => p.Label)) + "；本次使用 " + available[0].Label + "。");
    }

    internal static async Task<EncoderSelection> DetectAsync(string work, int quality, CancellationToken token, Action<string> log, Action<string>? status = null)
    {
        var attempts = new List<EncoderAttempt>();
        log("DISPLAY ADAPTERS (informational, not capability proof): " + JsonSerializer.Serialize(DisplayAdapters()));
        foreach (var profile in Candidates)
        {
            token.ThrowIfCancellationRequested();
            status?.Invoke("正在试编码检测 " + profile.Label + "…");
            string probe = Path.Combine(work, "probe-" + profile.Codec + ".mp4");
            int? exit = null;
            try
            {
                var args = new List<string> { "-hide_banner", "-nostdin", "-n", "-xerror", "-loglevel", "info", "-filter_threads", "2", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=24:duration=0.25", "-frames:v", "6", "-an" };
                args.AddRange(VideoArguments(profile, quality));
                args.AddRange(["-f", "mp4", probe]);
                var p = await BurnInEncoder.RunAsync(BurnInEncoder.Tool("ffmpeg"), args, work, token, log, null, 12).ConfigureAwait(false);
                exit = p.Exit;
                if (p.Exit != 0) throw new InvalidOperationException(p.Error[^Math.Min(p.Error.Length, 1200)..]);
                var facts = await BurnInEncoder.ProbeAsync(probe, token, log).ConfigureAwait(false);
                if (facts.Codec != "h264" || facts.Width != 1280 || facts.Height != 720 || facts.Duration < .1)
                    throw new InvalidOperationException("试编码输出验证失败。");
                attempts.Add(new(profile.Codec, true, exit, "真实 MP4 试编码及 ffprobe 验证成功。"));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is IOException or InvalidOperationException or FormatException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                attempts.Add(new(profile.Codec, false, exit, e.Message));
            }
            finally { if (File.Exists(probe)) File.Delete(probe); }
        }
        var result = Select(attempts);
        log("GPU DETECTION: " + JsonSerializer.Serialize(result));
        status?.Invoke(result.Notice);
        return result;
    }

    // Run at most one hardware attempt and one CPU retry. Cancellation is never retried.
    // The callback includes output validation so a corrupt/incomplete hardware output is not committed.
    internal static async Task<EncoderRun<T>> RunWithFallbackAsync<T>(EncoderProfile first, Func<EncoderProfile, Task<T>> attempt, Action resetTemporaryOutput, CancellationToken token, Action<string> log, Action<string>? status = null)
    {
        token.ThrowIfCancellationRequested();
        try { return new(await attempt(first).ConfigureAwait(false), first, false); }
        catch (Exception e) when (first.Hardware && !token.IsCancellationRequested && e is EncoderAttemptFailure)
        {
            token.ThrowIfCancellationRequested();
            log("HARDWARE FAILED; retry once from beginning on CPU: " + e.Message);
            resetTemporaryOutput();
            status?.Invoke(first.Label + " 压制失败，已自动改用 CPU 从头重试。不会覆盖原文件。");
            token.ThrowIfCancellationRequested();
            return new(await attempt(Cpu).ConfigureAwait(false), Cpu, true);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public int State;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice display, uint flags);

    internal static string[] DisplayAdapters()
    {
        var names = new List<string>();
        for (uint i = 0; i < 32; i++)
        {
            var d = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref d, 0)) break;
            if ((d.State & 8) == 0 && !string.IsNullOrWhiteSpace(d.Description)) names.Add(d.Description);
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
internal sealed class EncoderAttemptFailure(string message, Exception? inner = null) : Exception(message, inner);
