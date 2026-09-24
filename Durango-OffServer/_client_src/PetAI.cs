using System;
using System.Collections;
using System.Linq;
using Durango.Logic.Map;
using Durango.Model;
using Durango.Network;
using Durango.Terrain;
using Durango.UI;
using Durango.Utils;
using JetBrains.Annotations;
using Messages;
using UnityEngine;
using Yaml;
using Yaml.Util;

public class PetAI : StateBasedAI<PetAI.State>
{
	public enum HungryState
	{
		Good,
		NoBattle,
		NoRide
	}

	public enum State
	{
		Invalid = -1,
		SpawnInCage,
		RoamingInCage,
		IdleInCage,
		SpawnNearMaster,
		Normal,
		Chase,
		Idle,
		Riding,
		Return,
		EatOut,
		Battle,
		Dead,
		Count
	}

	public const float HungryPointForRide = 0f;

	private const float PenetrateAvoidTime = 10f;

	private const float SpawnDistanceFromMaster = 1000f;

	public static readonly float HungryRatioForBattle = Yaml.Util.Singleton<Constants>.Instance.Pet.Battle.HungryRatioEnterBattle;

	private static readonly WaitForSeconds HungryUpdateWaitForSeconds = new WaitForSeconds(1f);

	private static int _globalPosIndex;

	private VehiclePet _vehicle;

	private int _myPosIndex;

	private bool _isMapIndicatorAdded;

	private Vector3 _minArea = Vector3.zero;

	private Vector3 _maxArea = Vector3.zero;

	private GameObject _mealProp;

	private bool _isInitiallyLocated;

	private ICoroutineBinder _binder;

	[CanBeNull]
	private Gauge _hungryGauge;

	private double _latestMovementEndTime;

	protected override State InvalidState => State.Invalid;

	protected override int StateEnumCount => 12;

	public AnimalBehavior TargetAnimal { get; private set; }

	public bool InCage { get; private set; }

	public string OwnerName => _vehicle.OwnerName;

	public int AnimalEntityType { get; private set; }

	private float FollowDistance => _vehicle.FollowDistance;

	private float DistanceThreshould => _vehicle.DistanceThreshould;

	private float MaxFollowDistance => _vehicle.MaxFollowDistance;

	public HungryState Hungry { get; private set; }

	public void SetHungryGauge(Gauge hungry)
	{
		_hungryGauge = hungry;
		UpdateHungryState();
		this.StartCoroutine(ref _binder, CoUpdateHungry());
	}

	private IEnumerator CoUpdateHungry()
	{
		while (_hungryGauge != null)
		{
			float num = _hungryGauge.Get();
			if (num <= 0f)
			{
				yield return HungryUpdateWaitForSeconds;
				continue;
			}
			float seconds = (float)(_hungryGauge.When(num - 1f) - Connections.Frontend.GetPredictedServerTime());
			yield return new WaitForSeconds(seconds);
			UpdateHungryState();
		}
	}

	private void UpdateHungryState()
	{
		if (_hungryGauge != null)
		{
			HungryState hungry = HungryState.Good;
			float num = _hungryGauge.Get();
			float num2 = _hungryGauge.Max();
			if (num <= 0f * num2)
			{
				hungry = HungryState.NoRide;
			}
			else if (num <= HungryRatioForBattle * num2)
			{
				hungry = HungryState.NoBattle;
			}
			Hungry = hungry;
		}
	}

	public void Init(int animalEntityType)
	{
		_vehicle = GetComponent<VehiclePet>();
		AnimalEntityType = animalEntityType;
		InCage = false;
		_minArea = default(Vector3);
		_maxArea = default(Vector3);
		CharacterBehavior component = GetComponent<CharacterBehavior>();
		component.SurvivalGaugeUpdated += SurvivalGaugeUpdated;
		component.SurvivalGaugeInitialized += SurvivalGaugeInitialized;
		TargetAnimal.PathMovable.MovementProcessed += MovementProcessed;
	}

	public void SetInCage(Vector3 minArea, Vector3 maxArea)
	{
		InCage = true;
		_minArea = minArea;
		_maxArea = maxArea;
	}

