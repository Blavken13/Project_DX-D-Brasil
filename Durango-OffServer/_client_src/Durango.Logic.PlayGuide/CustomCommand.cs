using System;
using System.Collections;
using System.Collections.Generic;
using Durango.Network;
using Durango.Render.Camera;
using Durango.System;
using Durango.Terrain;
using Durango.UI;
using Durango.UI.Control;
using Durango.Utils;
using Durango.Utils.Extensions;
using JetBrains.Annotations;
using L10N;
using Messages;
using Shared.Faction;
using UnityEngine;

namespace Durango.Logic.PlayGuide;

public class CustomCommand
{
	private readonly Dictionary<string, Action> _registeredEvents = new Dictionary<string, Action>();

	private readonly Dictionary<string, Action<Dictionary<string, string>>> _registeredEventsWithParams = new Dictionary<string, Action<Dictionary<string, string>>>();

	private readonly PlayGuideSystem _system;

	private Coroutine _delayedFaceToKCoroutine;

	[CanBeNull]
	private NpcAI_KBike _npcAiK;

	[CanBeNull]
	private NpcAIDog _npcAiDog;

	private DogGuideState _lastDogGuideState;

	private Vector2 _dogPOIOnLoaded;

	private bool _isNpcDogSpawned;

	private const string RaftKEntityId = "502";

	private const string RaftKPrefab = "Models/NPC/F_NPC_K_Story.prefab";

	private const string RaftKToDo = "talk_npc_raft_ancora.meet_chief";

	private static readonly Vector2 RaftKFallbackTile = new Vector2(213f, 93f);

	[CanBeNull]
	private GameObject _npcRaftK;

	private bool _isNpcRaftKSpawning;

	private static readonly Vector2[] DogPoiChain = new Vector2[10]
	{
		new Vector2(63f, 60f),
		new Vector2(74f, 67f),
		new Vector2(80f, 78f),
		new Vector2(94f, 87f),
		new Vector2(125f, 96f),
		new Vector2(134f, 110f),
		new Vector2(139f, 115f),
		new Vector2(144f, 118f),
		new Vector2(186f, 119f),
		new Vector2(211f, 118f)
	};

	private int _dogChainMax = -1;

	private const string GuidePinKey = "guide.dog_poi";

	public event Action Ready;

	public event Action GuideOfKBegin;

	public event Action GuideOfKEnd;

	private static int DogChainIndex(Vector2 tile)
	{
		for (int i = 0; i < DogPoiChain.Length; i++)
		{
			if (Mathf.Abs(DogPoiChain[i].x - tile.x) < 0.5f && Mathf.Abs(DogPoiChain[i].y - tile.y) < 0.5f)
			{
				return i;
			}
		}
		return -1;
	}

	private Vector2 ResolveDogPoi(Vector2 requested)
	{
		Vector2 vector = requested;
		int num = DogChainIndex(requested);
		if (num >= 0)
		{
			if (num < _dogChainMax)
			{
				vector = DogPoiChain[_dogChainMax];
			}
			else
			{
				_dogChainMax = num;
			}
		}
		if (DogChainIndex(vector) == DogPoiChain.Length - 1)
		{
			Point2? tutorialBoatTile = GameSystem<TutorialIslandSystem>.Instance().TutorialBoatTile;
			Vector2 vector2 = ((!tutorialBoatTile.HasValue) ? RaftKFallbackTile : new Vector2(tutorialBoatTile.Value.x, tutorialBoatTile.Value.y));
			vector = new Vector2(vector2.x - 1f, vector2.y + 4f);
		}
		return vector;
	}

	public CustomCommand(PlayGuideSystem system)
	{
		_system = system;
		RegisterEventCommands();
	}

