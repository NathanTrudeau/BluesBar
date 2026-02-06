using System;
using System.Diagnostics;
using System.IO;

namespace BluesBar.Systems
{
    public static class AimTrainerLauncher
    {
        public static void Launch()
        {
            string exePath = FindAimTrainerExe();
            string aimDir = Path.GetDirectoryName(exePath)!;

            string profileDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BluesBar");

            string args = $"--profileDir \"{profileDir}\"";

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                WorkingDirectory = aimDir,   // critical for Assets
                UseShellExecute = false
            };

            Process.Start(psi);
        }

        private static string FindAimTrainerExe()
        {
            // Start in the actual runtime folder (bin/Debug/...).
            string? dir = AppDomain.CurrentDomain.BaseDirectory;

            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string candidate = Path.Combine(dir, "Packages", "BluesAimTrain", "BluesAimTrain.exe");
                if (File.Exists(candidate))
                    return candidate;

                // also try one level deeper for "Packages" under project folder scenarios
                candidate = Path.Combine(dir, "..", "Packages", "BluesAimTrain", "BluesAimTrain.exe");
                candidate = Path.GetFullPath(candidate);
                if (File.Exists(candidate))
                    return candidate;

                dir = Directory.GetParent(dir)?.FullName;
            }

            // If we got here, show where we searched from
            throw new FileNotFoundException(
                "Aim trainer not found.\n\nExpected at:\n" +
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Packages", "BluesAimTrain", "BluesAimTrain.exe") +
                "\n\nTip: Put the package under your output folder (bin/Debug/...)\n" +
                "or keep it at repo root and let this search climb up to find it."
            );
        }
    }
}


