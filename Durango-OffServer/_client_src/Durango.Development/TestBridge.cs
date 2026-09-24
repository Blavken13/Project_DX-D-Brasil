using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Crafting;
using Durango.Logic;
using Durango.Logic.Faction;
using Durango.Logic.InputSystem;
using Durango.Logic.Item;
using Durango.Logic.Npc;
using Durango.Logic.PlayGuide;
using Durango.Logic.Social;
using Durango.Network;
using Durango.Terrain;
using Durango.UI;
using Durango.UI.Control;
using Durango.UI.Popup;
using Durango.Utils;
using InteractionData;
using Messages;
using Shared.Quest;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Durango.Development;

public class TestBridge : MonoBehaviour
{
	private sealed class Request
	{
		public TcpClient Client;

		public string Line;
	}

	public sealed class Result
	{
		public readonly Dictionary<string, object> Data = new Dictionary<string, object>();

		public bool Ok = true;

		public Result Set(string key, object value)
		{
			Data[key] = value;
			return this;
		}

		public Result Fail(string error)
		{
			Ok = false;
			Data["error"] = error;
			return this;
		}
	}

	private delegate IEnumerator Handler(string[] args, Result result);

	private const int LogCapacity = 200;

	private static TestBridge _instance;

	private static readonly List<string> _log = new List<string>();

	private readonly Queue<Request> _pending = new Queue<Request>();

	private readonly Dictionary<string, Handler> _handlers = new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, string> _help = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private TcpListener _listener;

	private Thread _acceptThread;

	private volatile bool _running;

	private int _port;

	private static readonly string[] GaugeKeys = new string[8] { "life", "health", "stamina", "energy", "fatigue", "groggy", "hunger", "thirst" };

	public static void EnsureStarted(int port)
	{
		if (port > 0 && !(_instance != null))
		{
			GameObject obj = new GameObject("OffServerTestBridge");
			UnityEngine.Object.DontDestroyOnLoad(obj);
			_instance = obj.AddComponent<TestBridge>();
			_instance._port = port;
			_instance.StartListener();
		}
	}

	public static void Record(string kind, string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		lock (_log)
		{
			_log.Add(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + kind + "] " + text);
			if (_log.Count > 200)
			{
				_log.RemoveRange(0, _log.Count - 200);
			}
		}
	}

	private void Awake()
	{
		RegisterHandlers();
		Application.logMessageReceived += OnUnityLog;
	}

	private void OnDestroy()
	{
		Application.logMessageReceived -= OnUnityLog;
		StopListener();
	}

	private void OnUnityLog(string condition, string stackTrace, LogType type)
	{
		if (type == LogType.Error || type == LogType.Exception)
		{
			Record(type.ToString().ToLowerInvariant(), condition);
		}
	}

	private void StartListener()
	{
		try
		{
			_listener = new TcpListener(IPAddress.Loopback, _port);
			_listener.Start();
			_running = true;
			_acceptThread = new Thread(AcceptLoop);
			_acceptThread.IsBackground = true;
			_acceptThread.Name = "OffServerTestBridge";
			_acceptThread.Start();
		}
		catch (Exception)
		{
		}
	}

	private void StopListener()
	{
		_running = false;
		try
		{
			if (_listener != null)
			{
				_listener.Stop();
			}
		}
		catch
		{
		}
	}

	private void AcceptLoop()
	{
		while (_running)
		{
			TcpClient client;
			try
			{
				client = _listener.AcceptTcpClient();
			}
			catch
			{
				break;
			}
			Thread thread = new Thread((ThreadStart)delegate
			{
				ClientLoop(client);
			});
			thread.IsBackground = true;
			thread.Start();
		}
	}

