using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BestHTTP;
using Durango.Development;
using Durango.Utils;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Durango.Logic.Clusters;

public static class OffServerLink
{
	public sealed class RemoteServerEntry
	{
		[JsonProperty(PropertyName = "name")]
		public string Name;

		[JsonProperty(PropertyName = "gateway")]
		public string Gateway;
	}

	public sealed class RemoteServerSet
	{
		[JsonProperty(PropertyName = "servers")]
		public List<RemoteServerEntry> Servers;
	}

	public const string FileName = "offserver.txt";

	private static bool _loaded;

	private static string _gateway;

	private static string _account;

	private static string _name;

	private static int _testBridgePort;

	private static readonly List<string[]> _multiServers = new List<string[]>();

	private static readonly List<string[]> _localServers = new List<string[]>();

	public const string DefaultServersUrl = "https://raw.githubusercontent.com/ShuuuuShi/Durango-OffServer-Client/main/servers.json";

	private static string _serversUrl;

	private static bool _cacheLoaded;

	public static bool IsAdmin;

	public static string ServerAccountId;

	public static string DiscordName;

	private static int _httpStarted;

	private static int _httpDone;

	private static long _httpBytes;

	public static string ServerStatusSuffix = "";

	public static string LastServerVersion = "";

	private static string _launcherArg;

	private static string _clientVersion;

	public static List<string[]> MultiServers
	{
		get
		{
			Load();
			if (_localServers.Count > 0)
			{
				return _localServers;
			}
			if (_multiServers.Count == 0)
			{
				LoadServersCache();
			}
			return _multiServers;
		}
	}

	public static bool Active
	{
		get
		{
			if (!Enabled)
			{
				return MultiServers.Count > 0;
			}
			return true;
		}
	}

	public static string Name
	{
		get
		{
			Load();
			return _name;
		}
	}

	public static string ServersUrl
	{
		get
		{
			Load();
			if (!string.IsNullOrEmpty(_serversUrl))
			{
				return _serversUrl;
			}
			return "https://raw.githubusercontent.com/ShuuuuShi/Durango-OffServer-Client/main/servers.json";
		}
	}

