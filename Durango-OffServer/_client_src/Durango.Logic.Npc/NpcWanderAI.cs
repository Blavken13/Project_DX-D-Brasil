using System;
using System.Collections;
using System.Collections.Generic;
using Durango.Player.Animation;
using Durango.Terrain;
using Durango.Utils;
using UnityEngine;

namespace Durango.Logic.Npc;

public class NpcWanderAI : MonoBehaviour
{
	public float RunSpeed = 500f;

	public float WanderRadiusTiles = 6f;

	public float ReactDistance = 350f;

	public float ActivityChance = 0.5f;

	public string NpcId = string.Empty;

	public string DisplayName = string.Empty;

	public string PortraitIcon = string.Empty;

	private Vector2 _homeTile;

	private CharacterBehavior _character;

	private AnimalBehavior _animal;

	private PlayerBehavior _player;

	private float _talkPauseUntil;

	private Transform _faceTarget;

	private Coroutine _loop;

	private bool _isWalking;

	private string _activity;

	private float _activityUntil;

	private bool _hasStand;

	private bool _hasRun;

	public CharacterBehavior Character => _character;

	public Vector2 HomeTile => _homeTile;

	public string CurrentActivity => _activity;

	public bool IsIdle
	{
		get
		{
			if (!_isWalking && _activity == null)
			{
				return Time.time >= _talkPauseUntil;
			}
			return false;
		}
	}

	public bool IsBusyTalking
	{
		get
		{
			if (Time.time < _talkPauseUntil)
			{
				return _faceTarget != null;
			}
			return false;
		}
	}

	public static NpcWanderAI Attach(GameObject host, Vector2 homeTile, float radiusTiles, string npcId, string displayName, string portraitIcon)
	{
		NpcWanderAI obj = host.GetComponent<NpcWanderAI>() ?? host.AddComponent<NpcWanderAI>();
		obj._homeTile = homeTile;
		obj.WanderRadiusTiles = radiusTiles;
		obj.NpcId = npcId;
		obj.DisplayName = displayName;
		obj.PortraitIcon = portraitIcon;
		return obj;
	}

	public void OnTalked()
	{
		StopActivity();
		_talkPauseUntil = Mathf.Max(_talkPauseUntil, Time.time + 6f);
		_faceTarget = null;
	}

	public void BeginTalkWith(Transform partner, float seconds)
	{
		StopActivity();
		_faceTarget = partner;
		_talkPauseUntil = Time.time + seconds;
	}

	public bool ForceActivity(string clip)
	{
		if (!ClipInfo(clip, out var length, out var loop))
		{
			return false;
		}
		_activity = clip;
		_activityUntil = Time.time + (loop ? 12f : Mathf.Max(length, 1f));
		_isWalking = false;
		return PlayClip(clip, loop);
	}

	private void Start()
	{
		_character = GetComponent<CharacterBehavior>();
		_animal = _character as AnimalBehavior;
		_player = _character as PlayerBehavior;
		if (_character == null)
		{
			base.enabled = false;
			return;
		}
		if (_animal != null)
		{
			_animal.SetActivateRootMotion(active: false);
			_hasStand = HasClip("Barehand_Stand");
			_hasRun = HasClip("Barehand_Run");
			if (_hasStand && !_hasRun)
			{
			}
		}
		else
		{
			_hasStand = true;
			_hasRun = true;
		}
		PlayStand();
		_loop = StartCoroutine(Loop());
	}

	private void OnDisable()
	{
		if (_loop != null)
		{
			StopCoroutine(_loop);
			_loop = null;
		}
	}

	private string AnimalClipName(string clip)
	{
		return "F_" + clip;
	}

	private bool HasClip(string clip)
	{
		if (_animal != null)
		{
			if (_animal.Anim != null)
			{
				return _animal.Anim.GetClip(AnimalClipName(clip)) != null;
			}
			return false;
		}
		return Singleton<PlayerAnimationClipManager>.Instance().GetPlayerAnimationClipInfo(clip) != null;
	}

	private static bool ClipInfo(string clip, out float length, out bool loop)
	{
		length = 2f;
		loop = false;
		PlayerAnimationClipInfo playerAnimationClipInfo = Singleton<PlayerAnimationClipManager>.Instance().GetPlayerAnimationClipInfo(clip);
		if (playerAnimationClipInfo == null)
		{
			return false;
		}
		length = playerAnimationClipInfo.Length;
		loop = playerAnimationClipInfo.IsLoop;
		return true;
	}

	private bool PlayClip(string clip, bool loop, float rate = 1f)
	{
		if (_animal != null)
		{
			string motionName = AnimalClipName(clip);
			if (_animal.Anim == null || _animal.Anim.GetClip(motionName) == null)
			{
				return false;
			}
			_animal.CrossFade(motionName, 0.2f, loop, 0f, rate);
			return true;
		}
		if (_player != null)
		{
			_player.PlayMotionForcely(clip, rate, immediately: true);
			return true;
		}
		return false;
	}

	private void PlayStand()
	{
		_isWalking = false;
		if (_hasStand)
		{
			PlayClip("Barehand_Stand", loop: true);
		}
	}

