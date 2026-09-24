using System;
using System.Collections.Generic;
using Durango.Network;
using Durango.Terrain;
using Durango.Utils;
using Messages;
using UnityEngine;

namespace Durango.Logic.Npc;

public static class NpcSpawner
{
	private sealed class Def
	{
		public int Index;

		public string Id;

		public string Name;

		public string Portrait;

		public string Prefab;

		public bool Male;

		public string Hair;

		public string HairColor;

		public string SkinColor;

		public string Beard;

		public string Body;

		public string[] BodyColor;

		public Vector2 HomeTile;

		public float Radius;
	}

	private const string SafehousePrefix = "grass_company_safehouse";

	private static readonly string[] Office = new string[3] { "3B3F4A", "E8E4DA", "2C2C2C" };

	private static readonly string[] Worker = new string[3] { "8A6B3F", "D9CBA8", "3A2E22" };

	private static readonly string[] Coat = new string[3] { "F2F2EE", "DADAD6", "2B2B2B" };

	private static readonly string[] Police = new string[3] { "1F2E4A", "C9D1DC", "1A1A1A" };

	private static readonly string[] Leather = new string[3] { "6B4A2B", "A67C52", "2E2E2E" };

	private static readonly string[] Linen = new string[3] { "C9BFA5", "8C7B5C", "3A3A3A" };

	private static readonly string[] Rain = new string[3] { "E3B32A", "C48F10", "2A2A2A" };

	private static readonly string[] Jacket = new string[3] { "4A5D3B", "2E3A26", "1E1E1E" };

	private const string M_OFFICE = "Models/PC/Male/Body/m_body_officelook.fbx";

	private const string F_OFFICE = "Models/PC/Female/Body/f_body_officelook.fbx";

	private const string M_WORKER = "Models/PC/Male/Body/m_body_worker.fbx";

	private const string F_WORKER = "Models/PC/Female/Body/f_body_worker.fbx";

	private const string M_HOUSE = "Models/PC/Male/Body/m_body_house.fbx";

	private const string F_HOUSE = "Models/PC/Female/Body/f_body_house.fbx";

	private const string M_POLICE = "Models/PC/Male/Body/m_body_police.FBX";

	private const string F_POLICE = "Models/PC/Female/Body/f_body_police.FBX";

	private const string M_LEATHER = "Models/PC/Male/Body/m_body_beginner_leather.fbx";

	private const string F_LEATHER = "Models/PC/Female/Body/f_body_beginner_leather.fbx";

	private const string M_LINEN = "Models/PC/Male/Body/m_body_beginner_linen.fbx";

	private const string F_LINEN = "Models/PC/Female/Body/f_body_beginner_linen.fbx";

	private const string M_RAIN = "Models/PC/Male/Body/m_body_raincoat_hoody.FBX";

	private const string F_RAIN = "Models/PC/Female/Body/f_body_raincoat_hoody.FBX";

	private const string M_JACKET = "Models/PC/Male/Body/m_body_long_jacket.fbx";

	private const string F_JACKET = "Models/PC/Female/Body/f_body_long_jacket.fbx";

	private const string M_TRENCH = "Models/PC/Male/Body/m_body_trenchcoat.prefab";

	private const string F_TRENCH = "Models/PC/Female/Body/f_body_trenchcoat.prefab";

