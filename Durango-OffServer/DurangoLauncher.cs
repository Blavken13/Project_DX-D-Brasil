using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LostHorizon.PC;
using Path = System.IO.Path;

public static class Program
{
    public static void Run(Window window, string directory, bool preview)
    {
        LoginWindow controller = new LoginWindow(window, directory, preview);
        new Application().Run(window);
        GC.KeepAlive(controller);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!File.Exists(Path.Combine(directory, "DurangoV2.exe")))
                throw new FileNotFoundException("DurangoV2.exe não foi encontrado na pasta do jogo.");
            using (FileStream file = File.OpenRead(Path.Combine(directory, "Launcher/login.xaml")))
            {
                Window window = (Window)XamlReader.Load(file);
                Run(window, directory, args.Contains("--preview"));
            }
        }
        catch (Exception error)
        {
            MessageBox.Show("Não foi possível abrir Lost Horizon.\n\n" + error.Message,
                "Lost Horizon", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

internal sealed class LoginWindow
{
    private readonly Window window;
    private readonly string directory;
    private readonly bool preview;
    private readonly MediaElement video;
    private readonly ComboBox server;
    private readonly TextBox username;
    private readonly PasswordBox password, confirmation;
    private LauncherAuth auth;
    private LauncherSession session;
    private bool busy, register;

    private sealed class ServerChoice
    {
        internal string Name, Gateway;
        public override string ToString() { return Name; }
    }

    internal LoginWindow(Window window, string directory, bool preview)
    {
        this.window = window; this.directory = directory; this.preview = preview;
        if (preview) window.Title = "Lost Horizon · Prévia do login";
        video = Find<MediaElement>("Video"); server = Find<ComboBox>("Server");
        username = Find<TextBox>("Username"); password = Find<PasswordBox>("Password");
        confirmation = Find<PasswordBox>("Confirmation");
        Find<Image>("Logo").Source = new BitmapImage(new Uri(Path.Combine(directory, "Launcher/logo.png")));
        window.Icon = new BitmapImage(new Uri(Path.Combine(directory, "Launcher/icon.ico")));
        video.Source = new Uri(Path.Combine(directory, "DurangoV2_Data/StreamingAssets/Movie/PC/title.mp4"));
        video.MediaEnded += delegate { video.Position = TimeSpan.Zero; video.Play(); };
        video.MediaFailed += delegate { video.Stop(); };
        window.Closed += delegate { video.Close(); };
        Find<Button>("Submit").Click += async delegate { await Submit(); };
        Find<Button>("Continue").Click += async delegate { await Continue(); };
        Find<Button>("Switch").Click += delegate { SetRegister(!register); };
        Find<Button>("SignOut").Click += delegate { auth.Clear(); session = null; password.Clear(); confirmation.Clear(); Refresh(); };
        server.SelectionChanged += async delegate {
            if (server.SelectedItem != null) { SelectServer(); if (window.IsLoaded) await PollStatus(); }
        };
        window.Loaded += async delegate { video.Play(); await PollStatus(); };
        List<ServerChoice> choices = Servers(directory);
        foreach (ServerChoice choice in choices) server.Items.Add(choice);
        Visible("ServerPanel", choices.Count > 1);
        string configured = ConfiguredGateway(directory);
        server.SelectedItem = choices.FirstOrDefault(s => Same(s.Gateway, configured)) ?? choices[0];
    }

    private T Find<T>(string name) where T : class { return (T)window.FindName(name); }
    private void Visible(string name, bool show) { Find<UIElement>(name).Visibility = show ? Visibility.Visible : Visibility.Collapsed; }
    private static bool Same(string a, string b) { return String.Equals((a ?? "").TrimEnd('/'), (b ?? "").TrimEnd('/'), StringComparison.OrdinalIgnoreCase); }

    private void SelectServer()
    {
        ServerChoice choice = (ServerChoice)server.SelectedItem;
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vision Force/Lost Horizon/PC");
        auth = new LauncherAuth(choice.Gateway, folder);
        session = preview ? null : auth.Load();
        password.Clear(); confirmation.Clear(); SetRegister(false);
    }

    private void Refresh()
    {
        bool saved = session != null;
        Visible("Form", !saved); Visible("Account", saved);
        Visible("ConfirmationPanel", register && !saved);
        Find<TextBlock>("Heading").Text = saved ? "Sua aventura continua" : register ? "Criar conta" : "Entrar";
        Find<TextBlock>("Description").Text = register ? "Sua aventura começa aqui." : "Bem-vindo ao alfa de Lost Horizon.";
        Find<TextBlock>("AccountName").Text = saved ? session.Username : "";
        Find<Button>("Submit").Content = register ? "CRIAR CONTA" : "ENTRAR";
        Find<Button>("Switch").Content = register ? "Já tenho uma conta" : "Criar uma conta";
        Find<Button>("Submit").IsDefault = !saved;
        Find<Button>("Continue").IsDefault = saved;
        Notice("");
        if (!saved) username.Focus();
    }

    private void SetRegister(bool value) { register = value; confirmation.Clear(); Refresh(); }
    private void Notice(string text) { Find<TextBlock>("Notice").Text = text; }
    private void Busy(bool value)
    {
        busy = value; Visible("Progress", value); server.IsEnabled = !value;
        foreach (string name in new[] { "Form", "Account" }) Find<UIElement>(name).IsEnabled = !value;
    }

    private async System.Threading.Tasks.Task Submit()
    {
        if (busy || preview) return;
        Busy(true); Notice("");
        try
        {
            session = await auth.Authenticate(username.Text, password.Password, confirmation.Password, register);
            password.Clear(); confirmation.Clear();
            await auth.Validate(session);
            try { auth.Save(session); } catch (IOException) { } catch (System.Security.Cryptography.CryptographicException) { }
            StartGame(session);
        }
        catch (AuthFailure error) { session = null; Notice(error.Message); }
        catch (Exception) { Notice("Não foi possível abrir o jogo. Tente novamente."); }
        finally { Busy(false); }
    }

    private async System.Threading.Tasks.Task Continue()
    {
        if (busy || preview) return;
        Busy(true); Notice("");
        try { await auth.Validate(session); StartGame(session); }
        catch (AuthFailure error)
        {
            if (error.SessionLost) { auth.Clear(); session = null; Refresh(); }
            Notice(error.Message);
        }
        catch (Exception) { Notice("Não foi possível abrir o jogo. Tente novamente."); }
        finally { Busy(false); }
    }

    private void StartGame(LauncherSession authenticated)
    {
        string logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            @"AppData\LocalLow\NEXON Korea\Durango_ Wild Lands");
        Directory.CreateDirectory(logs);
        ProcessStartInfo start = new ProcessStartInfo {
            FileName = Path.Combine(directory, "DurangoV2.exe"), WorkingDirectory = directory,
            Arguments = "-logFile \"" + Path.Combine(logs, "output_log.txt") + "\"", UseShellExecute = false };
        authenticated.AddToEnvironment(start);
        video.Stop();
        try { Process.Start(start); window.Close(); }
        catch { video.Play(); throw; }
    }

    private async System.Threading.Tasks.Task PollStatus()
    {
        LauncherAuth selected = auth;
        if (selected == null) return;
        int online = await selected.Online().ConfigureAwait(false);
        if (window.Dispatcher.HasShutdownStarted) return;
        await window.Dispatcher.InvokeAsync(delegate {
            if (selected != auth) return;
            Find<TextBlock>("Status").Text = online >= 0 ? "Lost Horizon · Servidor online" : "Servidor indisponível no momento";
            Find<Ellipse>("StatusDot").Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(online >= 0 ? "#A2CA8D" : "#DC9C80"));
        });
    }

    private static string ConfiguredGateway(string directory)
    {
        string selected = LauncherAuth.DefaultGateway;
        string path = Path.Combine(directory, "offserver.txt");
        if (File.Exists(path)) foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            int separator = line.IndexOf('=');
            if (separator < 0) continue;
            string key = line.Substring(0, separator).Trim().ToLowerInvariant();
            if (key == "gateway" || key == "url") selected = line.Substring(separator + 1).Trim().TrimEnd('/');
        }
        return selected;
    }

    private static List<ServerChoice> Servers(string directory)
    {
        List<ServerChoice> choices = new List<ServerChoice>();
        string path = Path.Combine(directory, "offserver.txt");
        if (File.Exists(path)) foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (!line.StartsWith("server=", StringComparison.OrdinalIgnoreCase)) continue;
            string[] entry = line.Substring(7).Split('|');
            if (entry.Length > 1 && !choices.Any(c => Same(c.Gateway, entry[1])))
                choices.Add(new ServerChoice { Name = entry[0].Trim(), Gateway = entry[1].Trim().TrimEnd('/') });
        }
        string configured = ConfiguredGateway(directory);
        if (!choices.Any(c => Same(c.Gateway, configured)))
            choices.Add(new ServerChoice { Name = "Lost Horizon Brasil", Gateway = configured });
        return choices;
    }
}