	public void SetMaster(GameObject master, bool isRiding)
	{
		base.Master = master;
		base.CurState = (isRiding ? State.Riding : ((!InCage) ? State.SpawnNearMaster : State.SpawnInCage));
	}

	private void OnDestroy()
	{
		if (base.CurState != State.Invalid)
		{
			CharacterBehavior component = GetComponent<CharacterBehavior>();
			component.SurvivalGaugeUpdated -= SurvivalGaugeUpdated;
			component.SurvivalGaugeInitialized -= SurvivalGaugeInitialized;
			TargetAnimal.PathMovable.MovementProcessed -= MovementProcessed;
		}
	}

	private void MovementProcessed(Movement movement)
	{
		if (base.CurState != State.Battle)
		{
			BattleBegin();
		}
		_latestMovementEndTime = movement.Path[movement.Path.Length - 1].Time;
	}

	private void SurvivalGaugeUpdated(CharacterBehavior character)
	{
		if (!character.IsAlive && _isInitiallyLocated)
		{
			if (IsLocalPlayersPet())
			{
				GameSystem<PlayGuideSystem>.Instance().NotifyEventOccured("pet", "dead");
			}
			base.CurState = State.Dead;
		}
	}

	private void SurvivalGaugeInitialized(CharacterBehavior character)
	{
		if (!character.IsAlive)
		{
			base.CurState = State.Dead;
			StartCoroutine(SpawnAlreadyDead());
		}
	}

	protected override void OnAwake()
	{
		_myPosIndex = _globalPosIndex;
		_globalPosIndex++;
		TargetAnimal = GetComponent<AnimalBehavior>();
		TargetAnimal.SetActivateRootMotion(active: false);
	}

	protected override IEnumerator OnStart()
	{
		BoneLookAtTarget lookAt = GetComponent<BoneLookAtTarget>();
		if (lookAt != null)
		{
			lookAt.AutoChangeTarget = false;
		}
		while (!TerrainBase.IsPlayerInitialized)
		{
			yield return null;
		}
		if (lookAt != null)
		{
			lookAt.SetLookTarget(base.Master, findHead: true);
		}
		AddToMapIndicator();
	}

	private IEnumerator SpawnAlreadyDead()
	{
		while (!TerrainBase.IsPlayerInitialized)
		{
			yield return new WaitForSeconds(0.5f);
		}
		while (base.Master == null)
		{
			yield return null;
		}
		Locate(base.MasterPos);
		TargetAnimal.TurnToYaw(TargetAnimal.EntityId.GetHashCode() % 360, bSnap: true);
		TargetAnimal.SetActivateRootMotion(active: true);
		string motionName = TargetAnimal.AnimalFrameworkResource.GetAnimationElements("dead")?.FirstOrDefault().Clip;
		TargetAnimal.PlayToLast(motionName);
	}

	private void AddToMapIndicator()
	{
		if (!_isMapIndicatorAdded)
		{
			_isMapIndicatorAdded = true;
			MapIconIndicator orAdd = MapIndicators.GetOrAdd<MapIconIndicator>(TargetAnimal.EntityId, IndicatorType.Pet);
			orAdd.SetTarget(TargetAnimal.gameObject);
			orAdd.SetIcon("icon_map_animal", PresetColor.UISkyBlue, (!IsLocalPlayersPet()) ? 10 : 16, 30);
		}
	}

	protected override void DefineStates()
	{
		AddState(State.SpawnInCage, new StateElem
		{
			Doing = SpawnInCageDoing
		});
		AddState(State.RoamingInCage, new StateElem
		{
			Doing = RoamingInCageDoing
		});
		AddState(State.IdleInCage, new StateElem
		{
			Doing = IdleInCageDoing
		});
		AddState(State.SpawnNearMaster, new StateElem
		{
			Doing = SpawnNearMasterDoing
		});
		AddState(State.Normal, new StateElem
		{
			Entered = NormalEntered,
			Doing = NormalDoing
		});
		AddState(State.Chase, new StateElem
		{
			Doing = ChaseDoing
		});
		AddState(State.Idle, new StateElem
		{
			Doing = IdleDoing
		});
		AddState(State.Riding, new StateElem
		{
			Doing = RidingDoing
		});
		AddState(State.Return, new StateElem
		{
			Doing = ReturnDoing
		});
		AddState(State.EatOut, new StateElem
		{
			Entered = EatOutEntered,
			Doing = EatOutDoing,
			Exited = EatOutExited
		});
		AddState(State.Battle, new StateElem
		{
			Entered = BattleEntered,
			Doing = BattleDoing,
			Exited = BattleExited
		});
		AddState(State.Dead, new StateElem
		{
			Doing = DeadDoing,
			Exited = DeadExited
		});
	}

