using System.Collections;
using Durango.Model;
using Durango.Terrain;
using Durango.Utils;
using UnityEngine;

public class NpcAIK : StateBasedAI<NpcAIK.State>
{
	public enum State
	{
		Invalid = -1,
		Normal,
		Chase,
		Run,
		Count
	}

	[SerializeField]
	private string _standMotion = "F_Barehand_Stand";

	[SerializeField]
	private string _moveMotion = "F_Barehand_Run";

	[SerializeField]
	private float _engageDistance = 450f;

	[SerializeField]
	private float _moveSpeed = 500f;

	[SerializeField]
	private float _appearDiatanceFromPlayer = 1000f;

	private Vector3 _initialPos;

	private GameObject _victim;

	private AnimalBehavior _targetAnimal;

	protected override State InvalidState => State.Invalid;

	protected override int StateEnumCount => 3;

	private AnimalBehavior TargetAnimal
	{
		get
		{
			if (null == _targetAnimal)
			{
				_targetAnimal = GetComponent<AnimalBehavior>();
			}
			return _targetAnimal;
		}
	}

	protected override void DefineStates()
	{
		AddState(State.Normal, new StateElem
		{
			Entered = NormalEntered,
			Doing = NormalDoing,
			Exited = NormalExited
		});
		AddState(State.Chase, new StateElem
		{
			Entered = ChaseEntered,
			Doing = ChaseDoing,
			Exited = ChaseExited
		});
		AddState(State.Run, new StateElem
		{
			Entered = RunEntered,
			Doing = RunDoing,
			Exited = RunExited
		});
	}

	protected override IEnumerator OnStart()
	{
		TargetAnimal.EntityId = "666";
		base.CurState = State.Normal;
		GetComponent<BoneLookAtTarget>().AutoChangeTarget = false;
		while (!TerrainBase.IsPlayerInitialized)
		{
			yield return null;
		}
		Vector3 vector = Util.WorldPositionToClientPosition(new Vector3(512f, 512f));
		_initialPos = PlayerBehavior.LocalPlayer.CurrentPosition + (vector - PlayerBehavior.LocalPlayer.CurrentPosition).normalized * _appearDiatanceFromPlayer;
		_initialPos.y = 0f;
		TargetAnimal.CurrentPosition = _initialPos;
	}

	protected override IEnumerator OnBeforeDoingState()
	{
		BoneLookAtTarget component = GetComponent<BoneLookAtTarget>();
		_victim = PlayerBehavior.LocalPlayer.gameObject;
		if (_victim == null)
		{
			yield return new WaitForSeconds(1f);
		}
		else
		{
			component.SetLookTarget(PlayerBehavior.LocalPlayer.gameObject, findHead: true);
		}
	}

	protected override IEnumerator OnAfterDoingState()
	{
		yield break;
	}

	protected override bool IsAIEnded()
	{
		return false;
	}

	protected override bool IsTerminalState(State state)
	{
		return false;
	}

	private void NormalEntered()
	{
		TargetAnimal.CrossFade(_standMotion, 0.1f);
	}

	private void NormalExited()
	{
	}

	private IEnumerator NormalDoing()
	{
		if (null != _victim && (_victim.transform.position - base.transform.position).magnitude > _engageDistance)
		{
			base.CurState = State.Chase;
		}
		yield return new WaitForSeconds(0.3f);
	}

	private void ChaseEntered()
	{
	}

	private void ChaseExited()
	{
	}

	private IEnumerator ChaseDoing()
	{
		TargetAnimal.CrossFade(_moveMotion, 0.1f);
		float prevTime = Time.time;
		while (true)
		{
			if (null == _victim || base.IsInterrupted)
			{
				yield break;
			}
			float num = Time.time - prevTime;
			prevTime = Time.time;
			Vector3 vector = Maths.Make2D(_victim.transform.position - base.transform.position);
			if (vector.magnitude <= _engageDistance)
			{
				break;
			}
			float yaw = Maths.CalcYawWithTarget(_victim.transform.position, base.transform.position);
			TargetAnimal.TurnToYaw(yaw, bSnap: false);
			Vector3 vector2 = vector.normalized * _moveSpeed;
			TargetAnimal.CurrentPosition += vector2 * num;
			yield return null;
		}
		base.CurState = State.Normal;
	}

	private void RunEntered()
	{
	}

	private void RunExited()
	{
	}

	private IEnumerator RunDoing()
	{
		TargetAnimal.CrossFade(_moveMotion, 0.1f);
		float prevTime = Time.time;
		while (true)
		{
			if (null == _victim || base.IsInterrupted)
			{
				yield break;
			}
			float num = Time.time - prevTime;
			prevTime = Time.time;
			Vector3 vector = Maths.Make2D(_initialPos - base.transform.position);
			if (vector.magnitude <= _engageDistance)
			{
				break;
			}
			float yaw = Maths.CalcYawWithTarget(_initialPos, base.transform.position);
			TargetAnimal.TurnToYaw(yaw, bSnap: false);
			Vector3 vector2 = vector.normalized * _moveSpeed;
			TargetAnimal.CurrentPosition += vector2 * num;
			yield return null;
		}
		Object.Destroy(base.gameObject);
	}

	public void EventRun()
	{
		base.CurState = State.Run;
	}
}