	private static readonly Def[] Defs = new Def[21]
	{
		new Def
		{
			Index = 0,
			Id = "k",
			Name = "K",
			Portrait = "todo_icon_npc_TheFirm",
			Prefab = "Models/NPC/F_NPC_K.prefab",
			HomeTile = new Vector2(130f, 108f),
			Radius = 6f
		},
		P(1, "liu", "ร\u0e34ว", "todo_icon_npc_ChlorophylForum", male: false, "f_hair_lady", "2A1A0F", "E9C9B0", null, "Models/PC/Female/Body/f_body_officelook.fbx", new string[3] { "3E7A45", "E8E4DA", "2C2C2C" }, 112f, 90f, 6f),
		P(2, "nowak", "โนว\u0e31ค", "todo_icon_npc_ChamberOfPioneer", male: true, "m_hair_short_side", "5A3A1E", "D9B18F", "m_beard_medium_outskirt", "Models/PC/Male/Body/m_body_worker.fbx", Worker, 126f, 88f, 6f),
		P(3, "x", "X", "todo_icon_npc_TheCommittee", male: true, "m_hair_mid_neat", "1A1A1A", "E5CDB8", null, "Models/PC/Male/Body/m_body_trenchcoat.prefab", new string[3] { "2B2B2B", "3A3A3A", "111111" }, 116f, 82f, 4f),
		P(4, "lama", "ดร.ลามะ", "todo_icon_npc_Lama", male: true, "m_hair_bald", "6E6E6E", "C99A7A", "m_beard_large_castro", "Models/PC/Male/Body/m_body_officelook.fbx", Coat, 108f, 98f, 5f),
		P(5, "rodriguez", "โรดร\u0e34เกซ", "todo_icon_npc_rodriguez", male: true, "m_hair_messy", "2A1A0F", "B57A55", "m_beard_small_mexican", "Models/PC/Male/Body/m_body_beginner_leather.fbx", Leather, 124f, 100f, 6f),
		P(6, "sawarat", "สาวร\u0e31ตน\u0e4c", "todo_icon_npc_sawarat", male: false, "f_hair_ponytail", "1A1A1A", "C68E63", null, "Models/PC/Female/Body/f_body_beginner_linen.fbx", Linen, 110f, 104f, 6f),
		P(7, "mccain", "แม\u0e47คเคน", "todo_icon_npc_mccain", male: true, "m_hair_wilson", "8A8A8A", "E1C1A6", "m_beard_large_wilson", "Models/PC/Male/Body/m_body_long_jacket.fbx", Jacket, 128f, 94f, 6f),
		P(8, "zein", "ซาอ\u0e34น", "todo_icon_npc_zein", male: true, "m_hair_sideshave", "1A1A1A", "8C5A3C", null, "Models/PC/Male/Body/m_body_police.FBX", Police, 120f, 108f, 6f),
		P(9, "pia", "เป\u0e35ยร\u0e4c", "todo_icon_npc_pia", male: false, "f_hair_mid_cute", "B08A5A", "F0D6C2", null, "Models/PC/Female/Body/f_body_house.fbx", new string[3] { "C7A2B8", "F1E6EC", "2C2C2C" }, 114f, 110f, 5f),
		P(10, "e", "E", "todo_icon_npc_e", male: true, "m_hair_shortwave", "3A2A1A", "D2AE90", null, "Models/PC/Male/Body/m_body_beginner_linen.fbx", Linen, 132f, 96f, 5f),
		P(11, "f", "F", "todo_icon_npc_f", male: false, "f_hair_lowtail", "5A3A1E", "E4C4AE", null, "Models/PC/Female/Body/f_body_beginner_linen.fbx", Linen, 134f, 100f, 5f),
		P(12, "g", "G", "todo_icon_npc_g", male: true, "m_hair_bomb", "1A1A1A", "A0704E", null, "Models/PC/Male/Body/m_body_beginner_linen.fbx", Linen, 130f, 100f, 5f),
		P(13, "_924s", "อน\u0e38กรรมการ 924", "todo_icon_npc__924s", male: true, "m_hair_mid_neat", "2A2A2A", "E5CDB8", null, "Models/PC/Male/Body/m_body_officelook.fbx", Office, 118f, 78f, 3f),
		P(14, "_628s", "อน\u0e38กรรมการ 628", "todo_icon_npc__628s", male: false, "f_hair_mid_neat", "2A2A2A", "E9C9B0", null, "Models/PC/Female/Body/f_body_officelook.fbx", Office, 121f, 78f, 3f),
		P(15, "_415s", "อน\u0e38กรรมการ 415", "todo_icon_npc__415s", male: true, "m_hair_barcode", "5A5A5A", "D9B18F", null, "Models/PC/Male/Body/m_body_officelook.fbx", Office, 124f, 78f, 3f),
		P(16, "hauata", "ฮาวอาตา", "todo_icon_npc_hauata", male: true, "m_hair_braid_tail", "1A1A1A", "8C5A3C", null, "Models/PC/Male/Body/m_body_beginner_leather.fbx", Leather, 106f, 92f, 6f),
		P(17, "maki", "มาก\u0e34", "todo_icon_npc_maki", male: false, "f_hair_longstraight", "1A1A1A", "F0D6C2", null, "Models/PC/Female/Body/f_body_raincoat_hoody.FBX", Rain, 108f, 108f, 6f),
		P(18, "josipovic", "โยซ\u0e34โปว\u0e34ช", "todo_icon_npc_josipovic", male: true, "m_hair_charming", "8A6A3A", "E1C1A6", "m_beard_medium_continental", "Models/PC/Male/Body/m_body_worker.fbx", Worker, 136f, 108f, 6f),
		P(19, "charlie", "ชาร\u0e4cล\u0e35", "todo_icon_npc_Optimistic", male: true, "m_hair_afro_short", "2A1A0F", "B57A55", null, "Models/PC/Male/Body/m_body_house.fbx", new string[3] { "E2A33A", "F5E9D2", "2C2C2C" }, 122f, 94f, 7f),
		P(20, "d383", "D383", "todo_icon_npc_agent", male: true, "m_hair_bald", "2A2A2A", "D2AE90", null, "Models/PC/Male/Body/m_body_trenchcoat.prefab", new string[3] { "1F1F1F", "2A2A2A", "111111" }, 134f, 112f, 5f)
	};