	private void PlayRun()
	{
		_isWalking = true;
		if (_hasRun)
		{
			PlayClip("Barehand_Run", loop: true);
		}
	}

	private void StopActivity()
	{
		if (_activity != null)
		{
			_activity = null;
			PlayStand();
		}
	}

	private bool PlayerNear(out Vector3 playerPos)
	{
		playerPos = Vector3.zero;
		PlayerBehavior localPlayer = PlayerBehavior.LocalPlayer;
		if (localPlayer == null)
		{
			return false;
		}
		playerPos = localPlayer.CurrentPosition;
		Vector3 vector = playerPos - _character.CurrentPosition;
		vector.y = 0f;
		return vector.magnitude < ReactDistance;
	}

	private bool MustHold(out Vector3 faceAt)
	{
		faceAt = Vector3.zero;
		if (Time.time < _talkPauseUntil)
		{
			if (_faceTarget != null)
			{
				faceAt = _faceTarget.position;
			}
			else
			{
				PlayerNear(out faceAt);
			}
			return true;
		}
		return PlayerNear(out faceAt);
	}

	private void FaceTo(Vector3 at)
	{
		if (at != Vector3.zero)
		{
			_character.TurnToYaw(Maths.CalcYawWithTarget(at, base.transform.position), bSnap: false);
		}
	}

	private IEnumerator Loop()
	{
		yield return new WaitForSeconds(UnityEngine.Random.Range(0.5f, 3f));
		while (true)
		{
			PlayStand();
			float until = Time.time + UnityEngine.Random.Range(3f, 8f);
			Vector3 faceAt;
			while (Time.time < until || MustHold(out faceAt))
			{
				if (MustHold(out faceAt))
				{
					FaceTo(faceAt);
				}
				yield return null;
			}
			if (UnityEngine.Random.value < ActivityChance)
			{
				string[] array = NpcSpawner.ActivitiesOf(NpcId);
				string text = array[UnityEngine.Random.Range(0, array.Length)];
				if (ClipInfo(text, out var length, out var loop) && HasClip(text))
				{
					_activity = text;
					_activityUntil = Time.time + (loop ? UnityEngine.Random.Range(8f, 20f) : (Mathf.Max(length, 1f) + 0.3f));
					if (PlayClip(text, loop))
					{
						while (_activity != null && Time.time < _activityUntil)
						{
							yield return null;
						}
					}
					if (_activity != null)
					{
						_activity = null;
						PlayStand();
						yield return new WaitForSeconds(UnityEngine.Random.Range(1f, 3f));
					}
					continue;
				}
			}
			if (!PickDestination(out var dest))
			{
				continue;
			}
			List<Vector3> path = BuildPath(dest);
			PlayRun();
			float prev = Time.time;
			for (int i = 0; i < path.Count; i++)
			{
				Vector3 target = path[i];
				target.y = _character.CurrentPosition.y;
				while (!MustHold(out faceAt))
				{
					float num = Time.time - prev;
					prev = Time.time;
					Vector3 currentPosition = _character.CurrentPosition;
					Vector3 vector = target - currentPosition;
					vector.y = 0f;
					if (vector.magnitude < 20f)
					{
						break;
					}
					_character.TurnToYaw(Maths.CalcYawWithTarget(target, currentPosition), bSnap: false);
					Vector3 vector2 = vector.normalized * (RunSpeed * num);
					if (vector2.magnitude > vector.magnitude)
					{
						vector2 = vector;
					}
					Vector3 vector3 = currentPosition + vector2;
					vector3.y = LocalMoveOperator.GetWorldHeight(vector3, 0, 0f) ?? currentPosition.y;
					_character.CurrentPosition = vector3;
					yield return null;
				}
				if (MustHold(out faceAt))
				{
					break;
				}
			}
			_isWalking = false;
		}
	}

	private bool PickDestination(out Vector3 dest)
	{
		dest = Vector3.zero;
		NavGrid.EnsureLoaded();
		for (int i = 0; i < 8; i++)
		{
			float f = UnityEngine.Random.Range(0f, (float)Math.PI * 2f);
			float num = UnityEngine.Random.Range(1.5f, WanderRadiusTiles);
			Vector2 tilePosition = new Vector2(Mathf.Round(_homeTile.x + Mathf.Cos(f) * num), Mathf.Round(_homeTile.y + Mathf.Sin(f) * num));
			if (!NavGrid.Ready || NavGrid.CostAt((int)tilePosition.x, (int)tilePosition.y) > 0)
			{
				dest = Util.TilePositionToClientPosition(tilePosition, tileCenter: true);
				return true;
			}
		}
		return false;
	}

	private List<Vector3> BuildPath(Vector3 dest)
	{
		List<Vector3> list = new List<Vector3>();
		NavGrid.EnsureLoaded();
		if (NavGrid.Ready)
		{
			List<Vector2> list2 = NavGrid.FindPathTiles(_character.CurrentPosition, dest);
			if (list2 != null)
			{
				for (int i = 1; i < list2.Count; i++)
				{
					list.Add(Util.TilePositionToClientPosition(list2[i], tileCenter: true));
				}
			}
		}
		list.Add(dest);
		return list;
	}
}
