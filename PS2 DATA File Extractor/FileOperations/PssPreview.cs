using System.ComponentModel;
using System.Diagnostics;

namespace PS2_DATA_File_Extractor.FileOperations;

internal static class PssPreview
{
    public static bool TryCreate(byte[] pssData, out Bitmap? preview, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(pssData);
        preview = null;
        reason = null;

        string directory = Path.Combine(
            Path.GetTempPath(), "BackyardBaseballPS2Editor", "PssPreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string videoPath = Path.Combine(directory, "preview.pss");
        string imagePath = Path.Combine(directory, "preview.png");
        string scriptPath = Path.Combine(directory, "decode.py");

        try
        {
            File.WriteAllBytes(videoPath, pssData);

            foreach (string ffmpeg in FindExecutables("ffmpeg.exe"))
            {
                if (TryFfmpeg(ffmpeg, videoPath, imagePath, out preview)) return true;
            }

            File.WriteAllText(scriptPath,
                "import cv2, sys\n" +
                "capture = cv2.VideoCapture(sys.argv[1])\n" +
                "count = int(capture.get(cv2.CAP_PROP_FRAME_COUNT))\n" +
                "capture.set(cv2.CAP_PROP_POS_FRAMES, max(0, count // 2))\n" +
                "ok, frame = capture.read()\n" +
                "capture.release()\n" +
                "raise SystemExit(0 if ok and cv2.imwrite(sys.argv[2], frame) else 1)\n");

            foreach (string python in FindPythonExecutables())
            {
                if (TryPythonOpenCv(python, scriptPath, videoPath, imagePath, out preview)) return true;
            }

            reason = "Animation preview unavailable.\nThe PSS can still be exported or replaced.";
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool TryFfmpeg(
        string executable,
        string videoPath,
        string imagePath,
        out Bitmap? preview)
    {
        ProcessStartInfo start = CreateStartInfo(executable);
        start.ArgumentList.Add("-loglevel");
        start.ArgumentList.Add("error");
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-ss");
        start.ArgumentList.Add("0.7");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(videoPath);
        start.ArgumentList.Add("-frames:v");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add(imagePath);
        return RunDecoder(start, imagePath, out preview);
    }

    private static bool TryPythonOpenCv(
        string executable,
        string scriptPath,
        string videoPath,
        string imagePath,
        out Bitmap? preview)
    {
        ProcessStartInfo start = CreateStartInfo(executable);
        start.ArgumentList.Add(scriptPath);
        start.ArgumentList.Add(videoPath);
        start.ArgumentList.Add(imagePath);
        return RunDecoder(start, imagePath, out preview);
    }

    private static ProcessStartInfo CreateStartInfo(string executable) => new()
    {
        FileName = executable,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = false,
        RedirectStandardOutput = false
    };

    private static bool RunDecoder(
        ProcessStartInfo start,
        string imagePath,
        out Bitmap? preview)
    {
        preview = null;
        try
        {
            if (File.Exists(imagePath)) File.Delete(imagePath);
            using Process? process = Process.Start(start);
            if (process == null) return false;
            if (!process.WaitForExit(8000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            if (process.ExitCode != 0 || !File.Exists(imagePath)) return false;
            using Image source = Image.FromFile(imagePath);
            preview = new Bitmap(source);
            return true;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> FindPythonExecutables()
    {
        HashSet<string> results = new(StringComparer.OrdinalIgnoreCase);
        AddPathExecutables(results, "python.exe");

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AddExecutable(results, Path.Combine(localAppData, "Python", "bin", "python.exe"));
        AddChildExecutables(results, Path.Combine(localAppData, "Programs", "Python"), "python.exe");

        string? pyenvRoot = Environment.GetEnvironmentVariable("PYENV_ROOT");
        if (!string.IsNullOrWhiteSpace(pyenvRoot))
            AddChildExecutables(results, Path.Combine(pyenvRoot, "versions"), "python.exe");

        return results.Take(16);
    }

    private static IEnumerable<string> FindExecutables(string fileName)
    {
        HashSet<string> results = new(StringComparer.OrdinalIgnoreCase);
        AddPathExecutables(results, fileName);
        return results;
    }

    private static void AddPathExecutables(ISet<string> results, string fileName)
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue)) return;

        foreach (string directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                AddExecutable(results, Path.Combine(directory.Trim(), fileName));
            }
            catch (ArgumentException)
            {
            }
        }
    }

    private static void AddChildExecutables(ISet<string> results, string parent, string fileName)
    {
        try
        {
            if (!Directory.Exists(parent)) return;
            foreach (string directory in Directory.EnumerateDirectories(parent))
                AddExecutable(results, Path.Combine(directory, fileName));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private static void AddExecutable(ISet<string> results, string path)
    {
        try
        {
            if (File.Exists(path)) results.Add(Path.GetFullPath(path));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }
}