	private static readonly string[] ActiveIds = new string[7] { "k", "x", "liu", "lama", "nowak", "charlie", "d383" };

	private static readonly Dictionary<string, string[]> Activities = new Dictionary<string, string[]>
	{
		{
			"k",
			new string[5] { "Barehand_Sit_B", "Avatar_Map", "Emotion_Think", "Emotion_Browse", "Barehand_Stand_Tired" }
		},
		{
			"x",
			new string[5] { "Emotion_Think", "Avatar_Map", "Emotion_Boring", "Emotion_Browse", "Emotion_NO" }
		},
		{
			"liu",
			new string[5] { "Barehand_Gather_Low", "Barehand_Gather_Middle", "Emotion_Wonder", "Barehand_Sit_A", "Emotion_Heart" }
		},
		{
			"lama",
			new string[5] { "Emotion_Think", "Avatar_Map", "Barehand_Gather_Low", "Emotion_Wonder", "Emotion_Browse" }
		},
		{
			"nowak",
			new string[5] { "Craft_Material_Wood", "Craft_Stand", "Emotion_Bodybuilder_A", "Barehand_Stand_Hot", "Barehand_Sit_C" }
		},
		{
			"charlie",
			new string[6] { "Emotion_Cheerup_A", "Emotion_Clap", "Emotion_Dance_Sway", "Barehand_Sit_A", "Emotion_Joy", "Emotion_Welcome_A" }
		},
		{
			"d383",
			new string[5] { "Avatar_Map", "Emotion_Browse", "Emotion_OK", "Emotion_Think", "Barehand_Stand" }
		}
	};

	private static readonly string[] DefaultActivities = new string[5] { "Emotion_Think", "Emotion_Browse", "Barehand_Sit_A", "Emotion_Boring", "Barehand_Gather_Low" };

	private static readonly List<GameObject> Spawned = new List<GameObject>();

	private static GameObject _conversation;

	private static bool _pending;

	public static List<GameObject> Instances => Spawned;

	private static Def P(int index, string id, string name, string portrait, bool male, string hair, string hairColor, string skin, string beard, string body, string[] bodyColor, float tx, float tz, float radius)
	{
		return new Def
		{
			Index = index,
			Id = id,
			Name = name,
			Portrait = portrait,
			Male = male,
			Hair = (male ? "Models/PC/Male/Hair/" : "Models/PC/Female/Hair/") + hair,
			HairColor = hairColor,
			SkinColor = skin,
			Beard = ((beard != null) ? ("Models/PC/Male/Beard/" + beard + ".fbx") : null),
			Body = body,
			BodyColor = bodyColor,
			HomeTile = new Vector2(tx, tz),
			Radius = radius
		};
	}

	public static string[] ActivitiesOf(string id)
	{
		if (!Activities.TryGetValue(id, out var value))
		{
			return DefaultActivities;
		}
		return value;
	}

	public static void ClearAll()
	{
		foreach (GameObject item in Spawned)
		{
			if (!(item == null))
			{
				PlayerBehavior component = item.GetComponent<PlayerBehavior>();
				if (component != null && Singleton<PlayerManager>.HasInstance())
				{
					Singleton<PlayerManager>.Instance().HandleDisappearMsg(new DisappearEntity
					{
						EntityId = component.EntityId
					});
				}
				else
				{
					UnityEngine.Object.Destroy(item);
				}
			}
		}
		Spawned.Clear();
		if (_conversation != null)
		{
			UnityEngine.Object.Destroy(_conversation);
			_conversation = null;
		}
	}

	public static void OnRegionInitialized()
	{
		ClearAll();
		_pending = (GameManager.Region?.TerrainId ?? string.Empty).StartsWith("grass_company_safehouse");
		if (_pending)
		{
			GameManager gameManager = Singleton<GameManager>.Instance();
			gameManager.MainSceneLoaded -= OnMainSceneLoaded;
			gameManager.MainSceneLoaded += OnMainSceneLoaded;
			if (GameManager.IsMainScene && Singleton<TerrainBase>.HasInstance() && Singleton<TerrainBase>.Instance().IsReady)
			{
				OnChunksLoaded();
			}
		}
	}