	private void RegisterEventCommands()
	{
		RegisterEventCommand("Event_BeginMMO", Event_BeginMMO);
		RegisterEventCommand("Event_Appear_K", Event_Appear_K);
		RegisterEventCommand("Event_BeginCPR", Event_BeginCPR);
		RegisterEventCommand("Event_Show_BottomMenu", Event_Show_BottomMenu);
		RegisterEventCommand("Event_Disappear_K", Event_Disappear_K);
		RegisterEventCommand("Dog_NormalMode", Dog_NormalMode);
		RegisterEventCommand("Dog_Introduce", Dog_Introduce);
		RegisterEventCommand("Dog_Happy", Dog_Happy);
		RegisterEventCommand("Event_UnLockPlayerMove", Event_UnLockPlayerMove);
		RegisterEventCommand("Event_ShowOtherPlayer", Event_ShowOtherPlayer);
		RegisterEventCommand("Ancora_Event_Init_Health", Ancora_Event_Init_Health);
		RegisterEventCommand("Ancora_Event_Resurrect", Ancora_Event_Resurrect);
		RegisterEventCommand("Ancora_Event_Restore_Food_K", Ancora_Event_Restore_Food_K);
		RegisterEventCommand("Ancora_Event_Tired", Ancora_Event_Tired);
		RegisterEventCommand("Event_Restore_Standing_K_CutScene", Event_Restore_Standing_K_CutScene);
		RegisterEventCommand("Refresh_Context_Action", Refresh_Context_Action);
		RegisterEventCommand("Enable_Magnifying_Glass", Enable_Magnifying_Glass);
		RegisterEventCommand("Close_Inventory", Close_Inventory);
		RegisterEventCommand("Dog_SetPOI_Tile", Dog_SetPOI_Tile);
		RegisterEventCommand("Dog_Set_Farewell_Tile", Dog_Set_Farewell_Tile);
		RegisterEventCommand("CustomQuestEvent", CustomQuestEvent);
		RegisterEventCommand("PlaySoundEvent", PlaySoundEvent);
		RegisterEventCommand("Activate_Faction", Activate_Faction);
	}

	private static void Activate_Faction(Dictionary<string, string> parameters)
	{
		string value = parameters.Get("faction");
		if (string.IsNullOrEmpty(value))
		{
			return;
		}
		try
		{
			FactionType faction = (FactionType)Enum.Parse(typeof(FactionType), value, ignoreCase: true);
			Connections.Frontend.Send(new ActivateFaction
			{
				Faction = faction
			});
		}
		catch (Exception)
		{
		}
	}

	public void ClearAll()
	{
		if (_npcAiDog != null)
		{
			UnityEngine.Object.Destroy(_npcAiDog.gameObject);
		}
		if (_npcAiK != null)
		{
			UnityEngine.Object.Destroy(_npcAiK.gameObject);
		}
		_npcAiK = null;
		_npcAiDog = null;
		_lastDogGuideState = DogGuideState.Intro;
		_dogPOIOnLoaded = Vector2.zero;
		_isNpcDogSpawned = false;
		_dogChainMax = -1;
		if (_npcRaftK != null)
		{
			UnityEngine.Object.Destroy(_npcRaftK);
		}
		_npcRaftK = null;
		_isNpcRaftKSpawning = false;
	}

	public void OnEventStarted(string eventName)
	{
		if (eventName == "trigger_arrive_shipyard" || eventName == "talk_npc_raft_ancora")
		{
			SpawnRaftK();
		}
	}