	protected override bool IsAIEnded()
	{
		return false;
	}

	protected override bool IsTerminalState(State state)
	{
		if (state == State.Return || state == State.Dead)
		{
			return true;
		}
		return false;
	}

	private IEnumerator SpawnNearMasterDoing()
	{
		while (!TerrainBase.IsPlayerInitialized)
		{
			yield return new WaitForSeconds(0.5f);
		}
		while (base.Master == null)
		{
			yield return null;
		}
		Vector3 vector = default(Vector3);
		for (int i = 0; i < 30; i++)
		{
			vector = GetRandomMasterSurroundingPos(1000f);
			if (!Durango.Utils.Singleton<TerrainBase>.Instance().IsCollidableMasked(Util.ClientPositionToWorldPosition(vector)))
			{
				break;
			}
		}
		Locate(vector);
		base.CurState = ((!GetComponent<CharacterBehavior>().IsAlive) ? State.Dead : State.Normal);
	}

	private void Locate(Vector3 newPos, bool randomYaw = true)
	{
		float y = TargetAnimal.ProcessWaterDepth(newPos);
		newPos.y = y;
		TargetAnimal.CurrentPosition = newPos;
		if (randomYaw)
		{
			TargetAnimal.TurnToYaw(UnityEngine.Random.Range(0, 360), bSnap: true);
		}
		_isInitiallyLocated = true;
	}

	public void Tamed()
	{
		base.CurState = State.Normal;
	}

	private void NormalEntered()
	{
		string motionName = TargetAnimal.AnimalFrameworkResource.GetAnimationElements("stand")?.FirstOrDefault().Clip;
		TargetAnimal.CrossFade(motionName, 0.1f);
	}

	private IEnumerator NormalDoing()
	{
		if (!(base.Master == null))
		{
			base.CurState = ((!NeedToChaseMaster()) ? State.Idle : State.Chase);
			yield return new WaitForSeconds(1f);
		}
	}

	private IEnumerator SpawnInCageDoing()
	{
		while (!TerrainBase.IsPlayerInitialized)
		{
			yield return null;
		}
		Locate(CalcRoamingPositionInCage());
		base.CurState = State.IdleInCage;
	}

	private IEnumerator RoamingInCageDoing()
	{
		Pair<string, float> moveClip = GetMoveClip(_vehicle.WalkSpeed);
		AnimalBehavior targetAnimal = TargetAnimal;
		string item = moveClip.Item1;
		float fadeTime = 0.1f;
		float item2 = moveClip.Item2;
		targetAnimal.CrossFade(item, fadeTime, loop: true, 0f, item2);
		float prevTime = Time.time;
		Vector3 destPos = CalcRoamingPositionInCage();
		while (true)
		{
			if (base.Master == null || base.IsInterrupted)
			{
				yield break;
			}
			float num = Time.time - prevTime;
			prevTime = Time.time;
			if (Maths.Make2D(destPos - base.transform.position).magnitude <= 100f)
			{
				break;
			}
			Vector3 normalized = Maths.Make2D(destPos - base.transform.position).normalized;
			TargetAnimal.TurnToYaw(Maths.CalcYaw(normalized), bSnap: false);
			Vector3 vector = normalized * _vehicle.WalkSpeed;
			TargetAnimal.CurrentPosition += vector * num;
			yield return null;
		}
		base.CurState = State.IdleInCage;
	}