	private static void OnMainSceneLoaded()
	{
		if (_pending && !GameManager.IsPrologueMode && Singleton<TerrainBase>.HasInstance())
		{
			TerrainBase terrainBase = Singleton<TerrainBase>.Instance();
			terrainBase.LoadingChunksFinished -= OnChunksLoaded;
			terrainBase.LoadingChunksFinished += OnChunksLoaded;
		}
	}

	private static void OnChunksLoaded()
	{
		if (Singleton<TerrainBase>.HasInstance())
		{
			Singleton<TerrainBase>.Instance().LoadingChunksFinished -= OnChunksLoaded;
		}
		if (_pending)
		{
			Singleton<GameManager>.Instance().AddOnReady(SpawnAll);
		}
	}

	public static void SpawnAll()
	{
		_pending = false;
		ClearAll();
		if (!(GameManager.Region?.TerrainId ?? string.Empty).StartsWith("grass_company_safehouse"))
		{
			return;
		}
		Def[] defs = Defs;
		foreach (Def def in defs)
		{
			if (def.Prefab == null && IsActive(def))
			{
				SpawnPlayerBody(def);
			}
		}
		defs = Defs;
		foreach (Def def2 in defs)
		{
			if (def2.Prefab != null && IsActive(def2))
			{
				SpawnPrefab(def2);
			}
		}
		_conversation = new GameObject("NpcConversation");
		_conversation.AddComponent<NpcConversation>();
	}

	private static bool IsActive(Def d)
	{
		return Array.IndexOf(ActiveIds, d.Id) >= 0;
	}

	private static void BorrowPlayerClips(AnimalBehavior animal, Def d)
	{
		if (animal == null || animal.Anim == null)
		{
			return;
		}
		Animation animation = null;
		foreach (GameObject item in Spawned)
		{
			PlayerBehavior playerBehavior = ((item != null) ? item.GetComponent<PlayerBehavior>() : null);
			if (playerBehavior != null && !playerBehavior.IsMale && playerBehavior.Anim != null)
			{
				animation = playerBehavior.Anim;
				break;
			}
		}
		if (animation == null && PlayerBehavior.LocalPlayer != null && !PlayerBehavior.LocalPlayer.IsMale)
		{
			animation = PlayerBehavior.LocalPlayer.Anim;
		}
		if (animation == null)
		{
			return;
		}
		List<string> list = new List<string>();
		list.Add("Barehand_Stand");
		list.Add("Barehand_Run");
		list.Add("Barehand_Walk_Tired");
		list.Add("Barehand_Stand_Tired");
		list.AddRange(ActivitiesOf(d.Id));
		int num = 0;
		foreach (string item2 in list)
		{
			string text = "F_" + item2;
			if (!(animal.Anim.GetClip(text) != null))
			{
				AnimationClip clip = animation.GetClip(text);
				if (clip != null)
				{
					animal.Anim.AddClip(clip, text);
					num++;
				}
			}
		}
	}

	private static void SpawnPrefab(Def d)
	{
		Singleton<AssetBundleManager>.Instance().RequestAsset(d.Prefab, typeof(GameObject), delegate(UnityEngine.Object asset)
		{
			if (asset == null)
			{
				SpawnPlayerBody(d);
			}
			else if ((GameManager.Region?.TerrainId ?? string.Empty).StartsWith("grass_company_safehouse"))
			{
				GameObject gameObject = (GameObject)UnityEngine.Object.Instantiate(asset, Vector3.zero, Quaternion.identity);
				gameObject.name = "npc_" + d.Id;
				NpcAIK[] componentsInChildren = gameObject.GetComponentsInChildren<NpcAIK>(includeInactive: true);
				for (int i = 0; i < componentsInChildren.Length; i++)
				{
					UnityEngine.Object.DestroyImmediate(componentsInChildren[i]);
				}
				ClientAnimalActor[] componentsInChildren2 = gameObject.GetComponentsInChildren<ClientAnimalActor>(includeInactive: true);
				for (int j = 0; j < componentsInChildren2.Length; j++)
				{
					UnityEngine.Object.DestroyImmediate(componentsInChildren2[j]);
				}
				ClientInteractionQuest[] componentsInChildren3 = gameObject.GetComponentsInChildren<ClientInteractionQuest>(includeInactive: true);
				for (int k = 0; k < componentsInChildren3.Length; k++)
				{
					UnityEngine.Object.DestroyImmediate(componentsInChildren3[k]);
				}
				Vector3 vector = GroundAt(d.HomeTile);
				AnimalBehavior component = gameObject.GetComponent<AnimalBehavior>();
				if (component != null)
				{
					component.EntityId = "npc_" + d.Id;
					component.CurrentPosition = vector;
					component.TurnToYaw(180f, bSnap: true);
					BorrowPlayerClips(component, d);
				}
				else
				{
					gameObject.transform.position = vector;
				}
				Finish(gameObject, d);
			}
		});
	}