	private void SpawnRaftK()
	{
		if (_npcRaftK != null || _isNpcRaftKSpawning)
		{
			return;
		}
		_isNpcRaftKSpawning = true;
		Singleton<AssetBundleManager>.Instance().RequestAsset("Models/NPC/F_NPC_K_Story.prefab", typeof(GameObject), delegate(UnityEngine.Object asset)
		{
			_isNpcRaftKSpawning = false;
			if (!(asset == null) && !(_npcRaftK != null))
			{
				GameObject gameObject = (GameObject)UnityEngine.Object.Instantiate(asset, Vector3.zero, Quaternion.identity);
				gameObject.name = "NPC_K_Raft_Ancora";
				ClientInteractionQuest[] componentsInChildren = gameObject.GetComponentsInChildren<ClientInteractionQuest>(includeInactive: true);
				for (int i = 0; i < componentsInChildren.Length; i++)
				{
					UnityEngine.Object.DestroyImmediate(componentsInChildren[i]);
				}
				ClientInteractionToDo.Attach(gameObject, "502", "K", T._("대화하기"), "talk_npc_raft_ancora.meet_chief");
				Point2? tutorialBoatTile = GameSystem<TutorialIslandSystem>.Instance().TutorialBoatTile;
				Vector2 tilePosition = ((!tutorialBoatTile.HasValue) ? RaftKFallbackTile : new Vector2(tutorialBoatTile.Value.x, tutorialBoatTile.Value.y));
				tilePosition.x -= 2f;
				tilePosition.y += 3f;
				Vector3 position = Durango.Terrain.Util.TilePositionToClientPosition(tilePosition);
				position.x += 100f;
				position.z += 100f;
				gameObject.transform.position = position;
				gameObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
				_npcRaftK = gameObject;
			}
		});
	}

	public void DispatchCustomCmd(string customCmd)
	{
		if (string.IsNullOrEmpty(customCmd))
		{
			return;
		}
		string[] array = customCmd.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
		int num = array.Length;
		for (int i = 0; i < num; i++)
		{
			ExtractParameters(array[i].Trim(), out var cmd, out var parameters);
			if (!string.IsNullOrEmpty(cmd))
			{
				if (parameters != null && parameters.Count > 0)
				{
					ExecuteCustomCmd(cmd, parameters);
				}
				else
				{
					ExecuteCustomCmd(cmd);
				}
			}
		}
	}

	public void LoadDogGuideProgress(PlayGuideSystem.GuideStorageData guideStorageData)
	{
		_lastDogGuideState = guideStorageData.LastDogGuideState;
		_dogPOIOnLoaded = guideStorageData.LastDogPOITile;
		_dogChainMax = DogChainIndex(_dogPOIOnLoaded);
	}

	public void SaveDogGuideProgress(PlayGuideSystem.GuideStorageData guideStorageData)
	{
		guideStorageData.LastDogGuideState = _lastDogGuideState;
		guideStorageData.LastDogPOITile = ((!(_npcAiDog == null)) ? _npcAiDog.GetPOIPosTile() : Vector2.zero);
	}

	public void RestoreDogState()
	{
		switch (_lastDogGuideState)
		{
		case DogGuideState.Intro:
		case DogGuideState.AfterCpr:
			Singleton<BgmManager>.Instance().SetPause(pause: true);
			break;
		case DogGuideState.Normal:
			CallDogCommand(delegate
			{
				if (_npcAiDog != null)
				{
					Vector2 pOIPosTile = ResolveDogPoi(_dogPOIOnLoaded);
					SetGuidePin(pOIPosTile.x, pOIPosTile.y);
					_npcAiDog.SetPOIPosTile(pOIPosTile);
				}
			});
			UIManager.OnLoadingCurtainHidden(LetDogMoveCloseToPlayer);
			break;
		case DogGuideState.Finished:
			break;
		}
	}

	private void LetDogMoveCloseToPlayer()
	{
		if (_npcAiDog != null)
		{
			_npcAiDog.MoveCloseToPlayer();
		}
	}

	private void RegisterEventCommand(string cmdName, Action<Dictionary<string, string>> function)
	{
		_registeredEventsWithParams[cmdName] = function;
	}

	private void RegisterEventCommand(string cmdName, Action function)
	{
		_registeredEvents[cmdName] = function;
	}

	private void ExecuteCustomCmd(string cmdName, Dictionary<string, string> parameters)
	{
		if (_registeredEventsWithParams.TryGetValue(cmdName, out var value))
		{
			value(parameters);
		}
	}