	private IEnumerator IdleInCageDoing()
	{
		string motionName = TargetAnimal.AnimalFrameworkResource.GetAnimationElements("stand")?.FirstOrDefault().Clip;
		yield return StartCoroutine(CoPlayMotion(motionName, null, UnityEngine.Random.Range(1, 10)));
		if (!base.IsInterrupted)
		{
			base.CurState = State.RoamingInCage;
		}
	}

	private Vector3 CalcRoamingPositionInCage()
	{
		return new Vector3(UnityEngine.Random.Range(_minArea.x, _maxArea.x), 0f, UnityEngine.Random.Range(_minArea.z, _maxArea.z));
	}

	private bool NeedToChaseMaster()
	{
		return base.DistanceToMaster > FollowDistance + DistanceThreshould;
	}

	private IEnumerator ChaseDoing()
	{
		bool isMoving = false;
		float lastMoveTime = Time.time;
		float prevTime = Time.time;
		AnimationElemBase animationElements = TargetAnimal.AnimalFrameworkResource.GetAnimationElements("stand");
		Pair<string, float> runMotion = GetMoveClip(_vehicle.MoveSpeed);
		string standMotion = animationElements?.FirstOrDefault().Clip;
		while (true)
		{
			if (base.Master == null || base.IsInterrupted)
			{
				yield break;
			}
			float num = Time.time - prevTime;
			prevTime = Time.time;
			float magnitude = Maths.Make2D(base.Master.transform.position - base.transform.position).magnitude;
			if (magnitude <= FollowDistance)
			{
				base.CurState = State.Normal;
				yield break;
			}
			if (magnitude > MaxFollowDistance)
			{
				break;
			}
			Vector3 normalized = Maths.Make2D(CalcChasePosition(base.Master) - base.transform.position).normalized;
			TargetAnimal.TurnToYaw(Maths.CalcYaw(normalized), bSnap: false);
			Vector3 vector = normalized * _vehicle.MoveSpeed;
			Vector3 currentPosition = TargetAnimal.CurrentPosition;
			Vector3 vector2 = ProcessCollisionWithSliding(TargetAnimal.CurrentPosition, vector * num);
			if ((vector2 - currentPosition).magnitude / num / _vehicle.MoveSpeed < 0.7f)
			{
				if (isMoving)
				{
					TargetAnimal.CrossFade(standMotion, 0.1f);
					isMoving = false;
				}
				if (Time.time - lastMoveTime > 10f)
				{
					base.CurState = State.SpawnNearMaster;
				}
			}
			else
			{
				if (!isMoving)
				{
					AnimalBehavior targetAnimal = TargetAnimal;
					string item = runMotion.Item1;
					float fadeTime = 0.1f;
					float item2 = runMotion.Item2;
					targetAnimal.CrossFade(item, fadeTime, loop: true, 0f, item2);
					isMoving = true;
				}
				lastMoveTime = Time.time;
				Locate(vector2, randomYaw: false);
			}
			yield return null;
		}
		base.CurState = State.SpawnNearMaster;
	}

	private Vector3 CalcChasePosition([NotNull] GameObject master)
	{
		float num = (float)((_myPosIndex + 1) / 2) * 20f;
		if (_myPosIndex % 2 == 0)
		{
			num = 0f - num;
		}
		Vector3 vector = Quaternion.Euler(0f, num, 0f) * master.transform.forward;
		Vector3 vector2 = master.transform.position + vector * FollowDistance;
		DebugExtension.DebugCircle(vector2, 50f, 5f);
		return vector2;
	}

	private IEnumerator IdleDoing()
	{
		string motionName = TargetAnimal.AnimalFrameworkResource.GetAnimationElements("stand")?.FirstOrDefault().Clip;
		yield return StartCoroutine(CoPlayMotion(motionName, delegate
		{
			if (NeedToChaseMaster())
			{
				base.CurState = State.Chase;
				return true;
			}
			return false;
		}, 0f));
		if (!base.IsInterrupted)
		{
			base.CurState = State.Normal;
		}
	}

	public void BeginRide()
	{
		TargetAnimal.PathMovable.Clear();
		Locate(base.MasterPos, randomYaw: false);
		base.CurState = State.Riding;
	}

