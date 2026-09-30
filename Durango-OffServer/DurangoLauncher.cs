using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            string launcherPath =
                Assembly.GetExecutingAssembly().Location;

            string gameDir =
                Path.GetDirectoryName(launcherPath);

            string gameExe =
                Path.Combine(gameDir, "DurangoV2.exe");

            if (!File.Exists(gameExe))
            {
                MessageBox.Show(
                    "DurangoV2.exe não foi encontrado.\n\n" + gameExe,
                    "Durango Brasil",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                return;
            }

            string userProfile =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile
                );

            string logDir =
                Path.Combine(
                    userProfile,
                    @"AppData\LocalLow\NEXON Korea\Durango_ Wild Lands"
                );

            Directory.CreateDirectory(logDir);

            string logFile =
                Path.Combine(
                    logDir,
                    "output_log.txt"
                );

            ProcessStartInfo psi =
                new ProcessStartInfo();

            psi.FileName = gameExe;
            psi.WorkingDirectory = gameDir;
            psi.Arguments =
                "-logFile \"" + logFile + "\"";

            psi.UseShellExecute = false;

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Erro ao iniciar Durango:\n\n" + ex.Message,
                "Durango Brasil",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }
}
