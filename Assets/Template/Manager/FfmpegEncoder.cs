using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ffmpeg.exe로 JPG 프레임 시퀀스 + WAV → MP4 합성.
// Windows 키오스크 전제. ffmpeg.exe를 StreamingAssets/ffmpeg/ffmpeg.exe 에 배치해야 동작.
public static class FfmpegEncoder
{
    /// StreamingAssets 내부 상대 경로. 운영 환경에선 여기에 ffmpeg.exe 배치
    public const string RelativeFfmpegPath = "ffmpeg/ffmpeg.exe";

    public static string ResolveFfmpegPath()
    {
        return Path.Combine(Application.streamingAssetsPath, RelativeFfmpegPath);
    }

    public static bool IsAvailable()
    {
        return File.Exists(ResolveFfmpegPath());
    }

    /// <summary>
    /// 프레임 시퀀스(framesDir 내부 frame_%05d.jpg) + audio.wav 를 outputMp4 경로로 합성.
    /// </summary>
    public static async Task<bool> EncodeAsync(string framesDir, string audioWavPath, string outputMp4, int frameRate)
    {
        if (!IsAvailable())
        {
            Debug.LogError($"[Ffmpeg] ffmpeg.exe 없음: {ResolveFfmpegPath()}\n" +
                           "StreamingAssets/ffmpeg/ffmpeg.exe 에 배치해주세요.");
            return false;
        }

        if (!Directory.Exists(framesDir))
        {
            Debug.LogError($"[Ffmpeg] 프레임 디렉토리 없음: {framesDir}");
            return false;
        }

        var outDir = Path.GetDirectoryName(outputMp4);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            Directory.CreateDirectory(outDir);

        string framePattern = Path.Combine(framesDir, "frame_%05d.jpg");
        bool hasAudio = !string.IsNullOrEmpty(audioWavPath) && File.Exists(audioWavPath);

        string args = hasAudio
            ? $"-y -framerate {frameRate} -i \"{framePattern}\" -i \"{audioWavPath}\" " +
              $"-c:v libx264 -pix_fmt yuv420p -c:a aac -b:a 128k -shortest \"{outputMp4}\""
            : $"-y -framerate {frameRate} -i \"{framePattern}\" " +
              $"-c:v libx264 -pix_fmt yuv420p \"{outputMp4}\"";

        Debug.Log($"[Ffmpeg] invoke: {ResolveFfmpegPath()} {args}");

        try
        {
            var psi = new ProcessStartInfo(ResolveFfmpegPath(), args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                Debug.LogError("[Ffmpeg] 프로세스 시작 실패");
                return false;
            }

            var stderrTask = proc.StandardError.ReadToEndAsync();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();

            await Task.Run(() => proc.WaitForExit());

            string stderr = await stderrTask;
            await stdoutTask;

            if (proc.ExitCode != 0)
            {
                Debug.LogError($"[Ffmpeg] exit={proc.ExitCode}\n{stderr}");
                return false;
            }

            Debug.Log($"[Ffmpeg] done exit=0 out={outputMp4} size={new FileInfo(outputMp4).Length}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Ffmpeg] 예외: {e.Message}");
            return false;
        }
    }
}