	public void EndRide()
	{
		base.CurState = State.Normal;
	}

	private IEnumerator RidingDoing()
	{
		AnimationElemBase animationElements = TargetAnimal.AnimalFrameworkResource.GetAnimationElements("stand");
		Pair<string, float> runMotion = GetMoveClip(_vehicle.MoveSpeed);
		string standMotion = animationElements?.FirstOrDefault().Clip;
		bool prevMoving = false;
		while (!base.IsInterrupted)
		{
			if (!string.IsNullOrEmpty(_vehicle.StopMotion) && prevMoving && !TargetAnimal.IsMoving)
			{
				yield return StartCoroutine(CoPlayMotion(_vehicle.StopMotion, () => TargetAnimal.IsMoving));
			}
			else if ((bool)TargetAnimal.IsMoving)
			{
				AnimalBehavior targetAnimal = TargetAnimal;
				string item = runMotion.Item1;
				float item2 = runMotion.Item2;
				targetAnimal.CrossFade(item, -1f, loop: true, 0f, item2);
			}
			else
			{
				TargetAnimal.CrossFade(standMotion);
			}
			prevMoving = TargetAnimal.IsMoving;
			yield return null;
		}
	}

	public void Return()
	{
		if (!InCage && base.CurState != State.Return)
		{
			if (base.CurState == State.Dead)
			{
				RemovePet();
			}
			else
			{
				base.CurState = State.Return;
			}
		}
	}

	private IEnumerator ReturnDoing()
	{
		if (base.Master == null)
		{
			RemovePet();
			yield break;
		}
		if (!InCage)
		{
			yield return new WaitForSeconds(2f);
		}
		if (base.Master == null)
		{
			RemovePet();
			yield break;
		}
		Pair<string, float> moveClip = GetMoveClip(_vehicle.MoveSpeed);
		AnimalBehavior targetAnimal = TargetAnimal;
		string item = moveClip.Item1;
		float fadeTime = 0.1f;
		float item2 = moveClip.Item2;
		targetAnimal.CrossFade(item, fadeTime, loop: true, 0f, item2);
		Vector3 vector = Maths.Make2D(base.transform.position - base.Master.transform.position).normalized;
		if (vector == Vector3.zero)
		{
			vector = Vector3.right;
		}
		Vector3 returnPos = base.transform.position + vector * 3000f;
		float prevTime = Time.time;
		while (true)
		{
			float num = Time.time - prevTime;
			prevTime = Time.time;
			if (Maths.Make2D(returnPos - base.transform.position).magnitude <= 100f)
			{
				break;
			}
			vector = Maths.Make2D(returnPos - base.transform.position).normalized;
			TargetAnimal.TurnToYaw(Maths.CalcYaw(vector), bSnap: false);
			Vector3 vector2 = vector * _vehicle.MoveSpeed;
			TargetAnimal.CurrentPosition += vector2 * num;
			yield return null;
		}
		RemovePet();
	}

	public void RemovePet()
	{
		if (base.CurState == State.Riding)
		{
			_vehicle.DetachDriver();
		}
		UnityEngine.Object.Destroy(base.gameObject);
	}

	public void EatOut()
	{
		base.CurState = State.EatOut;
	}

	private void EatOutEntered()
	{
		Durango.Utils.Singleton<AssetBundleManager>.Instance().RequestAsset("Models/Prop/tool/basket_feed_01.prefab", typeof(GameObject), delegate(UnityEngine.Object asset)
		{
			if (!(this == null) && !((GameObject)asset == null))
			{
				GetComponentInChildren<Animation>().Sample();
				Vector3 position = base.transform.position + base.transform.forward * _vehicle.EatDistance;
				position.y = 0f;
				_mealProp = (GameObject)UnityEngine.Object.Instantiate(asset);
				_mealProp.transform.position = position;
				_mealProp.transform.rotation = Quaternion.identity;
			}
		});
	}

	private IEnumerator EatOutDoing()
	{
		string motionName = TargetAnimal.AnimalFrameworkResource.GetAnimationElements("eat")?.FirstOrDefault().Clip;
		yield return StartCoroutine(CoPlayMotion(motionName, null, 10f));
		if (!base.IsInterrupted)
		{
			base.CurState = State.Normal;
		}
	}