	public static string ServersCachePath => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "offserver.servers.cache.json");

	public static string LauncherArg
	{
		get
		{
			if (_launcherArg == null)
			{
				_launcherArg = string.Empty;
				try
				{
					string[] commandLineArgs = global::System.Environment.GetCommandLineArgs();
					for (int i = 0; i < commandLineArgs.Length; i++)
					{
						if (commandLineArgs[i] == "-offserver-launcher")
						{
							_launcherArg = ((i + 1 < commandLineArgs.Length) ? commandLineArgs[i + 1] : "1");
							break;
						}
					}
				}
				catch
				{
				}
			}
			return _launcherArg;
		}
	}

	public static string ClientVersion
	{
		get
		{
			if (_clientVersion == null)
			{
				_clientVersion = string.Empty;
				try
				{
					string path = Path.Combine(Application.dataPath, "version.txt");
					if (File.Exists(path))
					{
						_clientVersion = File.ReadAllText(path).Trim();
					}
				}
				catch
				{
				}
			}
			return _clientVersion;
		}
	}

	public static bool Enabled
	{
		get
		{
			Load();
			return !string.IsNullOrEmpty(_gateway);
		}
	}

	public static string Gateway
	{
		get
		{
			Load();
			return _gateway;
		}
	}

	public static string AccountId
	{
		get
		{
			Load();
			if (!string.IsNullOrEmpty(_account))
			{
				return _account;
			}
			return Sanitize(SystemInfo.deviceUniqueIdentifier);
		}
	}

	public static string ConfigPath => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "offserver.txt");

	public static int TestBridgePort
	{
		get
		{
			Load();
			return _testBridgePort;
		}
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindowW(string lpClassName, string lpWindowName);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

	public static void ApplyWindowTitle()
	{
		try
		{
			IntPtr intPtr = FindWindowW(null, "Durango: Wild Lands");
			if (intPtr != IntPtr.Zero)
			{
				SetWindowTextW(intPtr, "Durango: Wild Lands — OffServer v" + ClientVersion);
			}
		}
		catch (Exception)
		{
		}
	}

	public static void SetAdminFlags(Dictionary<string, bool> flags)
	{
		bool value = false;
		IsAdmin = (flags?.TryGetValue("admin", out value) ?? false) && value;
	}

	public static void NoteRequestStart()
	{
		_httpStarted++;
	}

	public static void NoteRequestDone(int bytes)
	{
		_httpDone++;
		if (bytes > 0)
		{
			_httpBytes += bytes;
		}
	}

	public static string DownloadProgressText()
	{
		int httpStarted = _httpStarted;
		int httpDone = _httpDone;
		if (httpStarted <= 0)
		{
			return "Connecting…";
		}
		int num = (int)(100L * (long)httpDone / httpStarted);
		if (num > 100)
		{
			num = 100;
		}
		int num2 = num / 10;
		string text = new string('█', num2) + new string('░', 10 - num2);
		string text2 = ((double)_httpBytes / 1048576.0).ToString("0.0");
		if (httpDone >= httpStarted)
		{
			return "Data loaded " + httpStarted + " files · " + text2 + " MB — entering world…";
		}
		return "Loading data " + httpDone + "/" + httpStarted + " files · " + text2 + " MB  [" + text + "] " + num + "%";
	}

	public static void SetServerStatus(bool up, int online)
	{
        ServerStatusSuffix = (up ? (" • Jogadores online: " + online) : " • Servidor offline");
	}

	public static Cluster CreateClusterNamed(string displayName, string gateway = null)
	{
		string name = _name;
		_name = displayName;
		Cluster cluster = CreateCluster();
		_name = name;
		if (!string.IsNullOrEmpty(gateway))
		{
			cluster.GatewayUrlRoot = gateway;
		}
		return cluster;
	}

	public static void FetchStatus(string gateway, Action<bool, int> callback)
	{
		if (string.IsNullOrEmpty(gateway))
		{
			callback(arg1: false, 0);
			return;
		}
		try
		{
			HTTPRequest hTTPRequest = new HTTPRequest(new Uri(gateway.TrimEnd('/') + "/status"), HTTPMethods.Get, delegate(HTTPRequest req, HTTPResponse resp)
			{
				bool arg = false;
				int result = 0;
				try
				{
					if (resp != null && resp.IsSuccess)
					{
						string text = resp.DataAsText ?? string.Empty;
						arg = text.Contains("\"ok\":true") || text.Contains("\"ok\": true");
						Match match = Regex.Match(text, "\"online\"\\s*:\\s*(\\d+)");
						if (match.Success)
						{
							int.TryParse(match.Groups[1].Value, out result);
						}
					}
				}
				catch
				{
				}
				callback(arg, result);
			});
			hTTPRequest.Timeout = TimeSpan.FromSeconds(5.0);
			hTTPRequest.DisableCache = true;
			hTTPRequest.Send();
		}
		catch
		{
			callback(arg1: false, 0);
		}
	}

	public static bool SameGateway(string a, string b)
	{
		if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
		{
			return false;
		}
		return string.Equals(NormalizeUrl(a), NormalizeUrl(b), StringComparison.OrdinalIgnoreCase);
	}

	private static List<string[]> ParseServers(string text)
	{
		List<string[]> list = new List<string[]>();
		if (string.IsNullOrEmpty(text))
		{
			return list;
		}
		RemoteServerSet remoteServerSet = Json.Read<RemoteServerSet>(text);
		if (remoteServerSet?.Servers == null)
		{
			return list;
		}
		foreach (RemoteServerEntry server in remoteServerSet.Servers)
		{
			if (server != null && !string.IsNullOrEmpty(server.Name) && !string.IsNullOrEmpty(server.Gateway))
			{
				list.Add(new string[2]
				{
					server.Name.Trim(),
					NormalizeUrl(server.Gateway)
				});
			}
		}
		return list;
	}

	public static void LoadServersCache()
	{
		if (_cacheLoaded)
		{
			return;
		}
		_cacheLoaded = true;
		try
		{
			string serversCachePath = ServersCachePath;
			if (File.Exists(serversCachePath))
			{
				List<string[]> list = ParseServers(File.ReadAllText(serversCachePath));
				if (list.Count > 0)
				{
					_multiServers.Clear();
					_multiServers.AddRange(list);
				}
			}
		}
		catch
		{
		}
	}

	public static void FetchServers(Action onChanged)
	{
		Load();
		if (_localServers.Count > 0)
		{
			return;
		}
		string url = ServersUrl;
		try
		{
			HTTPRequest hTTPRequest = new HTTPRequest(new Uri(url), HTTPMethods.Get, delegate(HTTPRequest req, HTTPResponse resp)
			{
				bool flag = false;
				try
				{
					flag = resp != null && resp.IsSuccess && ApplyServersJson(resp.DataAsText, "BestHTTP", onChanged);
				}
				catch (Exception ex2)
				{
					UnityEngine.Debug.Log("[OffServer] servers.json BestHTTP parse error: " + ex2.Message);
				}
				if (!flag)
				{
					UnityEngine.Debug.Log("[OffServer] servers.json BestHTTP failed: " + req.State.ToString() + " " + ((resp != null) ? resp.StatusCode.ToString() : "no-response") + ((req.Exception != null) ? (" " + req.Exception.Message) : "") + " -> UnityWebRequest");
					FetchServersUnity(url, onChanged);
				}
			});
			hTTPRequest.Timeout = TimeSpan.FromSeconds(6.0);
			hTTPRequest.DisableCache = true;
			hTTPRequest.Send();
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.Log("[OffServer] servers.json BestHTTP error: " + ex.Message + " -> UnityWebRequest");
			FetchServersUnity(url, onChanged);
		}
	}

	private static void FetchServersUnity(string url, Action onChanged)
	{
		try
		{
			UnityWebRequest www = UnityWebRequest.Get(url);
			www.timeout = 8;
			www.SendWebRequest().completed += delegate
			{
				try
				{
					if (www.isNetworkError || www.isHttpError)
					{
						UnityEngine.Debug.Log("[OffServer] servers.json UnityWebRequest failed: " + www.responseCode + " " + www.error + " -> keep cache (" + _multiServers.Count + ")");
					}
					else if (!ApplyServersJson(www.downloadHandler.text, "UnityWebRequest", onChanged))
					{
						UnityEngine.Debug.Log("[OffServer] servers.json UnityWebRequest: empty/invalid list");
					}
				}
				catch (Exception ex2)
				{
					UnityEngine.Debug.Log("[OffServer] servers.json UnityWebRequest error: " + ex2.Message);
				}
				finally
				{
					www.Dispose();
				}
			};
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.Log("[OffServer] servers.json UnityWebRequest start error: " + ex.Message);
		}
	}

	private static bool ApplyServersJson(string text, string via, Action onChanged)
	{
		List<string[]> list = ParseServers(text);
		if (list.Count == 0)
		{
			return false;
		}
		bool flag = list.Count != _multiServers.Count;
		int num = 0;
		while (!flag && num < list.Count)
		{
			flag = list[num][0] != _multiServers[num][0] || list[num][1] != _multiServers[num][1];
			num++;
		}
		_multiServers.Clear();
		_multiServers.AddRange(list);
		_cacheLoaded = true;
		try
		{
			File.WriteAllText(ServersCachePath, text);
		}
		catch
		{
		}
		UnityEngine.Debug.Log("[OffServer] servers.json via " + via + ": " + list.Count + " servers" + (flag ? " (changed -> refresh list)" : ""));
		if (flag)
		{
			onChanged?.Invoke();
		}
		return true;
	}

	public static Cluster CreateCluster()
	{
		Load();
		TestBridge.EnsureStarted(_testBridgePort);
		Cluster obj = new Cluster
		{
			GatewayUrlRoot = _gateway,
			Mode = Mode.Online
		};
		string value = ((!string.IsNullOrEmpty(_name)) ? _name : ("OffServer " + _gateway));
		obj.Names = new Dictionary<string, string>
		{
			{ "en_US", value },
			{ "ko_KR", value }
		};
		obj.OnRequestAccount = null;
		obj.OnConfirm = null;
		obj.OnDeletePlayer = null;
		obj.IsRecommendable = true;
		return obj;
	}

	private static void Load()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		ApplyWindowTitle();
		try
		{
			string configPath = ConfigPath;
			if (!File.Exists(configPath))
			{
				return;
			}
			string[] array = File.ReadAllLines(configPath, Encoding.UTF8);
			for (int i = 0; i < array.Length; i++)
			{
				string text = array[i].Trim();
				if (text.Length == 0 || text.StartsWith("#"))
				{
					continue;
				}
				int num = text.IndexOf('=');
				if (num < 0)
				{
					_gateway = NormalizeUrl(text);
					continue;
				}
				string text2 = text.Substring(0, num).Trim().ToLowerInvariant();
				string text3 = text.Substring(num + 1).Trim();
				switch (text2)
				{
				case "gateway":
				case "url":
					_gateway = NormalizeUrl(text3);
					break;
				case "account":
					_account = Sanitize(text3);
					break;
				case "name":
					_name = text3;
					break;
				case "server":
				{
					string[] array2 = text3.Split('|');
					if (array2.Length >= 1 && array2[0].Trim().Length > 0)
					{
						_localServers.Add(new string[2]
						{
							array2[0].Trim(),
							(array2.Length > 1) ? array2[1].Trim() : string.Empty
						});
					}
					break;
				}
				case "servers_url":
					_serversUrl = text3.Trim();
					break;
				case "testbridge":
					int.TryParse(text3, out _testBridgePort);
					break;
				}
			}
			string.IsNullOrEmpty(_gateway);
		}
		catch (Exception)
		{
			_gateway = null;
		}
	}

	private static string NormalizeUrl(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return null;
		}
		string text = value.Trim().TrimEnd('/');
		if (!text.StartsWith("http://") && !text.StartsWith("https://"))
		{
			text = "http://" + text;
		}
		return text;
	}

	private static string Sanitize(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "pc-unknown";
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
			{
				stringBuilder.Append(c);
			}
		}
		if (stringBuilder.Length == 0)
		{
			return "pc-unknown";
		}
		if (stringBuilder.Length > 120)
		{
			stringBuilder.Length = 120;
		}
		return stringBuilder.ToString();
	}
}
