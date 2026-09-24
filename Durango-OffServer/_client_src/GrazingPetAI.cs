using System;
using System.Collections;
using System.Linq;
using Durango.Terrain;
using Durango.Utils;
using Messages;
using UnityEngine;

public class GrazingPetAI : StateBasedAI<GrazingPetAI.State>
{
	public enum State
	{
		Invalid = -1,
		Idle,
		Roming,
		Count
	}

	public AnimalBehavior TargetAnimal { get; private set; }

	protected override State InvalidState => State.Invalid;

	protected override int StateEnumCount => 2;

	public Pet Pet { get; set; }

	private float PlaybackRate
	{
		get
		{
			if (Pet.Stat.PlaybackRate > 0f)
			{
				return Pet.Stat.PlaybackRate;
			}
			return 1f;
		}
	}

	protected override void OnAwake()
	{
		TargetAnimal = GetComponent<AnimalBehavior>();
		TargetAnimal.SetActivateRootMotion(active: false);
	}

	protected override IEnumerator OnStart()
	{
		base.CurState = State.Idle;
		return base.OnStart();
	}

	private void Update()
	{
		Vector3 currentPosition = TargetAnimal.CurrentPosition;
		currentPosition.y = TargetAnimal.ProcessWaterDepth(currentPosition);
		TargetAnimal.CurrentPosition = currentPosition;
	}

	protected override void DefineStates()
	{
		AddState(State.Idle, new StateElem
		{
			Doing = OnIdle
		});
		AddState(State.Roming, new StateElem
		{
			Doing = OnRoming
		});
	}

	private IEnumerator OnIdle()
	{
		while (true)
		{
			float value = UnityEngine.Random.value;
			if (value > 0.5f)
			{
				break;
			}
			float seconds = 1f;
			if (value < 0.2f)
			{
				if (TargetAnimal.AnimalFrameworkResource.GetAnimationElements("idle") is AnimationElem animationElem)
				{
					TargetAnimal.Play(animationElem.motion, loop: true, 0f, PlaybackRate);
					seconds = animationElem.Clip.length / PlaybackRate;
				}
			}
			else if (TargetAnimal.AnimalFrameworkResource.GetAnimationElements("stand") is AnimationElem animationElem2)
			{
				TargetAnimal.Play(animationElem2.motion, loop: true, 0f, PlaybackRate);
				float num = animationElem2.Clip.length / PlaybackRate;
				seconds = UnityEngine.Random.Range(num, num * 3f);
			}
			yield return new WaitForSeconds(seconds);
		}
		base.CurState = State.Roming;
	}

	private IEnumerator OnRoming()
	{
		while (!(UnityEngine.Random.value > 0.7f))
		{
			MoveSet moveSet = ((TargetAnimal.AnimalFrameworkResource.GetAnimationElements("move_motion_sets") is AnimationElemMoveSet animationElemMoveSet) ? animationElemMoveSet.elems.FirstOrDefault() : null);
			if (moveSet != null)
			{
				MoveMotionInfo moveMotion = moveSet.GetMoveMotion(0f);
				TargetAnimal.SetRotateSpeed(moveMotion.rot_speed);
				TargetAnimal.Play(moveMotion.motion, loop: true, 0f, PlaybackRate);
				float moveSpeed = moveMotion.base_move_speed;
				float yaw = UnityEngine.Random.value * 360f;
				float timer = UnityEngine.Random.Range(2f, 7f);
				TargetAnimal.TurnToYaw(yaw, bSnap: false);
				while (timer > 0f)
				{
					float currentYaw = TargetAnimal.CurrentYaw;
					Vector3 delta = new Vector3(Mathf.Sin(currentYaw * ((float)Math.PI / 180f)), 0f, Mathf.Cos(currentYaw * ((float)Math.PI / 180f))) * moveSpeed * Time.deltaTime;
					Vector3 currentPosition = TargetAnimal.CurrentPosition;
					Vector3 vector = ProcessCollisionWithSliding(TargetAnimal.CurrentPosition, delta);
					Vector2 floatTile = Util.ClientPositionToTilePosition(vector);
					if (TerrainWater.IsTooDeepToSwim(Singleton<TerrainBase>.Instance().GetTileDepth(floatTile), 0f))
					{
						base.CurState = State.Idle;
						yield break;
					}
					if ((vector - currentPosition).magnitude / Time.deltaTime / moveSpeed < 0.7f)
					{
						base.CurState = State.Idle;
						yield break;
					}
					TargetAnimal.CurrentPosition = vector;
					timer -= Time.deltaTime;
					yield return null;
				}
			}
			yield return null;
		}
		base.CurState = State.Idle;
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

	protected override bool IsAIEnded()
	{
		return false;
	}

	protected override bool IsTerminalState(State state)
	{
		return false;
	}
}