	private void EatOutExited()
	{
		UnityEngine.Object.Destroy(_mealProp);
	}

	private void BattleEntered()
	{
		TargetAnimal.SetActivateRootMotion(active: true);
	}

	private IEnumerator BattleDoing()
	{
		while (true)
		{
			if (base.IsInterrupted)
			{
				yield break;
			}
			if (TargetAnimal.GetMoveServerTime() - _latestMovementEndTime > 3.0)
			{
				break;
			}
			yield return null;
		}
		BattleEnd();
	}

	private void BattleExited()
	{
		TargetAnimal.SetActivateRootMotion(active: false);
	}

	private IEnumerator DeadDoing()
	{
		TargetAnimal.SetActivateRootMotion(active: true);
		string clip = TargetAnimal.AnimalFrameworkResource.GetAnimationElements("dead")?.FirstOrDefault().Clip;
		while (true)
		{
			if (TargetAnimal.CurrentAnimClipName != clip)
			{
				TargetAnimal.CrossFade(clip, 0.1f, loop: false);
			}
			if (TargetAnimal.IsAlive)
			{
				break;
			}
			yield return null;
		}
		TransitionTo(State.Normal, force: true);
	}

	private void DeadExited()
	{
		TargetAnimal.SetActivateRootMotion(active: false);
	}

	public bool IsLocalPlayersPet()
	{
		if (PlayerBehavior.LocalPlayer == null)
		{
			return false;
		}
		return base.Master == PlayerBehavior.LocalPlayer.gameObject;
	}

	private IEnumerator CoPlayMotion(string motionName, Func<bool> funcTransition, float length = -1f, float fadeInTime = 0.1f, float playbackRate = 1f)
	{
		bool loop = length >= 0f;
		TargetAnimal.CrossFade(motionName, fadeInTime, loop, 0f, playbackRate);
		if (TargetAnimal.CurAnimState != null)
		{
			length = Mathf.Max(length, TargetAnimal.CurAnimState.length);
		}
		float prevTime = Time.time;
		while (!(Time.time - prevTime >= length) && !base.IsInterrupted && (funcTransition == null || !funcTransition()))
		{
			yield return null;
		}
	}

	private Vector3 ProcessCollisionWithSliding(Vector3 beginPos, Vector3 delta)
	{
		if (delta == Vector3.zero)
		{
			return beginPos;
		}
		delta = Collisions.ProcessSimpleSliding(Collisions.CreateCollisionParam(beginPos, delta));
		return beginPos + delta;
	}

	public void BattleBegin()
	{
		base.CurState = State.Battle;
		_latestMovementEndTime = TargetAnimal.GetMoveServerTime();
	}

	public void BattleEnd()
	{
		base.CurState = State.Normal;
		TargetAnimal.RootMotionMovable.ResetRootMotionOffset();
		TargetAnimal.PathMovable.Clear();
	}

	private Pair<string, float> GetMoveClip(float moveSpeed)
	{
		AnimationElemMoveSet animationElemMoveSet = TargetAnimal.AnimalFrameworkResource.GetAnimationElements("move_motion_sets") as AnimationElemMoveSet;
		MoveSet moveSet = animationElemMoveSet?.elems.FirstOrDefault();
		if (moveSet == null)
		{
			return default(Pair<string, float>);
		}
		if (!string.IsNullOrEmpty(_vehicle.MoveSet))
		{
			foreach (MoveSet elem in animationElemMoveSet.elems)
			{
				if (string.Equals(elem.name, _vehicle.MoveSet, StringComparison.OrdinalIgnoreCase))
				{
					moveSet = elem;
					break;
				}
			}
		}
		MoveMotionInfo moveMotion = moveSet.GetMoveMotion(moveSpeed);
		if (moveMotion == null)
		{
			return default(Pair<string, float>);
		}
		return new Pair<string, float>(moveMotion.FirstOrDefault().Clip, moveSpeed / moveMotion.base_move_speed);
	}
}