	private void ExecuteCustomCmd(string cmdName)
	{
		if (_registeredEvents.TryGetValue(cmdName, out var value))
		{
			value();
		}
	}

	public static void ExtractParameters(string cmdString, out string cmd, out Dictionary<string, string> parameters)
	{
		cmd = cmdString;
		parameters = null;
		if (!cmdString.Contains("("))
		{
			return;
		}
		string[] array = cmdString.Split(new string[4] { "(", ")", ",", " " }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length >= 1)
		{
			cmd = array[0];
		}
		if (array.Length < 2)
		{
			return;
		}
		parameters = new Dictionary<string, string>();
		for (int i = 1; i < array.Length; i++)
		{
			string[] array2 = array[i].Split(new string[2] { ":", " " }, StringSplitOptions.RemoveEmptyEntries);
			if (array2.Length == 2)
			{
				parameters.Add(array2[0], array2[1]);
			}
		}
	}

	private void Event_BeginMMO()
	{
		GameSystem<global::InputSystem>.Instance().MoveLock = true;
		PlayerBehavior localPlayer = PlayerBehavior.LocalPlayer;
		if (localPlayer != null)
		{
			localPlayer.OutlineEnabled = false;
			localPlayer.LookAtController.Activated = false;
		}
		Singleton<PlayerController>.Instance().IsProhibitAnimRefresh = true;
		Singleton<PlayerController>.Instance().TurnToYaw(0f, snap: true);
		PlayerController.MotionUpdater.Motion("Bike_Begin", 0f, 1f, forceTransition: true);
		LoadingCurtainGroup loadingCurtainGroup = UIManager.FindScript<LoadingCurtainGroup>();
		bool flag = loadingCurtainGroup == null || loadingCurtainGroup.IsFadeoutStarted;
		VisibleController.HideExceptFor(VisibleType.VisibleOnCutScene, hide: true, "BeginMMO");
		UIManager.MessageBox.SetVisible(visible: true, "BeginMMO");
		ShowBottomMenu(isShow: false);
		EnableMagnifyingGlass(enable: false);
		SpawnNpcDog(isEventIntro: true, flag, 1f);
		Singleton<CameraController>.Instance().SetZoom(1f);
		Singleton<CameraController>.Instance().LockZoomControl(isLock: true);
		CallDogCommand(delegate
		{
			if (_npcAiDog != null)
			{
				Singleton<CameraController>.Instance().Target(_npcAiDog.gameObject, 0.3f).Zoom(2.2f, 0.3f);
			}
		});
		_system.PauseUpdate = true;
		if (flag)
		{
			LoadingCurtain_FadeoutStarted();
			LoadingCurtain_FadeoutFinished();
		}
		else
		{
			EventDelegate.Add(loadingCurtainGroup.FadeOutStarted, LoadingCurtain_FadeoutStarted, oneShot: true);
			EventDelegate.Add(loadingCurtainGroup.FadeOutFinished, LoadingCurtain_FadeoutFinished, oneShot: true);
		}
	}

	private void LoadingCurtain_FadeoutStarted()
	{
		if (_npcAiDog != null)
		{
			_npcAiDog.RepositionToIntro();
			_npcAiDog.PlayIntroAnim();
		}
		_system.PauseUpdate = false;
	}

	private void LoadingCurtain_FadeoutFinished()
	{
		if (this.Ready != null)
		{
			this.Ready();
		}
	}

	private void Event_Appear_K()
	{
		SpawnBikeK();
		if (this.GuideOfKBegin != null)
		{
			this.GuideOfKBegin();
		}
	}

	private void Event_BeginCPR()
	{
		if (_npcAiK != null)
		{
			_npcAiK.BeginCPR();
		}
		Singleton<CameraController>.Instance().Target(null, 0.3f).Zoom(2.2f, 0.3f);
	}