	private static void SpawnPlayerBody(Def d)
	{
		string text = "npc_" + d.Id;
		Vector3 fromClientPosition = GroundAt(d.HomeTile);
		WorldPosition position = default(WorldPosition);
		position.SetFromClientPosition(fromClientPosition);
		double bufferedServerTime = Connections.Frontend.GetBufferedServerTime();
		string text2 = (d.Male ? "Models/PC/Male/Body/m_body_nothing.FBX" : "Models/PC/Female/Body/f_body_nothing.FBX");
		string defaultInner = (d.Male ? "Models/PC/Male/Inner/m_inner_basic.FBX" : "Models/PC/Female/Inner/f_inner_basic.FBX");
		PlayerDisplay display = new PlayerDisplay
		{
			EntityId = text,
			DefaultBody = text2,
			DefaultInner = defaultInner,
			DefaultHead = string.Empty,
			DefaultHair = string.Empty,
			Hair = (d.Hair ?? string.Empty),
			Body = (d.Body ?? text2),
			Head = string.Empty,
			Equip = string.Empty,
			Beard = d.Beard,
			BodyColor = (d.BodyColor ?? new string[0]),
			HeadColor = new string[0],
			EquipColor = new string[0],
			SkinColor = d.SkinColor,
			HairColor = d.HairColor,
			EyeColor = "3B2412",
			LipColor = "C97A6A",
			BodySize = 1f,
			VehicleEntityId = string.Empty
		};
		AppearPlayer appearPlayer = new AppearPlayer
		{
			EntityId = text,
			EntityType = (ushort)(d.Male ? 1000u : 1001u),
			IsAlive = true,
			Name = d.Name,
			Level = 1,
			Title = new Title
			{
				EntityId = string.Empty,
				TitleId = string.Empty,
				_Title = string.Empty
			},
			Member = new Member
			{
				EntityId = text,
				ClanId = string.Empty,
				ClanName = string.Empty,
				RoleId = -1,
				ApplyingClanId = null
			},
			Display = display,
			Move = new Move
			{
				EntityId = text,
				Movements = new Movement[1]
				{
					new Movement
					{
						MotionName = "Barehand_Stand",
						MotionOption = 1,
						PlaybackRate = 1f,
						RotSpeed = 540f,
						Path = new Location[1]
						{
							new Location
							{
								Position = position,
								Yaw = 180f,
								Time = bufferedServerTime,
								Floor = 0,
								Height = 0f
							}
						}
					}
				}
			},
			Survival = new Survival
			{
				EntityId = text,
				Life = new Gauge(300f, 0f, new GaugeNode[1]
				{
					new GaugeNode(0.0, 300f)
				}),
				Gauges = new Dictionary<string, Gauge>()
			},
			Musician = null,
			RescueRequested = false
		};
		PacketHeader header = new PacketHeader
		{
			Time = bufferedServerTime,
			TypeCode = 90u
		};
		Connections.Frontend.Handle(90u, appearPlayer, header);
		PlayerBehavior player = Singleton<PlayerManager>.Instance().GetPlayer(text);
		if (!(player == null))
		{
			player.gameObject.name = text;
			Finish(player.gameObject, d);
		}
	}

	private static void Finish(GameObject go, Def d)
	{
		ClientInteractionNpc.Attach(go, d.Index, "npc_" + d.Id, d.Name);
		NpcWanderAI.Attach(go, d.HomeTile, d.Radius, d.Id, d.Name, d.Portrait);
		Spawned.Add(go);
	}

	private static Vector3 GroundAt(Vector2 tile)
	{
		Vector3 vector = Util.TilePositionToClientPosition(tile, tileCenter: true);
		vector.y = LocalMoveOperator.GetWorldHeight(vector, 0, 0f).GetValueOrDefault();
		return vector;
	}
}