	private void ClientLoop(TcpClient client)
	{
		try
		{
			client.NoDelay = true;
			using StreamReader streamReader = new StreamReader(client.GetStream(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			while (_running)
			{
				string text = streamReader.ReadLine();
				if (text == null)
				{
					break;
				}
				text = text.Trim();
				if (text.Length != 0)
				{
					lock (_pending)
					{
						_pending.Enqueue(new Request
						{
							Client = client,
							Line = text
						});
					}
				}
			}
		}
		catch
		{
		}
		finally
		{
			try
			{
				client.Close();
			}
			catch
			{
			}
		}
	}

	private void Update()
	{
		while (true)
		{
			Request request;
			lock (_pending)
			{
				if (_pending.Count == 0)
				{
					break;
				}
				request = _pending.Dequeue();
			}
			StartCoroutine(Execute(request));
		}
	}

	private IEnumerator Execute(Request request)
	{
		Result result = new Result();
		string[] array = SplitArgs(request.Line);
		if (array.Length == 0)
		{
			Send(request.Client, result.Fail("empty"));
			yield break;
		}
		if (!_handlers.TryGetValue(array[0], out var value))
		{
			Send(request.Client, result.Fail("unknown command '" + array[0] + "' — พ\u0e34มพ\u0e4c help"));
			yield break;
		}
		IEnumerator routine = null;
		try
		{
			routine = value(array, result);
		}
		catch (Exception ex)
		{
			result.Fail(ex.GetType().Name + ": " + ex.Message);
		}
		if (routine != null)
		{
			while (true)
			{
				bool flag;
				try
				{
					flag = routine.MoveNext();
				}
				catch (Exception ex2)
				{
					result.Fail(ex2.GetType().Name + ": " + ex2.Message);
					break;
				}
				if (!flag)
				{
					break;
				}
				yield return routine.Current;
			}
		}
		Send(request.Client, result);
	}

	private static void Send(TcpClient client, Result result)
	{
		string text = Json(result);
		byte[] bytes = Encoding.UTF8.GetBytes(text + "\n");
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				client.GetStream().Write(bytes, 0, bytes.Length);
				client.GetStream().Flush();
			}
			catch
			{
			}
		});
	}

	private void Add(string name, string help, Handler handler)
	{
		_handlers[name] = handler;
		_help[name] = help;
	}

	private void RegisterHandlers()
	{
		Add("help", "รายการคำส\u0e31\u0e48ง", CmdHelp);
		Add("ping", "ตอบ pong + เวลาในเกม", CmdPing);
		Add("state", "สถานะรวม: ฉาก/ผ\u0e39\u0e49เล\u0e48น/ไทล\u0e4c/เกจ/เกาะ/guide event", CmdState);
		Add("gauges", "เกจเอาช\u0e35ว\u0e34ตรอดท\u0e31\u0e49งหมด (ค\u0e48า/ส\u0e39งส\u0e38ด/อ\u0e31ตรา)", CmdGauges);
		Add("effects", "สถานะต\u0e34ดต\u0e31วของผ\u0e39\u0e49เล\u0e48น", CmdEffects);
		Add("inv", "ไอเทมในกระเป\u0e4bา [filter]", CmdInventory);
		Add("use", "use <itemId|prototypeId|tag:xxx> — ใช\u0e49/ก\u0e34นไอเทม", CmdUse);
		Add("equip", "equip <itemId|prototypeId> — สวมใส\u0e48", CmdEquip);
		Add("drop", "drop <itemId|prototypeId> — ท\u0e34\u0e49งไอเทม", CmdDrop);
		Add("near", "near [prop|movable|all] [radius] — ว\u0e31ตถ\u0e38โต\u0e49ตอบได\u0e49รอบต\u0e31ว", CmdNear);
		Add("touch", "touch <entityId|entityType|name> [waitMs] — แตะว\u0e31ตถ\u0e38แล\u0e49วค\u0e37นเมน\u0e39โต\u0e49ตอบ", CmdTouch);
		Add("menu", "เมน\u0e39โต\u0e49ตอบของเป\u0e49าหมายป\u0e31จจ\u0e38บ\u0e31น", CmdMenu);
		Add("select", "select <Interaction|index> [id] — กดป\u0e38\u0e48มในเมน\u0e39โต\u0e49ตอบ", CmdSelect);
		Add("context", "ป\u0e38\u0e48มบร\u0e34บท (ด\u0e37\u0e48มน\u0e49ำ/ล\u0e49างต\u0e31ว/สแกน ฯลฯ) ท\u0e35\u0e48ใช\u0e49ได\u0e49ตอนน\u0e35\u0e49", CmdContext);
		Add("do", "do <Interaction> — กดป\u0e38\u0e48มบร\u0e34บท เช\u0e48น DrinkWater WashBody SearchWarphole", CmdDo);
		Add("move", "move <tileX> <tileY> [timeoutMs] — เด\u0e34นไปไทล\u0e4c (รอจนถ\u0e36ง)", CmdMove);
		Add("moveto", "moveto <entityId> [timeoutMs] — เด\u0e34นไปหาว\u0e31ตถ\u0e38", CmdMoveTo);
		Add("stop", "หย\u0e38ดเด\u0e34น", CmdStop);
		Add("triggers", "triggers — trigger ของ guide (PlayerTriggerGuide) ท\u0e35\u0e48โหลดอย\u0e39\u0e48 พร\u0e49อมกรอบ collider เป\u0e47นไทล\u0e4c", CmdTriggers);
		Add("npc", "npc — NPC ประจำเกาะท\u0e35\u0e48วางไว\u0e49 (ช\u0e37\u0e48อ/ไทล\u0e4c/คอมโพเนนต\u0e4c) · npc talk <index> — ส\u0e48งค\u0e38ย (InteractWithEpicNPC) · npc respawn", CmdNpc);
		Add("override", "override <ResourcesPath> — เส\u0e49นทางไฟล\u0e4cท\u0e31บ (game/override/…bytes) ท\u0e35\u0e48เกมจะใช\u0e49แทน Resources", CmdOverride);
		Add("nav", "nav — สถานะตารางเด\u0e34น · nav <x> <y> — ต\u0e49นท\u0e38นไทล\u0e4c · nav path <x> <y> — จ\u0e38ดแวะจากต\u0e31วละครไปไทล\u0e4c · nav off|on", CmdNav);
		Add("craft", "craft <recipeId> [qty] — คราฟต\u0e4cด\u0e49วยว\u0e31ตถ\u0e38ด\u0e34บท\u0e35\u0e48เล\u0e37อกอ\u0e31ตโนม\u0e31ต\u0e34", CmdCraft);
		Add("recipes", "recipes [filter] — ส\u0e39ตรท\u0e35\u0e48ร\u0e39\u0e49จ\u0e31ก", CmdRecipes);
		Add("guide", "guide — event/flow/todo ของ play guide · guide flow <name> · guide complete · guide reload", CmdGuide);
		Add("dialog", "dialog — บทพ\u0e39ดท\u0e35\u0e48แสดงอย\u0e39\u0e48 · dialog next [n] — กดข\u0e49าม · dialog quiz <index>", CmdDialog);
		Add("msgbox", "msgbox — กล\u0e48องข\u0e49อความท\u0e35\u0e48เป\u0e34ดอย\u0e39\u0e48 · msgbox ok|cancel", CmdMsgBox);
		Add("ui", "ui list [filter] · ui click <path|label> · ui open <TypeName> · ui close <TypeName>", CmdUi);
		Add("key", "key <InputCommand> [down|up|press] — ย\u0e34งคำส\u0e31\u0e48งค\u0e35ย\u0e4cล\u0e31ดของเกม (เช\u0e48น HelperButtonAction, OpenInventory) โดยไม\u0e48ต\u0e49องกดค\u0e35ย\u0e4cจร\u0e34ง", CmdKey);
		Add("keys", "keys [filter] — รายช\u0e37\u0e48อ InputCommand ท\u0e31\u0e49งหมด", CmdKeys);
		Add("emote", "emote <emoticonId> — อ\u0e35โมต\u0e34คอน เช\u0e48น smile", CmdEmote);
		Add("motion", "motion <motionId> — ท\u0e48าทาง", CmdMotion);
		Add("cheat", "cheat <text> — ส\u0e48ง Cheat ไปเซ\u0e34ร\u0e4cฟ (เช\u0e48น m 96 94 = วาร\u0e4cป)", CmdCheat);
		Add("boat", "boat — สถานะแพหน\u0e35เกาะ · boat put — ใส\u0e48ว\u0e31ตถ\u0e38ด\u0e34บท\u0e35\u0e48ม\u0e35ท\u0e31\u0e49งหมด · boat depart", CmdBoat);
		Add("screenshot", "screenshot [path] — บ\u0e31นท\u0e36กภาพหน\u0e49าจอ", CmdScreenshot);
		Add("log", "log [n] — ข\u0e49อความระบบ/ประกาศ/แชท/ข\u0e49อผ\u0e34ดพลาดล\u0e48าส\u0e38ด", CmdLog);
		Add("wait", "wait <ms>", CmdWait);
	}

	private IEnumerator CmdHelp(string[] args, Result r)
	{
		List<object> list = new List<object>();
		foreach (KeyValuePair<string, string> item in _help)
		{
			list.Add(item.Key + " — " + item.Value);
		}
		list.Sort((object a, object b) => string.CompareOrdinal((string)a, (string)b));
		r.Set("commands", list);
		yield break;
	}

	private IEnumerator CmdPing(string[] args, Result r)
	{
		r.Set("pong", true).Set("time", Time.time).Set("frame", Time.frameCount);
		yield break;
	}

	private static bool InWorld()
	{
		if (PlayerBehavior.LocalPlayer != null)
		{
			return !GameManager.IsTitleScene;
		}
		return false;
	}

	private static bool UiReady()
	{
		if (Singleton<UIManager>.HasInstance() && !GameManager.IsTitleScene && !GameManager.IsSceneClosing)
		{
			return PlayerBehavior.LocalPlayer != null;
		}
		return false;
	}

	private static Vector2 PlayerTile()
	{
		return Durango.Terrain.Util.ClientPositionToTilePosition(PlayerBehavior.LocalPlayer.CurrentPosition);
	}

	private IEnumerator CmdState(string[] args, Result r)
	{
		r.Set("scene", SceneManager.GetActiveScene().name);
		r.Set("title", GameManager.IsTitleScene);
		r.Set("loading", Singleton<UIManager>.HasInstance() && UIManager.IsLoadingCurtain);
		r.Set("uiReady", UiReady());
		r.Set("connected", Connections.Frontend != null && Connections.Frontend.Connected());
		r.Set("playerId", GameManager.PlayerId);
		if (GameManager.Region != null)
		{
			r.Set("region", Dict("id", GameManager.Region.Id, "name", GameManager.Region.Name, "template", GameManager.Region.TemplateId, "role", GameManager.Region.Role().ToString()));
		}
		if (InWorld())
		{
			PlayerBehavior localPlayer = PlayerBehavior.LocalPlayer;
			Vector2 vector = PlayerTile();
			r.Set("tile", Dict("x", (int)vector.x, "y", (int)vector.y));
			r.Set("pos", Dict("x", localPlayer.CurrentPosition.x, "y", localPlayer.CurrentPosition.y, "z", localPlayer.CurrentPosition.z));
			r.Set("alive", localPlayer.IsAlive);
			r.Set("moving", (bool)localPlayer.IsMoving);
			r.Set("tired", localPlayer.IsTired);
			r.Set("riding", localPlayer.IsRiding);
			r.Set("gauges", GaugeDict(localPlayer));
			r.Set("effects", EffectList());
			r.Set("guide", GuideSummary());
			r.Set("dialog", DialogInfo());
		}
		yield break;
	}

	private static Dictionary<string, object> GaugeDict(CharacterBehavior p)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		for (int i = 0; i < GaugeKeys.Length; i++)
		{
			Gauge gauge = p.GetGauge(GaugeKeys[i]);
			if (gauge != null)
			{
				dictionary[GaugeKeys[i]] = Dict("value", gauge.Get(), "max", gauge.Max(), "velocity", gauge.Velocity(Connections.Frontend.GetBufferedServerTime() + 1.0));
			}
		}
		return dictionary;
	}

	private IEnumerator CmdGauges(string[] args, Result r)
	{
		if (!InWorld())
		{
			r.Fail("not in world");
		}
		else
		{
			r.Set("gauges", GaugeDict(PlayerBehavior.LocalPlayer));
		}
		yield break;
	}

	private static List<object> EffectList()
	{
		List<object> list = new List<object>();
		Durango.Logic.StatusEffects statusEffects = GameSystem<StatusEffectSystem>.Instance().GetStatusEffects();
		if (statusEffects != null)
		{
			foreach (Durango.Logic.StatusEffect item in statusEffects.List)
			{
				list.Add(Dict("id", item.Id, "level", item.Level, "name", item.Name, "until", item.Until));
			}
		}
		return list;
	}

	private IEnumerator CmdEffects(string[] args, Result r)
	{
		r.Set("effects", EffectList());
		yield break;
	}

	private static Dictionary<string, object> ItemDict(ItemData item)
	{
		List<object> list = new List<object>();
		foreach (TagData tag in item.Tags)
		{
			list.Add(tag.Id);
		}
		return Dict("id", item.Id, "prototype", item.PrototypeId, "name", item.Name, "level", item.Level, "equipment", item.IsEquipments, "size", item.Size, "tags", list);
	}

	private IEnumerator CmdInventory(string[] args, Result r)
	{
		string text = ((args.Length > 1) ? args[1] : null);
		List<object> list = new List<object>();
		foreach (ItemData playerItem in GameSystem<InventorySystem>.Instance().PlayerItemList)
		{
			if (text == null || Matches(playerItem, text))
			{
				list.Add(ItemDict(playerItem));
			}
		}
		r.Set("items", list).Set("count", list.Count);
		yield break;
	}

	private static bool Matches(ItemData item, string filter)
	{
		if (filter.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
		{
			return item.HasTag(filter.Substring(4));
		}
		if (!string.Equals(item.Id, filter, StringComparison.OrdinalIgnoreCase) && !string.Equals(item.PrototypeId, filter, StringComparison.OrdinalIgnoreCase))
		{
			if (item.Name != null)
			{
				return item.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
			}
			return false;
		}
		return true;
	}

	private static ItemData FindItem(string filter)
	{
		foreach (ItemData playerItem in GameSystem<InventorySystem>.Instance().PlayerItemList)
		{
			if (Matches(playerItem, filter))
			{
				return playerItem;
			}
		}
		return null;
	}

	private IEnumerator CmdUse(string[] args, Result r)
	{
		if (args.Length < 2)
		{
			r.Fail("use <item>");
			yield break;
		}
		ItemData item = FindItem(args[1]);
		if (item == null)
		{
			r.Fail("ไม\u0e48พบไอเทม " + args[1]);
			yield break;
		}
		bool done = false;
		GameSystem<InventorySystem>.Instance().UseItem(item, playerAccepted: true, delegate
		{
			done = true;
		});
		float until = Time.time + 5f;
		while (!done && Time.time < until)
		{
			yield return null;
		}
		r.Set("item", ItemDict(item)).Set("succeeded", done);
	}

	private IEnumerator CmdEquip(string[] args, Result r)
	{
		if (args.Length < 2)
		{
			r.Fail("equip <item>");
			yield break;
		}
		ItemData item = FindItem(args[1]);
		if (item == null)
		{
			r.Fail("ไม\u0e48พบไอเทม " + args[1]);
			yield break;
		}
		bool replied = false;
		GameSystem<EquipSystem>.Instance().EquipItem(item);
		float until = Time.time + 3f;
		while (!replied && Time.time < until)
		{
			replied = item.IsEquipments;
			yield return null;
		}
		r.Set("item", ItemDict(item));
	}

	private IEnumerator CmdDrop(string[] args, Result r)
	{
		if (args.Length < 2)
		{
			r.Fail("drop <item>");
			yield break;
		}
		ItemData item = FindItem(args[1]);
		if (item == null)
		{
			r.Fail("ไม\u0e48พบไอเทม " + args[1]);
			yield break;
		}
		InventorySystem.DropItems(InventorySystem.MakeDumpItemsPacket(GameSystem<InventorySystem>.Instance().PlayerInventory, new string[1] { item.Id }));
		yield return new WaitForSeconds(0.5f);
		r.Set("dropped", item.Id);
	}

	private static Dictionary<string, object> ObjectDict(GameObject go)
	{
		Vector2 vector = Durango.Terrain.Util.ClientPositionToTilePosition(go.transform.position);
		string text = go.name;
		ImmovableBase component = go.GetComponent<ImmovableBase>();
		if (component != null)
		{
			text = component.GetName();
		}
		SelectableObject component2 = go.GetComponent<SelectableObject>();
		if (component2 != null)
		{
			text = component2.GetName();
		}
		return Dict("entityId", ObjectIdentifier.GetEntityId(go), "entityType", ObjectIdentifier.GetEntityType(go), "name", text, "object", go.name, "tile", Dict("x", (int)vector.x, "y", (int)vector.y), "distance", Vector3.Distance(go.transform.position, PlayerBehavior.LocalPlayer.CurrentPosition));
	}

	private static List<GameObject> SearchNear(string kind, float radius)
	{
		List<GameObject> list = new List<GameObject>();
		if (kind == "prop" || kind == "all")
		{
			InteractionSystem.GetNearObjectsInternal(list, LayerHelper.PropMask, radius, InteractionSystem.PropInteractionObjectFilter);
		}
		if (kind == "movable" || kind == "all")
		{
			InteractionSystem.GetNearObjectsInternal(list, LayerHelper.InteractionMask, radius, InteractionSystem.MovableInteractionObjectFilter);
		}
		return list;
	}

	private IEnumerator CmdNear(string[] args, Result r)
	{
		if (!InWorld())
		{
			r.Fail("not in world");
			yield break;
		}
		string kind = ((args.Length > 1) ? args[1].ToLowerInvariant() : "all");
		float radius = ((args.Length > 2) ? ParseFloat(args[2], 1600f) : 1600f);
		List<object> list = new List<object>();
		foreach (GameObject item in SearchNear(kind, radius))
		{
			list.Add(ObjectDict(item));
		}
		list.Sort((object a, object b) => ((float)((Dictionary<string, object>)a)["distance"]).CompareTo((float)((Dictionary<string, object>)b)["distance"]));
		r.Set("objects", list).Set("count", list.Count);
	}

	private static GameObject FindObject(string key, float radius)
	{
		GameObject result = null;
		float num = float.MaxValue;
		int result2;
		bool flag = int.TryParse(key, out result2);
		foreach (GameObject item in SearchNear("all", radius))
		{
			bool flag2 = string.Equals(ObjectIdentifier.GetEntityId(item), key, StringComparison.OrdinalIgnoreCase) || (flag && ObjectIdentifier.GetEntityType(item) == result2);
			if (!flag2)
			{
				ImmovableBase component = item.GetComponent<ImmovableBase>();
				SelectableObject component2 = item.GetComponent<SelectableObject>();
				string text = ((component != null) ? component.GetName() : ((component2 != null) ? component2.GetName() : item.name));
				flag2 = (text != null && text.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) || item.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
			}
			if (flag2)
			{
				float num2 = Vector3.Distance(item.transform.position, PlayerBehavior.LocalPlayer.CurrentPosition);
				if (num2 < num)
				{
					num = num2;
					result = item;
				}
			}
		}
		return result;
	}

	private static List<object> MenuList()
	{
		List<object> list = new List<object>();
		InteractionMenuList menuList = GameSystem<InteractionSystem>.Instance().MenuList;
		for (int i = 0; i < menuList.Count; i++)
		{
			InteractionMenuData interactionMenuData = menuList[i];
			list.Add(Dict("index", i, "action", interactionMenuData.Action.ToString(), "id", interactionMenuData.Id, "name", interactionMenuData.Name, "count", interactionMenuData.Count, "disabled", interactionMenuData.Disabled, "denied", interactionMenuData.AccessDenied));
		}
		return list;
	}

	private IEnumerator CmdTouch(string[] args, Result r)
	{
		if (args.Length < 2)
		{
			r.Fail("touch <entityId|entityType|name>");
			yield break;
		}
		GameObject go = FindObject(args[1], 4000f);
		if (go == null)
		{
			r.Fail("ไม\u0e48พบว\u0e31ตถ\u0e38 " + args[1] + " ในระยะ");
			yield break;
		}
		InteractionSystem interactionSystem = GameSystem<InteractionSystem>.Instance();
		bool updated = false;
		Action onUpdated = delegate
		{
			updated = true;
		};
		interactionSystem.MenuList.Updated += onUpdated;
		interactionSystem.SetInteractionTarget(new InteractionObject(go));
		float num = ((args.Length > 2) ? (ParseFloat(args[2], 2000f) / 1000f) : 2f);
		float until = Time.time + num;
		while (!updated && Time.time < until)
		{
			yield return null;
		}
		yield return null;
		interactionSystem.MenuList.Updated -= onUpdated;
		r.Set("target", ObjectDict(go)).Set("menu", MenuList()).Set("menuName", interactionSystem.MenuList.Name);
	}

	private IEnumerator CmdMenu(string[] args, Result r)
	{
		InteractionSystem interactionSystem = GameSystem<InteractionSystem>.Instance();
		InteractionObject target = interactionSystem.Target;
		if (target != null && target.Target != null)
		{
			r.Set("target", ObjectDict(target.Target));
		}
		r.Set("menu", MenuList()).Set("menuName", interactionSystem.MenuList.Name);
		yield break;
	}

	private IEnumerator CmdSelect(string[] args, Result r)
	{
		if (args.Length < 2)
		{
			r.Fail("select <Interaction|index> [id]");
			yield break;
		}
		InteractionSystem interactionSystem = GameSystem<InteractionSystem>.Instance();
		InteractionMenuList menuList = interactionSystem.MenuList;
		if (!int.TryParse(args[1], out var result))
		{
			result = -1;
			for (int i = 0; i < menuList.Count; i++)
			{
				InteractionMenuData interactionMenuData = menuList[i];
				bool num = string.Equals(interactionMenuData.Action.ToString(), args[1], StringComparison.OrdinalIgnoreCase);
				bool flag = interactionMenuData.Name != null && interactionMenuData.Name.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) >= 0;
				bool flag2 = args.Length < 3 || string.Equals(interactionMenuData.Id, args[2], StringComparison.OrdinalIgnoreCase);
				if ((num || flag) && flag2)
				{
					result = i;
					break;
				}
			}
		}
		if (result < 0 || result >= menuList.Count)
		{
			r.Fail("ไม\u0e48ม\u0e35เมน\u0e39 " + args[1]).Set("menu", MenuList());
			yield break;
		}
		InteractionMenuData menu = menuList[result];
		interactionSystem.SelectTargetInteractionMenu(menu);
		yield return new WaitForSeconds(0.3f);
		r.Set("selected", Dict("action", menu.Action.ToString(), "id", menu.Id, "name", menu.Name));
	}

	private static List<InteractionMenuData> ContextActions()
	{
		List<InteractionMenuData> result = new List<InteractionMenuData>();
		GameSystem<InteractionSystem>.Instance().GetContextActionList(result);
		return result;
	}

	private IEnumerator CmdContext(string[] args, Result r)
	{
		List<object> list = new List<object>();
		foreach (InteractionMenuData item in ContextActions())
		{
			list.Add(Dict("action", item.Action.ToString(), "id", item.Id, "name", item.Name, "disabled", item.Disabled));
		}
		r.Set("actions", list);
		yield break;
	}

	private IEnumerator CmdDo(string[] args, Result r)
	{
		if (args.Length < 2)
		{
			r.Fail("do <Interaction>");
			yield break;
		}
		InteractionMenuData? found = null;
		foreach (InteractionMenuData item in ContextActions())
		{
			if (string.Equals(item.Action.ToString(), args[1], StringComparison.OrdinalIgnoreCase) || (item.Name != null && item.Name.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) >= 0))
			{
				found = item;
				break;
			}
		}
		if (!found.HasValue)
		{
			Interaction action;
			try
			{
				action = (Interaction)Enum.Parse(typeof(Interaction), args[1], ignoreCase: true);
			}
			catch
			{
				r.Fail("ไม\u0e48ร\u0e39\u0e49จ\u0e31ก Interaction " + args[1]);
				yield break;
			}
			found = new InteractionMenuData(action);
		}
		GameSystem<InteractionSystem>.Instance().DoNoneTargetAction(found.Value);
		yield return new WaitForSeconds(0.3f);
		r.Set("action", found.Value.Action.ToString());
	}

	private IEnumerator MoveAndWait(Vector3 position, GameObject target, float timeout, Result r)
	{
		bool done = false;
		Action onComplete = delegate
		{
			done = true;
		};
		Singleton<PlayerController>.Instance().TestBridgeMoveTo(position, target, onComplete);
		float until = Time.time + timeout;
		while (!done && Time.time < until)
		{
			yield return null;
		}
		Vector2 vector = PlayerTile();
		r.Set("arrived", done).Set("tile", Dict("x", (int)vector.x, "y", (int)vector.y));
		if (!done)
		{
			r.Fail("timeout — ย\u0e31งไม\u0e48ถ\u0e36ง (" + (int)vector.x + "," + (int)vector.y + ")");
		}
	}

	private IEnumerator CmdMove(string[] args, Result r)
	{
		if (!InWorld() || args.Length < 3)
		{
			r.Fail("move <tileX> <tileY> [timeoutMs]");
			yield break;
		}
		float x = ParseFloat(args[1], 0f);
		float y = ParseFloat(args[2], 0f);
		float timeout = ((args.Length > 3) ? (ParseFloat(args[3], 60000f) / 1000f) : 60f);
		Vector3 position = Durango.Terrain.Util.TilePositionToClientPosition(new Vector2(x, y), tileCenter: true);
		IEnumerator routine = MoveAndWait(position, null, timeout, r);
		while (routine.MoveNext())
		{
			yield return routine.Current;
		}
	}

	private IEnumerator CmdMoveTo(string[] args, Result r)
	{
		if (!InWorld() || args.Length < 2)
		{
			r.Fail("moveto <entityId|name> [timeoutMs]");
			yield break;
		}
		GameObject go = FindObject(args[1], 6000f);
		if (go == null)
		{
			r.Fail("ไม\u0e48พบว\u0e31ตถ\u0e38 " + args[1]);
			yield break;
		}
		float timeout = ((args.Length > 2) ? (ParseFloat(args[2], 60000f) / 1000f) : 60f);
		IEnumerator routine = MoveAndWait(Vector3.zero, go, timeout, r);
		while (routine.MoveNext())
		{
			yield return routine.Current;
		}
		r.Set("target", ObjectDict(go));
	}

	private IEnumerator CmdNav(string[] args, Result r)
	{
		NavGrid.EnsureLoaded();
		r.Set("ready", NavGrid.Ready).Set("size", NavGrid.Size).Set("error", NavGrid.LastError)
			.Set("routing", LocalMoveNavigator.RoutingEnabled);
		if (args.Length >= 2 && (args[1] == "off" || args[1] == "on"))
		{
			LocalMoveNavigator.RoutingEnabled = args[1] == "on";
			r.Set("routing", LocalMoveNavigator.RoutingEnabled);
		}
		else if (args.Length >= 4 && args[1] == "path")
		{
			if (!InWorld())
			{
				r.Fail("not in world");
				yield break;
			}
			Vector3 toClient = Durango.Terrain.Util.TilePositionToClientPosition(new Vector2(ParseFloat(args[2], 0f), ParseFloat(args[3], 0f)), tileCenter: true);
			List<Vector3> list = NavGrid.FindWaypoints(PlayerBehavior.LocalPlayer.CurrentPosition, toClient);
			List<object> list2 = new List<object>();
			if (list != null)
			{
				foreach (Vector3 item in list)
				{
					Vector2 vector = Durango.Terrain.Util.ClientPositionToTilePosition(item);
					list2.Add((int)vector.x + "," + (int)vector.y);
				}
			}
			List<object> list3 = new List<object>();
			if (NavGrid.LastPathTiles != null)
			{
				foreach (Vector2 lastPathTile in NavGrid.LastPathTiles)
				{
					list3.Add((int)lastPathTile.x + "," + (int)lastPathTile.y);
				}
			}
			r.Set("found", list != null).Set("waypoints", list2).Set("tiles", list3);
		}
		else if (args.Length >= 3)
		{
			int num = (int)ParseFloat(args[1], 0f);
			int num2 = (int)ParseFloat(args[2], 0f);
			r.Set("tile", Dict("x", num, "y", num2)).Set("cost", NavGrid.CostAt(num, num2));
		}
	}

	private IEnumerator CmdNpc(string[] args, Result r)
	{
		if (args.Length >= 2 && args[1] == "respawn")
		{
			NpcSpawner.SpawnAll();
			r.Set("respawned", true);
			yield break;
		}
		if (args.Length >= 3 && args[1] == "clips")
		{
			int num = (int)ParseFloat(args[2], 0f);
			List<GameObject> instances = NpcSpawner.Instances;
			if (num < 0 || num >= instances.Count || instances[num] == null)
			{
				r.Set("error", "ไม\u0e48ม\u0e35 NPC ลำด\u0e31บ " + num);
				yield break;
			}
			CharacterBehavior component = instances[num].GetComponent<CharacterBehavior>();
			List<object> list = new List<object>();
			if (component != null && component.Anim != null)
			{
				foreach (AnimationState item in component.Anim)
				{
					list.Add(item.name);
				}
			}
			r.Set("object", instances[num].name).Set("clips", list).Set("count", list.Count);
			yield break;
		}
		if (args.Length >= 3 && args[1] == "info")
		{
			int num2 = (int)ParseFloat(args[2], 0f);
			List<GameObject> instances2 = NpcSpawner.Instances;
			if (num2 < 0 || num2 >= instances2.Count || instances2[num2] == null)
			{
				r.Set("error", "ไม\u0e48ม\u0e35 NPC ลำด\u0e31บ " + num2);
				yield break;
			}
			GameObject gameObject = instances2[num2];
			CharacterBehavior component2 = gameObject.GetComponent<CharacterBehavior>();
			List<object> list2 = new List<object>();
			Renderer[] componentsInChildren = gameObject.GetComponentsInChildren<Renderer>(includeInactive: true);
			foreach (Renderer renderer in componentsInChildren)
			{
				list2.Add(Dict("name", renderer.gameObject.name, "enabled", renderer.enabled, "active", renderer.gameObject.activeInHierarchy, "layer", renderer.gameObject.layer, "visible", renderer.isVisible, "bounds", renderer.bounds.center.ToString("F0") + " " + renderer.bounds.size.ToString("F0")));
			}
			Vector3 position = gameObject.transform.position;
			float? worldHeight = LocalMoveOperator.GetWorldHeight(position, 0, 0f);
			List<object> list3 = new List<object>();
			Transform[] componentsInChildren2 = gameObject.GetComponentsInChildren<Transform>(includeInactive: true);
			foreach (Transform transform in componentsInChildren2)
			{
				int num3 = 0;
				Transform transform2 = transform;
				while (transform2 != gameObject.transform && transform2 != null)
				{
					num3++;
					transform2 = transform2.parent;
				}
				if (num3 <= 2)
				{
					list3.Add(num3 + ":" + transform.name + " " + transform.position.ToString("F0") + ((transform.GetComponent<Animation>() != null) ? " [Animation]" : ""));
				}
			}
			Animation animation = ((component2 != null) ? component2.Anim : null);
			r.Set("object", gameObject.name).Set("active", gameObject.activeInHierarchy).Set("pos", position.ToString("F0"))
				.Set("scale", gameObject.transform.lossyScale.ToString("F2"))
				.Set("current", (component2 != null) ? component2.CurrentPosition.ToString("F0") : "")
				.Set("ground", worldHeight.HasValue ? worldHeight.Value.ToString("F0") : "null")
				.Set("animOn", (animation != null) ? animation.gameObject.name : "null")
				.Set("animClip", (animation != null && animation.clip != null) ? animation.clip.name : "null")
				.Set("tree", list3)
				.Set("renderers", list2);
			yield break;
		}
		if (args.Length >= 4 && args[1] == "act")
		{
			int num4 = (int)ParseFloat(args[2], 0f);
			List<GameObject> instances3 = NpcSpawner.Instances;
			if (num4 < 0 || num4 >= instances3.Count || instances3[num4] == null)
			{
				r.Set("error", "ไม\u0e48ม\u0e35 NPC ลำด\u0e31บ " + num4);
				yield break;
			}
			NpcWanderAI component3 = instances3[num4].GetComponent<NpcWanderAI>();
			if (component3 == null)
			{
				r.Set("error", "ไม\u0e48ม\u0e35 NpcWanderAI");
			}
			else
			{
				r.Set("played", component3.ForceActivity(args[3]));
			}
			yield break;
		}
		if (args.Length >= 3 && args[1] == "talk")
		{
			int num5 = (int)ParseFloat(args[2], 0f);
			Connections.Frontend.Send(new InteractWithEpicNPC
			{
				Npc = (EpicNPCType)num5,
				ItemIds = new string[0]
			});
			r.Set("sent", num5);
			yield break;
		}
		List<object> list4 = new List<object>();
		foreach (GameObject instance in NpcSpawner.Instances)
		{
			if (!(instance == null))
			{
				Vector2 vector = Durango.Terrain.Util.ClientPositionToTilePosition(instance.transform.position);
				List<object> list5 = new List<object>();
				Component[] components = instance.GetComponents<Component>();
				foreach (Component component4 in components)
				{
					list5.Add(component4.GetType().Name);
				}
				AnimalBehavior component5 = instance.GetComponent<AnimalBehavior>();
				string text = ((component5 != null && component5.CurAnimState != null) ? component5.CurAnimState.name : "");
				list4.Add(Dict("object", instance.name, "tile", (int)vector.x + "," + (int)vector.y, "clip", text, "components", list5));
			}
		}
		r.Set("npcs", list4).Set("count", list4.Count);
	}

	private IEnumerator CmdOverride(string[] args, Result r)
	{
		string text = ((args.Length >= 2) ? args[1] : "PlayGuide/tutorial_play_guide_flow");
		string value = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "override");
		r.Set("dataPath", Application.dataPath).Set("overrideDir", value).Set("file", text)
			.Set("found", Durango.Utils.Json.OverridePath(text));
		yield break;
	}

	private IEnumerator CmdTriggers(string[] args, Result r)
	{
		List<object> list = new List<object>();
		PlayerTriggerGuide[] array = UnityEngine.Object.FindObjectsOfType<PlayerTriggerGuide>();
		foreach (PlayerTriggerGuide playerTriggerGuide in array)
		{
			Collider component = playerTriggerGuide.GetComponent<Collider>();
			Dictionary<string, object> dictionary = Dict("object", playerTriggerGuide.gameObject.name, "flow", playerTriggerGuide.TestBridgeFlowName());
			Vector2 vector = Durango.Terrain.Util.ClientPositionToTilePosition(playerTriggerGuide.transform.position);
			dictionary["tile"] = (int)vector.x + "," + (int)vector.y;
			if (component != null)
			{
				Bounds bounds = component.bounds;
				Vector2 vector2 = Durango.Terrain.Util.ClientPositionToTilePosition(bounds.min);
				Vector2 vector3 = Durango.Terrain.Util.ClientPositionToTilePosition(bounds.max);
				dictionary["bounds"] = (int)vector2.x + "," + (int)vector2.y + " - " + (int)vector3.x + "," + (int)vector3.y;
				dictionary["sizeTiles"] = (bounds.size.x / 200f).ToString("0.0") + "x" + (bounds.size.z / 200f).ToString("0.0");
			}
			list.Add(dictionary);
		}
		r.Set("triggers", list).Set("count", list.Count);
		yield break;
	}

	private IEnumerator CmdStop(string[] args, Result r)
	{
		Singleton<PlayerController>.Instance().StopMove();
		yield break;
	}

	private IEnumerator CmdRecipes(string[] args, Result r)
	{
		string text = ((args.Length > 1) ? args[1] : null);
		List<object> list = new List<object>();
		foreach (Category category in GameSystem<RecipeSystem>.Instance().RecipeContainer.Categories)
		{
			foreach (Recipe recipe in category.Recipes)
			{
				if (recipe != null && (text == null || recipe.Id.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || (recipe.Name != null && recipe.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)))
				{
					list.Add(Dict("id", recipe.Id, "name", recipe.Name, "category", category.Id));
				}
			}
		}
		r.Set("recipes", list).Set("count", list.Count);
		yield break;
	}

	private IEnumerator CmdCraft(string[] args, Result r)
	{
		if (args.Length < 2)
		{
			r.Fail("craft <recipeId> [qty]");
			yield break;
		}
		Recipe recipe = GameSystem<RecipeSystem>.Instance().RecipeContainer.GetRecipe(args[1]);
		if (recipe == null)
		{
			r.Fail("ไม\u0e48ร\u0e39\u0e49จ\u0e31กส\u0e39ตร " + args[1]);
			yield break;
		}
		CraftSystem craftSystem = GameSystem<CraftSystem>.Instance();
		CraftSlotContainer slotContainer = craftSystem.SlotContainer;
		slotContainer.Set(recipe, null, GameSystem<InventorySystem>.Instance().PlayerInventory, null);
		slotContainer.QuickFill();
		int b = ((args.Length <= 2) ? 1 : ((int)ParseFloat(args[2], 1f)));
		slotContainer.SetQuantity(Mathf.Max(1, b));
		List<object> materials = new List<object>();
		foreach (ItemData selectedMaterial in slotContainer.GetSelectedMaterials())
		{
			materials.Add(ItemDict(selectedMaterial));
		}
		bool finished = false;
		string outcome = null;
		Action<string, Crafted> onSucceed = delegate
		{
			finished = true;
			outcome = "succeed";
		};
		Action<string, ActionInfo> onFailed = delegate
		{
			finished = true;
			outcome = "failed";
		};
		craftSystem.CraftSucceed += onSucceed;
		craftSystem.CraftFailed += onFailed;
		craftSystem.Craft();
		float until = Time.time + 30f;
		while (!finished && Time.time < until)
		{
			yield return null;
		}
		craftSystem.CraftSucceed -= onSucceed;
		craftSystem.CraftFailed -= onFailed;
		r.Set("recipe", recipe.Id).Set("materials", materials).Set("result", outcome ?? "timeout");
		if (outcome != "succeed")
		{
			r.Ok = false;
		}
	}

	private static Dictionary<string, object> GuideSummary()
	{
		PlayGuideSystem playGuideSystem = GameSystem<PlayGuideSystem>.Instance();
		Dictionary<string, object> dictionary = Dict("begun", playGuideSystem.IsGuideBegin, "event", playGuideSystem.CurrentEventName);
		List<object> list = new List<object>();
		foreach (string item in playGuideSystem.ActiveFlowsInfo())
		{
			list.Add(item);
		}
		dictionary["flows"] = list;
		List<object> list2 = new List<object>();
		ToDoListSystem toDoListSystem = GameSystem<ToDoListSystem>.Instance();
		for (int i = 0; i < toDoListSystem.CollectionCount; i++)
		{
			ToDoCollection collection = toDoListSystem.GetCollection(i);
			foreach (ToDoBase toDo in collection.ToDoList)
			{
				object obj = null;
				if (collection is MissionToDoCollection missionToDoCollection)
				{
					Durango.Logic.Faction.MissionToDo currentToDo = missionToDoCollection.GetCurrentToDo();
					obj = ((currentToDo == null) ? "null" : currentToDo.DisplayText);
				}
				list2.Add(Dict("key", toDo.Key, "text", StripTags(toDo.LocalText), "progress", toDo.CurrentProgress, "target", toDo.TargetProgress, "completed", toDo.IsCompleted, "title", collection.Title, "event", (collection.GuideEvent != null) ? collection.GuideEvent.Name : null, "disabled", collection.IsDisabled, "order", (toDo is Durango.Logic.Faction.MissionToDo missionToDo) ? missionToDo.MissionOrder.ToString() : null, "current", obj, "regionId", GameManager.Region.Id));
			}
		}
		dictionary["todos"] = list2;
		return dictionary;
	}

	private IEnumerator CmdGuide(string[] args, Result r)
	{
		PlayGuideSystem playGuideSystem = GameSystem<PlayGuideSystem>.Instance();
		if (args.Length >= 3 && args[1] == "flow")
		{
			playGuideSystem.BeginFlow(args[2]);
			yield return null;
			r.Set("began", args[2]);
		}
		else if (args.Length >= 2 && args[1] == "complete")
		{
			playGuideSystem.CompleteCurrentEvent();
			yield return null;
		}
		else if (args.Length >= 2 && args[1] == "reload")
		{
			playGuideSystem.ReloadAll();
			yield return null;
		}
		r.Set("guide", GuideSummary());
	}

	private static DialogueGroupBase Dialogue()
	{
		if (!UiReady())
		{
			return null;
		}
		return UIManager.FindScript<DialogueGroupBase>();
	}

	private static object DialogInfo()
	{
		DialogueGroupBase dialogueGroupBase = Dialogue();
		if (dialogueGroupBase == null)
		{
			return null;
		}
		Dictionary<string, object> dictionary = dialogueGroupBase.TestBridgeInfo();
		GuideTooltip guideTooltip = ((UIManager.Popup != null) ? UIManager.Popup.Tooltip<GuideTooltip>() : null);
		dictionary["tooltip"] = guideTooltip != null && guideTooltip.IsVisible;
		return dictionary;
	}

	private IEnumerator CmdDialog(string[] args, Result r)
	{
		DialogueGroupBase dialogue = Dialogue();
		if (dialogue == null)
		{
			r.Fail("ไม\u0e48ม\u0e35 DialogueGroup");
			yield break;
		}
		if (args.Length >= 2 && args[1] == "next")
		{
			int n = ((args.Length <= 2) ? 1 : ((int)ParseFloat(args[2], 1f)));
			int advanced = 0;
			for (int i = 0; i < n; i++)
			{
				if (!dialogue.IsOpened || dialogue.TestBridgeCurrentType() == null)
				{
					GuideTooltip guideTooltip = ((UIManager.Popup != null) ? UIManager.Popup.Tooltip<GuideTooltip>() : null);
					if (!(guideTooltip != null) || !guideTooltip.IsVisible)
					{
						break;
					}
					guideTooltip.Hide();
					advanced++;
					yield return new WaitForSeconds(0.3f);
				}
				else
				{
					dialogue.TestBridgePress(pressed: true);
					yield return null;
					yield return null;
					dialogue.TestBridgePress(pressed: false);
					advanced++;
					yield return new WaitForSeconds(0.15f);
				}
			}
			r.Set("advanced", advanced);
		}
		else if (args.Length >= 3 && args[1] == "quiz")
		{
			int index = (int)ParseFloat(args[2], 0f);
			if (!dialogue.TestBridgeAnswerQuiz(index))
			{
				r.Fail("ไม\u0e48ม\u0e35ต\u0e31วเล\u0e37อก " + index);
			}
			yield return new WaitForSeconds(0.2f);
		}
		r.Set("open", dialogue.IsOpened).Set("dialog", DialogInfo());
	}

	private IEnumerator CmdMsgBox(string[] args, Result r)
	{
		MessageBox messageBox = (UiReady() ? UIManager.MessageBox : null);
		if (messageBox == null)
		{
			r.Fail("ไม\u0e48ม\u0e35 MessageBox");
			yield break;
		}
		if (args.Length >= 2)
		{
			bool ok = args[1].Equals("ok", StringComparison.OrdinalIgnoreCase) || args[1].Equals("yes", StringComparison.OrdinalIgnoreCase);
			if (!messageBox.TestBridgeChoose(ok))
			{
				r.Fail("MessageBox ไม\u0e48ได\u0e49เป\u0e34ดอย\u0e39\u0e48");
			}
			yield return null;
		}
		r.Set("open", messageBox.IsShow).Set("text", messageBox.TestBridgeText());
	}

	private static string UiPath(Transform t)
	{
		string text = t.name;
		while (t.parent != null)
		{
			t = t.parent;
			text = t.name + "/" + text;
		}
		return text;
	}

	private static string UiLabel(GameObject go)
	{
		UILabel componentInChildren = go.GetComponentInChildren<UILabel>();
		if (!(componentInChildren != null))
		{
			return string.Empty;
		}
		return StripTags(componentInChildren.text);
	}

	private IEnumerator CmdUi(string[] args, Result r)
	{
		string text = ((args.Length > 1) ? args[1].ToLowerInvariant() : "list");
		if (text == "list")
		{
			string text2 = ((args.Length > 2) ? args[2] : null);
			List<object> list = new List<object>();
			UIButton[] array = UnityEngine.Object.FindObjectsOfType<UIButton>();
			foreach (UIButton uIButton in array)
			{
				if (uIButton.gameObject.activeInHierarchy)
				{
					string text3 = UiPath(uIButton.transform);
					string text4 = UiLabel(uIButton.gameObject);
					if (text2 == null || text3.IndexOf(text2, StringComparison.OrdinalIgnoreCase) >= 0 || text4.IndexOf(text2, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						list.Add(Dict("path", text3, "label", text4, "enabled", uIButton.isEnabled));
					}
				}
			}
			Selectable[] array2 = UnityEngine.Object.FindObjectsOfType<Selectable>();
			foreach (Selectable selectable in array2)
			{
				if (selectable.gameObject.activeInHierarchy && !(selectable.GetComponent<UIButton>() != null))
				{
					string text5 = UiPath(selectable.transform);
					string text6 = UiLabel(selectable.gameObject);
					if (text2 == null || text5.IndexOf(text2, StringComparison.OrdinalIgnoreCase) >= 0 || text6.IndexOf(text2, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						list.Add(Dict("path", text5, "label", text6, "enabled", true, "selectable", true));
					}
				}
			}
			r.Set("buttons", list).Set("count", list.Count);
		}
		else if (text == "click" && args.Length > 2)
		{
			GameObject target = FindUi(args[2]);
			if (target == null)
			{
				r.Fail("ไม\u0e48พบป\u0e38\u0e48ม " + args[2]);
				yield break;
			}
			Selectable component = target.GetComponent<Selectable>();
			if (component != null && component.Clicked != null)
			{
				Selectable.Current = component;
				try
				{
					component.Clicked();
				}
				finally
				{
					Selectable.Current = null;
				}
			}
			else
			{
				UICamera.Notify(target, "OnClick", null);
			}
			yield return null;
			r.Set("clicked", UiPath(target.transform));
		}
		else if ((text == "open" || text == "close") && args.Length > 2)
		{
			UIBase ui = FindUiScript(args[2]);
			if (ui == null)
			{
				r.Fail("ไม\u0e48พบ UI " + args[2]);
				yield break;
			}
			bool result = ((text == "open") ? ui.Open() : ui.Close());
			yield return null;
			r.Set("type", ui.GetType().Name).Set("result", result).Set("opened", ui.IsOpened);
		}
		else
		{
			r.Fail("ui list [filter] · ui click <path|label> · ui open|close <TypeName>");
		}
	}

	private static GameObject FindUi(string key)
	{
		GameObject best = null;
		UIButton[] array = UnityEngine.Object.FindObjectsOfType<UIButton>();
		foreach (UIButton uIButton in array)
		{
			if (uIButton.gameObject.activeInHierarchy && UiMatch(uIButton.gameObject, key, ref best))
			{
				return best;
			}
		}
		Selectable[] array2 = UnityEngine.Object.FindObjectsOfType<Selectable>();
		foreach (Selectable selectable in array2)
		{
			if (selectable.gameObject.activeInHierarchy && UiMatch(selectable.gameObject, key, ref best))
			{
				return best;
			}
		}
		return best;
	}

	private static bool UiMatch(GameObject go, string key, ref GameObject best)
	{
		string text = UiPath(go.transform);
		if (string.Equals(text, key, StringComparison.OrdinalIgnoreCase) || text.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase))
		{
			best = go;
			return true;
		}
		if (best == null && (text.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 || UiLabel(go).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0))
		{
			best = go;
		}
		return false;
	}

	private static UIBase FindUiScript(string typeName)
	{
		Type type = null;
		Type[] types = typeof(TestBridge).Assembly.GetTypes();
		foreach (Type type2 in types)
		{
			if (typeof(UIBase).IsAssignableFrom(type2) && (type2.Name == typeName || type2.FullName == typeName))
			{
				type = type2;
				break;
			}
		}
		if (type == null)
		{
			return null;
		}
		if (!UiReady())
		{
			return null;
		}
		return typeof(UIManager).GetMethod("FindScript", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(type).Invoke(null, null) as UIBase;
	}

	private IEnumerator CmdKey(string[] args, Result r)
	{
		if (args.Length < 2)
		{
			r.Fail("key <InputCommand> [down|up|press]");
			yield break;
		}
		InputCommand command;
		try
		{
			command = (InputCommand)Enum.Parse(typeof(InputCommand), args[1], ignoreCase: true);
		}
		catch
		{
			r.Fail("ไม\u0e48ร\u0e39\u0e49จ\u0e31ก InputCommand " + args[1] + " — ด\u0e39 keys");
			yield break;
		}
		string mode = ((args.Length > 2) ? args[2].ToLowerInvariant() : "press");
		InputSystem input = GameSystem<InputSystem>.Instance();
		if (mode == "down")
		{
			input.TestBridgeDispatch(command, Trigger.Down);
		}
		else if (mode == "up")
		{
			input.TestBridgeDispatch(command, Trigger.Up);
		}
		else
		{
			input.TestBridgeDispatch(command, Trigger.Down);
			yield return null;
			input.TestBridgeDispatch(command, Trigger.Up);
		}
		yield return null;
		r.Set("command", command.ToString()).Set("mode", mode);
	}

	private IEnumerator CmdKeys(string[] args, Result r)
	{
		string text = ((args.Length > 1) ? args[1] : null);
		List<object> list = new List<object>();
		string[] names = Enum.GetNames(typeof(InputCommand));
		foreach (string text2 in names)
		{
			if (text == null || text2.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				list.Add(text2);
			}
		}
		r.Set("commands", list).Set("count", list.Count);
		yield break;
	}

	private IEnumerator CmdEmote(string[] args, Result r)
	{
		if (args.Length < 2)
		{
			r.Fail("emote <id>");
			yield break;
		}
		SocialSystem socialSystem = GameSystem<SocialSystem>.Instance();
		Emoticon emoticon = socialSystem.Emotional.GetEmoticon(args[1]);
		if (emoticon == null)
		{
			r.Fail("ไม\u0e48ร\u0e39\u0e49จ\u0e31กอ\u0e35โมต\u0e34คอน " + args[1]);
			yield break;
		}
		socialSystem.PlayEmoticon(emoticon);
		yield return null;
		r.Set("emoticon", args[1]).Set("available", emoticon.Available);
	}

	private IEnumerator CmdMotion(string[] args, Result r)
	{
		if (args.Length < 2)
		{
			r.Fail("motion <id>");
			yield break;
		}
		SocialSystem socialSystem = GameSystem<SocialSystem>.Instance();
		Durango.Logic.Social.Motion motion = socialSystem.Emotional.GetMotion(args[1]);
		if (motion == null)
		{
			r.Fail("ไม\u0e48ร\u0e39\u0e49จ\u0e31กท\u0e48าทาง " + args[1]);
		}
		else
		{
			r.Set("motion", args[1]).Set("played", socialSystem.PlayMotion(motion));
		}
	}

	private IEnumerator CmdCheat(string[] args, Result r)
	{
		if (args.Length < 2)
		{
			r.Fail("cheat <text>");
			yield break;
		}
		string text = string.Join(" ", args, 1, args.Length - 1);
		Connections.Frontend.Send(new Cheat
		{
			_Cheat = text
		});
		yield return new WaitForSeconds(0.5f);
		r.Set("sent", text);
	}

	private IEnumerator CmdBoat(string[] args, Result r)
	{
		TutorialIslandSystem tutorial = GameSystem<TutorialIslandSystem>.Instance();
		string text = ((args.Length > 1) ? args[1].ToLowerInvariant() : "");
		if (text == "put")
		{
			string text2 = tutorial.TestBridgePutAllMaterials();
			if (text2 != null)
			{
				r.Fail(text2);
			}
			yield return new WaitForSeconds(0.5f);
		}
		else if (text == "depart")
		{
			string text3 = tutorial.TestBridgeDepart();
			if (text3 != null)
			{
				r.Fail(text3);
			}
			yield return new WaitForSeconds(0.5f);
		}
		r.Set("boat", tutorial.TestBridgeInfo());
	}

	private IEnumerator CmdScreenshot(string[] args, Result r)
	{
		string path = ((args.Length > 1) ? args[1] : Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "testbridge_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".png"));
		ScreenCapture.CaptureScreenshot(path);
		yield return new WaitForEndOfFrame();
		yield return new WaitForSeconds(0.5f);
		r.Set("path", Path.GetFullPath(path)).Set("exists", File.Exists(path));
	}

	private IEnumerator CmdLog(string[] args, Result r)
	{
		int num = ((args.Length > 1) ? ((int)ParseFloat(args[1], 30f)) : 30);
		List<object> list = new List<object>();
		lock (_log)
		{
			for (int i = Mathf.Max(0, _log.Count - num); i < _log.Count; i++)
			{
				list.Add(_log[i]);
			}
		}
		r.Set("lines", list);
		yield break;
	}

	private IEnumerator CmdWait(string[] args, Result r)
	{
		float ms = ((args.Length > 1) ? ParseFloat(args[1], 1000f) : 1000f);
		yield return new WaitForSeconds(ms / 1000f);
		r.Set("waited", ms);
	}

	private static float ParseFloat(string text, float fallback)
	{
		if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return fallback;
		}
		return result;
	}

	public static string StripTags(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		int num = 0;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			switch (c)
			{
			case '<':
				num++;
				continue;
			case '>':
				if (num > 0)
				{
					num--;
					continue;
				}
				break;
			}
			if (c == '[')
			{
				int num2 = text.IndexOf(']', i);
				if (num2 > i && num2 - i <= 9)
				{
					i = num2;
					continue;
				}
			}
			if (num == 0)
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private static Dictionary<string, object> Dict(params object[] pairs)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		for (int i = 0; i + 1 < pairs.Length; i += 2)
		{
			dictionary[(string)pairs[i]] = pairs[i + 1];
		}
		return dictionary;
	}

	private static string[] SplitArgs(string line)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		foreach (char c in line)
		{
			if (c == '"')
			{
				flag = !flag;
			}
			else if (char.IsWhiteSpace(c) && !flag)
			{
				if (stringBuilder.Length > 0)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Length = 0;
				}
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		if (stringBuilder.Length > 0)
		{
			list.Add(stringBuilder.ToString());
		}
		return list.ToArray();
	}

	private static string Json(Result result)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("{\"ok\":").Append(result.Ok ? "true" : "false");
		foreach (KeyValuePair<string, object> datum in result.Data)
		{
			stringBuilder.Append(',');
			JsonString(stringBuilder, datum.Key);
			stringBuilder.Append(':');
			JsonValue(stringBuilder, datum.Value);
		}
		stringBuilder.Append('}');
		return stringBuilder.ToString();
	}

	private static void JsonValue(StringBuilder sb, object value)
	{
		if (value == null)
		{
			sb.Append("null");
		}
		else if (value is string)
		{
			JsonString(sb, (string)value);
		}
		else if (value is bool)
		{
			sb.Append(((bool)value) ? "true" : "false");
		}
		else if (value is int || value is long || value is short || value is byte || value is ushort)
		{
			sb.Append(Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture));
		}
		else if (value is float || value is double)
		{
			double d = Convert.ToDouble(value);
			if (double.IsNaN(d) || double.IsInfinity(d))
			{
				sb.Append("null");
			}
			else
			{
				sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
			}
		}
		else if (value is IDictionary<string, object>)
		{
			sb.Append('{');
			bool flag = true;
			foreach (KeyValuePair<string, object> item in (IDictionary<string, object>)value)
			{
				if (!flag)
				{
					sb.Append(',');
				}
				flag = false;
				JsonString(sb, item.Key);
				sb.Append(':');
				JsonValue(sb, item.Value);
			}
			sb.Append('}');
		}
		else if (value is IEnumerable)
		{
			sb.Append('[');
			bool flag2 = true;
			foreach (object item2 in (IEnumerable)value)
			{
				if (!flag2)
				{
					sb.Append(',');
				}
				flag2 = false;
				JsonValue(sb, item2);
			}
			sb.Append(']');
		}
		else
		{
			JsonString(sb, value.ToString());
		}
	}

	private static void JsonString(StringBuilder sb, string text)
	{
		sb.Append('"');
		if (text != null)
		{
			foreach (char c in text)
			{
				switch (c)
				{
				case '"':
					sb.Append("\\\"");
					continue;
				case '\\':
					sb.Append("\\\\");
					continue;
				case '\n':
					sb.Append("\\n");
					continue;
				case '\r':
					sb.Append("\\r");
					continue;
				case '\t':
					sb.Append("\\t");
					continue;
				}
				if (c < ' ')
				{
					StringBuilder stringBuilder = sb.Append("\\u");
					int num = c;
					stringBuilder.Append(num.ToString("x4", CultureInfo.InvariantCulture));
				}
				else
				{
					sb.Append(c);
				}
			}
		}
		sb.Append('"');
	}
}