	private static void Event_Show_BottomMenu()
	{
		ShowBottomMenu(isShow: true);
	}

	private static void ShowBottomMenu(bool isShow)
	{
		SetGroupVisible<MenuListGroupBase>(isShow);
		SetGroupVisible<BottomLeftMenuGroupBase>(isShow);
		if (Platform.Instance.UsePCUI)
		{
			SetGroupVisible<InteractionHelperGroupBase>(isShow);
			SetGroupVisible<ChattingGroupBase>(isShow);
		}
	}

	private static void SetGroupVisible<T>(bool isShow) where T : UIBase
	{
		T val = UIManager.FindScript<T>();
		if (val != null)
		{
			val.SetVisible(isShow, null, (!isShow) ? 0f : 0.3f);
		}
	}

	private void SpawnBikeK(Action func = null)
	{
		Singleton<AssetBundleManager>.Instance().RequestAsset("Models/NPC/NPC_KBikePrefab.prefab", typeof(GameObject), delegate(UnityEngine.Object asset)
		{
			if (!(asset == null))
			{
				GameObject gameObject = (GameObject)UnityEngine.Object.Instantiate(asset, Vector3.zero, Quaternion.identity);
				_npcAiK = gameObject.GetComponent<NpcAI_KBike>();
				if (_npcAiK != null)
				{
					_npcAiK.RepositionToIntro();
				}
				if (func != null)
				{
					func();
				}
			}
		});
	}

	private void SpawnNpcDog(bool isEventIntro, bool isReload = false, float delay = -1f)
	{
		if (_isNpcDogSpawned)
		{
			return;
		}
		if (_npcAiDog == null)
		{
			KUtility.DelayedCall(_system, delegate
			{
				Singleton<AssetBundleManager>.Instance().RequestAsset("Models/Ancora/Animals/Dog/DogPrefab.prefab", typeof(GameObject), delegate(UnityEngine.Object asset)
				{
					if (!(asset == null))
					{
						GameObject gameObject = (GameObject)UnityEngine.Object.Instantiate(asset, Vector3.zero, Quaternion.identity);
						_npcAiDog = gameObject.GetComponent<NpcAIDog>();
						if (isEventIntro && _npcAiDog != null)
						{
							_npcAiDog.PrepareIntroMMO();
							_npcAiDog.SetPOIPos(PlayerBehavior.LocalPlayer.CurrentPosition);
							if (isReload)
							{
								_npcAiDog.RepositionToIntro();
								_npcAiDog.PlayIntroAnim();
							}
						}
					}
				});
			}, delay);
		}
		_isNpcDogSpawned = true;
	}

	public void StandUp()
	{
		PlayerController.MotionUpdater.Motion("Bike_Getup", 0f, 1f, forceTransition: true);
		_delayedFaceToKCoroutine = _system.StartCoroutine(CoDelayedFaceToK());
		Singleton<CameraController>.Instance().Target(null, 0.3f).Offset(Vector3.zero, 0.3f)
			.Zoom(1.5f, 0.3f);
		VisibleController.HideExceptFor(VisibleType.VisibleOnCutScene, hide: false, "BeginMMO", 0.3f);
		ShowBottomMenu(isShow: false);
	}

	private IEnumerator CoDelayedFaceToK()
	{
		yield return new WaitForSeconds(5f);
		PlayerBehavior localPlayer = PlayerBehavior.LocalPlayer;
		Singleton<PlayerController>.Instance().RotateSpeed = 30f;
		if (_npcAiK != null)
		{
			Singleton<PlayerController>.Instance().RotateToObject(_npcAiK.Head);
			localPlayer.LookAtController.Activated = true;
			localPlayer.LookAtController.SetLookTarget(_npcAiK.Head);
			localPlayer.LookAtController.AutoChangeTarget = false;
		}
		Singleton<PlayerController>.Instance().IsProhibitAnimRefresh = false;
	}

