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

	private static int _httpRunStarted;

	private static int _httpRunDone;

	public static string ServerStatusSuffix = "";

	public static string LastServerVersion = "";

	private static string _launcherArg;

	private static string _clientVersion;

	private static string _writableRoot;

	private static bool _cacheRootsReady;

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
				if (string.IsNullOrEmpty(_clientVersion))
				{
					try
					{
						TextAsset textAsset = Resources.Load<TextAsset>("offserver_version");
						if (textAsset != null && !string.IsNullOrEmpty(textAsset.text))
						{
							_clientVersion = textAsset.text.Trim();
							OffServerLog.Info("[OffServer] ClientVersion from Resources seed = '" + _clientVersion + "'");
						}
					}
					catch
					{
					}
				}
			}
			return _clientVersion;
		}
	}

	public static string WritableRoot
	{
		get
		{
			if (_writableRoot != null)
			{
				return _writableRoot;
			}
			string[] array = ((Application.platform != RuntimePlatform.Android) ? new string[2]
			{
				Path.GetDirectoryName(Application.dataPath) ?? ".",
				Application.persistentDataPath
			} : new string[3]
			{
				Application.persistentDataPath,
				"/data/data/" + Application.identifier + "/files",
				Application.temporaryCachePath
			});
			string[] array2 = array;
			foreach (string text in array2)
			{
				if (!string.IsNullOrEmpty(text))
				{
					try
					{
						Directory.CreateDirectory(text);
						string path = Path.Combine(text, ".offserver-write-test");
						File.WriteAllText(path, "ok");
						File.Delete(path);
						_writableRoot = text;
					}
					catch
					{
						continue;
					}
					break;
				}
			}
			if (_writableRoot == null)
			{
				_writableRoot = Application.persistentDataPath;
			}
			OffServerLog.Info("[OffServer] writable root = " + _writableRoot);
			return _writableRoot;
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

	public static string ConfigPath
	{
		get
		{
			if (Application.platform == RuntimePlatform.Android)
			{
				return Path.Combine(WritableRoot, "offserver.txt");
			}
			return Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "offserver.txt");
		}
	}

	public static int TestBridgePort
	{
		get
		{
			Load();
			return _testBridgePort;
		}
	}

	public static void EnsureCacheRoots()
	{
		if (_cacheRootsReady)
		{
			return;
		}
		_cacheRootsReady = true;
		if (Application.platform != RuntimePlatform.Android)
		{
			return;
		}
		string root = WritableRoot;
		try
		{
			Directory.CreateDirectory(Path.Combine(root, "HTTPCache"));
			HTTPManager.RootCacheFolderProvider = () => root;
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.LogError("[OffServer] BestHTTP cache root failed: " + ex.Message);
		}
		try
		{
			string text = Path.Combine(Application.persistentDataPath, "UnityCache");
			Directory.CreateDirectory(Path.Combine(text, "Shared"));
			Directory.CreateDirectory(Path.Combine(text, "Temp"));
			string path = Path.Combine(text, "Temp/.write-test");
			File.WriteAllText(path, "ok");
			File.Delete(path);
			OffServerLog.Info("[OffServer] Unity bundle cache dirs ready at " + text);
		}
		catch (Exception ex2)
		{
			UnityEngine.Debug.LogError("[OffServer] default Unity cache not writable (" + ex2.Message + "), using " + root);
			try
			{
				string text2 = Path.Combine(root, "unitycache");
				Directory.CreateDirectory(text2);
				Cache currentCacheForWriting = Caching.AddCache(text2);
				if (currentCacheForWriting.valid)
				{
					Caching.currentCacheForWriting = currentCacheForWriting;
				}
			}
			catch (Exception ex3)
			{
				UnityEngine.Debug.LogError("[OffServer] alternate Unity cache failed: " + ex3.Message);
			}
		}
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentProcessId();

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

	public static void ApplyWindowTitle()
	{
		if (Application.platform != RuntimePlatform.WindowsPlayer)
		{
			return;
		}
		try
		{
			string clientVersion = ClientVersion;
			if (string.IsNullOrEmpty(clientVersion))
			{
				return;
			}
			uint currentProcessId = GetCurrentProcessId();
			IntPtr intPtr = IntPtr.Zero;
			while ((intPtr = FindWindowExW(IntPtr.Zero, intPtr, "UnityWndClass", null)) != IntPtr.Zero)
			{
				GetWindowThreadProcessId(intPtr, out var lpdwProcessId);
				if (lpdwProcessId == currentProcessId)
				{
					SetWindowTextW(intPtr, Application.productName + " v" + clientVersion);
					break;
				}
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

	public static void BeginLoadRun()
	{
		_httpRunStarted = _httpStarted;
		_httpRunDone = _httpDone;
	}

	public static float LoadRunProgress()
	{
		int num = _httpStarted - _httpRunStarted;
		if (num <= 0)
		{
			return 0f;
		}
		return Mathf.Clamp01((float)(_httpDone - _httpRunDone) / (float)num);
	}

	public static void SetServerStatus(bool up, int online)
	{
		ServerStatusSuffix = (up ? ("  [66dd66]●[-] Online " + online) : "  [ff5555]●[-] Offline");
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
					if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
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
			string[] array;
			if (File.Exists(configPath))
			{
				array = File.ReadAllLines(configPath, Encoding.UTF8);
				OffServerLog.Info("[OffServer] config=" + configPath);
			}
			else
			{
				TextAsset textAsset = Resources.Load<TextAsset>("offserver");
				if (textAsset == null || string.IsNullOrEmpty(textAsset.text))
				{
					UnityEngine.Debug.LogError("[OffServer] no config at " + configPath + " and no Resources/offserver seed");
					return;
				}
				array = textAsset.text.Replace("\r", string.Empty).Split('\n');
				OffServerLog.Info("[OffServer] config seeded from Resources (" + array.Length + " lines) -> " + configPath);
				try
				{
					Directory.CreateDirectory(Path.GetDirectoryName(configPath));
					File.WriteAllText(configPath, textAsset.text, Encoding.UTF8);
				}
				catch (Exception ex)
				{
					UnityEngine.Debug.LogError("[OffServer] could not write seed config: " + ex.Message);
				}
			}
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
					_account = ((text3.Length == 0) ? null : Sanitize(text3));
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
			OffServerLog.Info("[OffServer] gateway=" + _gateway + " account=" + _account + " localServers=" + _localServers.Count + " resolved account_id=" + AccountId);
			EnsureCacheRoots();
		}
		catch (Exception ex2)
		{
			UnityEngine.Debug.LogError("[OffServer] config load failed: " + ex2);
			_gateway = null;
		}
	}

	public static string RewriteLoopbackHost(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return url;
		}
		try
		{
			Uri uri = new Uri(url);
			if (uri.Host != "127.0.0.1" && !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
			{
				return url;
			}
			string gateway = Gateway;
			if (string.IsNullOrEmpty(gateway))
			{
				return url;
			}
			Uri uri2 = new Uri(gateway);
			string text = new UriBuilder(uri)
			{
				Host = uri2.Host
			}.Uri.ToString();
			OffServerLog.Info("[OffServer] rewrote loopback url " + url + " -> " + text);
			return text;
		}
		catch (Exception)
		{
			return url;
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