	private void Event_Restore_Standing_K_CutScene()
	{
		_lastDogGuideState = DogGuideState.AfterCpr;
		Singleton<CameraController>.Instance().Target(null, 0.3f).Zoom(1.5f, 0.3f);
		if (_npcAiK != null)
		{
			return;
		}
		GameSystem<global::InputSystem>.Instance().MoveLock = true;
		PlayerBehavior localPlayer = PlayerBehavior.LocalPlayer;
		if (localPlayer != null)
		{
			localPlayer.OutlineEnabled = false;
		}
		SpawnBikeK(delegate
		{
			if (!(_npcAiK == null))
			{
				_npcAiK.RestoreStandingKCutScene();
				Singleton<PlayerController>.Instance().RotateToObject(_npcAiK.Head);
				PlayerBehavior.LocalPlayer.LookAtController.Activated = true;
				PlayerBehavior.LocalPlayer.LookAtController.SetLookTarget(_npcAiK.Head);
				PlayerBehavior.LocalPlayer.LookAtController.AutoChangeTarget = false;
			}
		});
		CallDogCommand(delegate
		{
			if (_npcAiDog != null)
			{
				_npcAiDog.RestoreStandingKCutScene();
			}
		});
	}

	private void Event_Disappear_K()
	{
		Singleton<CameraController>.Instance().Target(null, 0.3f).ZoomRatio(1f, 0.3f)
			.Offset(Vector3.zero, 0.3f);
		if (_npcAiK != null)
		{
			_npcAiK.EventRun();
		}
		if (this.GuideOfKEnd != null)
		{
			this.GuideOfKEnd();
		}
	}

	private void Dog_NormalMode()
	{
		_lastDogGuideState = DogGuideState.Normal;
	}

	private void Dog_Introduce()
	{
		if (_npcAiDog != null)
		{
			_npcAiDog.Dog_Introduce();
		}
	}

	private void Dog_Happy()
	{
		if (_npcAiDog != null)
		{
			_npcAiDog.Dog_Happy();
		}
	}

	public void Event_UnLockPlayerMove()
	{
		PlayerBehavior localPlayer = PlayerBehavior.LocalPlayer;
		if (localPlayer != null)
		{
			localPlayer.LookAtController.AutoChangeTarget = true;
			if (_delayedFaceToKCoroutine != null)
			{
				_system.StopCoroutine(_delayedFaceToKCoroutine);
			}
			Singleton<PlayerController>.Instance().RotateSpeed = 540f;
			localPlayer.OutlineEnabled = true;
			Singleton<PlayerController>.Instance().IsProhibitAnimRefresh = false;
			PlayerController.MotionUpdater.RefreshMotion(null, force: true);
		}
		GameSystem<global::InputSystem>.Instance().MoveLock = false;
		Singleton<CameraController>.Instance().LockZoomControl(isLock: false);
		Singleton<CameraController>.Instance().Zoom(0.8f, 0.3f);
		Singleton<BgmManager>.Instance().SetPause(pause: false);
	}

	private static void Event_ShowOtherPlayer()
	{
		Singleton<PlayerManager>.Instance().HideOtherPlayers(hide: false);
	}

	private void Dog_SetPOI_Tile(Dictionary<string, string> parameters)
	{
		if (!_system.IsGuideBegin)
		{
			return;
		}
		Vector2 poi = ResolveDogPoi(new Vector2(parameters.Get("x").ToFloat(), parameters.Get("y").ToFloat()));
		SetGuidePin(poi.x, poi.y);
		CallDogCommand(delegate
		{
			if (_npcAiDog != null)
			{
				_npcAiDog.SetPOIPosTile(poi);
			}
		});
	}

	private static void SetGuidePin(float tileX, float tileY)
	{
		Vector3 value = Durango.Terrain.Util.TilePositionToClientPosition(new Vector2(tileX, tileY));
		value.x += 100f;
		value.z += 100f;
		UIManager.FindScript<NavigateGroup>().Point.SetTarget("guide.dog_poi", new PointTargetController.Arguments
		{
			Position = value,
			Icon = "todo_icon_npc_TheFirm",
			HideInScreen = true
		});
	}

	private static void ClearGuidePin()
	{
		UIManager.FindScript<NavigateGroup>().Point.ClearTarget("guide.dog_poi");
	}

	private void Dog_Set_Farewell_Tile(Dictionary<string, string> parameters)
	{
		ClearGuidePin();
		_lastDogGuideState = DogGuideState.Finished;
		CallDogCommand(delegate
		{
			float x = parameters.Get("x").ToFloat();
			float y = parameters.Get("y").ToFloat();
			if (_npcAiDog != null)
			{
				_npcAiDog.SetFarewellTile(new Vector2(x, y));
			}
		});
	}

	private void CallDogCommand(Action action)
	{
		if (_npcAiDog == null)
		{
			SpawnNpcDog(isEventIntro: false);
		}
		_system.StartCoroutine(CoSetDogCommand(action));
	}

	private IEnumerator CoSetDogCommand(Action action)
	{
		while (_npcAiDog == null)
		{
			yield return null;
		}
		action?.Invoke();
	}

	private static void Ancora_Event_Init_Health()
	{
		Connections.Frontend.Send(new TutorialEvent
		{
			Event = "init_health"
		});
	}

	private static void Ancora_Event_Resurrect()
	{
		Connections.Frontend.Send(new TutorialEvent
		{
			Event = "resurrect"
		});
	}

	private static void Ancora_Event_Restore_Food_K()
	{
		Connections.Frontend.Send(new TutorialEvent
		{
			Event = "restore_food_k"
		});
	}

	private static void Ancora_Event_Tired()
	{
		Connections.Frontend.Send(new TutorialEvent
		{
			Event = "set_tired"
		});
		PlayerBehavior.LocalPlayer.SurvivalGaugeUpdated += PlayerBehavior_SurvivalGaugeUpdated;
	}

	private static void PlayerBehavior_SurvivalGaugeUpdated(CharacterBehavior chcracter)
	{
		if (chcracter == PlayerBehavior.LocalPlayer && PlayerBehavior.LocalPlayer.IsTired)
		{
			PlayerBehavior.LocalPlayer.SurvivalGaugeUpdated -= PlayerBehavior_SurvivalGaugeUpdated;
		}
	}

	private static void Refresh_Context_Action()
	{
		ContextActionGroupBase contextActionGroupBase = UIManager.FindScript<ContextActionGroupBase>();
		if (contextActionGroupBase != null)
		{
			contextActionGroupBase.RefreshActionList();
		}
	}

	private static void Enable_Magnifying_Glass()
	{
		EnableMagnifyingGlass(enable: true);
	}

	private static void EnableMagnifyingGlass(bool enable)
	{
		if (Platform.Instance.UsePCUI)
		{
			InteractionHelperGroup_PC interactionHelperGroup_PC = UIManager.FindScript<InteractionHelperGroup_PC>();
			if (interactionHelperGroup_PC != null)
			{
				interactionHelperGroup_PC.MagnifyingGlassDisabled = !enable;
			}
		}
	}

	private static void Close_Inventory()
	{
		InventoryGroup inventoryGroup = UIManager.FindScript<InventoryGroup>();
		if (inventoryGroup != null)
		{
			inventoryGroup.Close();
		}
	}

	private static void CustomQuestEvent(Dictionary<string, string> parameters)
	{
		string text = parameters.Get("keyword");
		if (text != null)
		{
			Connections.Frontend.Send(new CustomQuestEvent
			{
				Keyword = text
			});
		}
	}

	private static void PlaySoundEvent(Dictionary<string, string> parameters)
	{
		string text = parameters.Get("name");
		if (text != null)
		{
			SoundManager.PlayEvent(text);
		}
	}
}
