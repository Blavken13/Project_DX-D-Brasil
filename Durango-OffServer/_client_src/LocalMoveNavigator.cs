using System;
using System.Collections.Generic;
using Durango.Terrain;
using Durango.Utils;
using UnityEngine;

public class LocalMoveNavigator
{
	public enum MoveType
	{
		Stop,
		MoveInDirection,
		MoveToPosition,
		MoveToTarget
	}

	public enum YawType
	{
		None,
		TargetYaw,
		TargetPosition
	}

	public class MoveParam
	{
		public MoveType MoveType;

		public float DistanceThreshold;

		public Vector3 TargetPos;

		public Vector3 MovingDir;

		public bool HasGoal;

		public GameObject TargetObj;

		public Action OnComplete;

		public bool CompleteIfBlocked;

		public void Reset()
		{
			MoveType = MoveType.Stop;
			HasGoal = false;
			TargetObj = null;
			DistanceThreshold = 0f;
			TargetPos = default(Vector3);
			MovingDir = default(Vector3);
			OnComplete = null;
			CompleteIfBlocked = false;
		}
	}

	public class YawParam
	{
		public YawType YawType;

		public float TargetYaw = -999f;

		public Vector3 TargetPosition;

		public bool IsSnapYaw;

		public void Reset()
		{
			YawType = YawType.None;
			TargetYaw = -999f;
			TargetPosition = Vector3.zero;
			IsSnapYaw = false;
		}
	}

	public const float InvalidYaw = -999f;

	private readonly MoveParam _moveParam = new MoveParam();

	private readonly YawParam _yawParam = new YawParam();

	private const float WaypointThreshold = 60f;

	private const float WaypointBlockedDistance = 150f;

	private const int MaxReplans = 12;

	private readonly List<Vector3> _waypoints = new List<Vector3>();

	private bool _following;

	private Vector3 _routeTargetPos;

	private GameObject _routeTargetObj;

	private Action _routeOnComplete;

	private float _routeThreshold;

	private bool _routeCompleteIfBlocked;

	private Vector3 _currentWaypoint;

	private int _replans;

	public static bool RoutingEnabled = true;

	private static PlayerBehavior Player => PlayerBehavior.LocalPlayer;

	public bool IsFollowingRoute => _following;

	public void MoveOperator_MovingBlocked()
	{
		_yawParam.Reset();
		_moveParam.Reset();
	}

	public void MoveOperator_TargetYawReached()
	{
		_yawParam.Reset();
	}

	public void MoveOperator_CollisionSlideOccurred(Vector3 slidingDelta)
	{
		_yawParam.YawType = YawType.TargetYaw;
		_yawParam.TargetYaw = Maths.CalcYaw(slidingDelta);
	}

	public MoveParam GetMoveParam()
	{
		return _moveParam;
	}

	public YawParam GetYawParam()
	{
		return _yawParam;
	}

	public void UpdateTargetPosition(Vector3 lastPos)
	{
		if (_moveParam.MoveType == MoveType.MoveToTarget && _yawParam.YawType != YawType.TargetYaw && (bool)_moveParam.TargetObj)
		{
			Vector3 interactionPosition = InteractionObject.GetInteractionPosition(_moveParam.TargetObj);
			if (Vector3.SqrMagnitude(interactionPosition - _moveParam.TargetPos) > Mathf.Epsilon)
			{
				_moveParam.TargetPos = interactionPosition;
				RotateToPosition(_moveParam.TargetPos);
			}
		}
		if (_yawParam.YawType == YawType.TargetPosition)
		{
			_yawParam.YawType = YawType.TargetYaw;
			_yawParam.TargetYaw = Maths.CalcYawWithTarget(_yawParam.TargetPosition, lastPos);
		}
	}

	public void Stop()
	{
		CancelRoute();
		if (Player.LookAtController != null)
		{
			Player.LookAtController.SetLookTarget(null);
		}
		_moveParam.Reset();
		if (!_yawParam.IsSnapYaw)
		{
			_yawParam.Reset();
		}
	}

	public void MoveInDirection(Vector3 dir)
	{
		CancelRoute();
		_moveParam.TargetObj = null;
		if (Player.LookAtController != null)
		{
			Player.LookAtController.SetLookTarget(null);
		}
		_moveParam.Reset();
		_moveParam.MoveType = MoveType.MoveInDirection;
		_moveParam.MovingDir = dir;
		SetTargetYaw(Maths.CalcYaw(dir));
	}

	public void MoveToPosition(Vector3 pos, Action onComplete, float distanceThreshold, bool completeIfBlocked)
	{
		if (Player.IsAlive && !GameSystem<InputSystem>.Instance().MoveLock && !TryPlanRoute(pos, null, onComplete, distanceThreshold, completeIfBlocked))
		{
			MoveToPositionDirect(pos, onComplete, distanceThreshold, completeIfBlocked);
		}
	}

	public void MoveToTarget(GameObject targetObj, Action onComplete, float distanceThreshold, bool completeIfBlocked)
	{
		if (Player.IsAlive && !GameSystem<InputSystem>.Instance().MoveLock && !(targetObj == null))
		{
			Vector3 interactionPosition = InteractionObject.GetInteractionPosition(targetObj);
			if (!TryPlanRoute(interactionPosition, targetObj, onComplete, distanceThreshold, completeIfBlocked))
			{
				MoveToTargetDirect(targetObj, onComplete, distanceThreshold, completeIfBlocked);
			}
		}
	}

	private void MoveToPositionDirect(Vector3 pos, Action onComplete, float distanceThreshold, bool completeIfBlocked)
	{
		FillMoveTargetParam(pos, distanceThreshold, null, onComplete, completeIfBlocked);
		_moveParam.MoveType = MoveType.MoveToPosition;
	}

	private void MoveToTargetDirect(GameObject targetObj, Action onComplete, float distanceThreshold, bool completeIfBlocked)
	{
		Vector3 interactionPosition = InteractionObject.GetInteractionPosition(targetObj);
		FillMoveTargetParam(interactionPosition, distanceThreshold, targetObj, onComplete, completeIfBlocked);
		_moveParam.MoveType = MoveType.MoveToTarget;
	}

	private bool TryPlanRoute(Vector3 targetPos, GameObject targetObj, Action onComplete, float distanceThreshold, bool completeIfBlocked)
	{
		CancelRoute();
		if (!RoutingEnabled || GameManager.IsPrologueMode)
		{
			return false;
		}
		NavGrid.EnsureLoaded();
		if (!NavGrid.Ready)
		{
			return false;
		}
		NavGrid.ClearTempBlocked();
		List<Vector3> list = NavGrid.FindWaypoints(Player.CurrentPosition, targetPos);
		if (list == null)
		{
			return false;
		}
		if (targetObj == null && NavGrid.LastAdjustedTarget.HasValue)
		{
			targetPos = NavGrid.LastAdjustedTarget.Value;
		}
		if (list.Count == 0 && !NavGrid.LastAdjustedTarget.HasValue)
		{
			return false;
		}
		_routeTargetPos = targetPos;
		_routeTargetObj = targetObj;
		_routeOnComplete = onComplete;
		_routeThreshold = distanceThreshold;
		_routeCompleteIfBlocked = completeIfBlocked;
		_replans = 0;
		_waypoints.Clear();
		_waypoints.AddRange(list);
		_following = true;
		NextWaypoint();
		return true;
	}

	private void CancelRoute()
	{
		_following = false;
		_waypoints.Clear();
		_routeTargetObj = null;
		_routeOnComplete = null;
	}

	private void NextWaypoint()
	{
		if (!_following)
		{
			return;
		}
		if (_waypoints.Count == 0)
		{
			GameObject routeTargetObj = _routeTargetObj;
			Action routeOnComplete = _routeOnComplete;
			float routeThreshold = _routeThreshold;
			bool routeCompleteIfBlocked = _routeCompleteIfBlocked;
			Vector3 routeTargetPos = _routeTargetPos;
			CancelRoute();
			if (routeTargetObj != null)
			{
				MoveToTargetDirect(routeTargetObj, routeOnComplete, routeThreshold, routeCompleteIfBlocked);
			}
			else
			{
				MoveToPositionDirect(routeTargetPos, routeOnComplete, routeThreshold, routeCompleteIfBlocked);
			}
		}
		else
		{
			_currentWaypoint = _waypoints[0];
			_waypoints.RemoveAt(0);
			FillMoveTargetParam(_currentWaypoint, 60f, null, OnWaypointReached, completeIfBlocked: true);
			_moveParam.MoveType = MoveType.MoveToPosition;
		}
	}

	private void OnWaypointReached()
	{
		if (!_following)
		{
			return;
		}
		Vector3 currentPosition = Player.CurrentPosition;
		Vector3 vector = _currentWaypoint - currentPosition;
		vector.y = 0f;
		if (vector.magnitude > 150f)
		{
			NavGrid.MarkTempBlocked(currentPosition + vector.normalized * 200f);
		}
		if (_replans < 12)
		{
			_replans++;
			List<Vector3> list = NavGrid.FindWaypoints(currentPosition, _routeTargetPos);
			if (list != null)
			{
				_waypoints.Clear();
				_waypoints.AddRange(list);
				if (_routeTargetObj == null && NavGrid.LastAdjustedTarget.HasValue)
				{
					_routeTargetPos = NavGrid.LastAdjustedTarget.Value;
				}
			}
		}
		NextWaypoint();
	}

	private void FillMoveTargetParam(Vector3 targetPos, float distanceThreshold, GameObject targetObj, Action onComplete, bool completeIfBlocked)
	{
		_moveParam.HasGoal = true;
		_moveParam.TargetPos = targetPos;
		_moveParam.MovingDir = default(Vector3);
		_moveParam.DistanceThreshold = distanceThreshold;
		_moveParam.TargetObj = targetObj;
		_moveParam.OnComplete = onComplete;
		_moveParam.CompleteIfBlocked = completeIfBlocked;
		if (Player.LookAtController != null)
		{
			Player.LookAtController.SetLookTarget(targetObj);
		}
		RotateToPosition(_moveParam.TargetPos);
	}

	public void RotateToObject(GameObject target, bool snap = false)
	{
		if (!(target == null))
		{
			Vector3 interactionPosition = InteractionObject.GetInteractionPosition(target);
			RotateToPosition(interactionPosition, snap);
		}
	}

	public void RotateToPosition(Vector3 pos, bool snap = false)
	{
		_yawParam.YawType = YawType.TargetPosition;
		_yawParam.TargetPosition = pos;
		_yawParam.IsSnapYaw = snap;
	}

	public void SetTargetYaw(float yaw, bool snap = false)
	{
		_yawParam.YawType = YawType.TargetYaw;
		_yawParam.TargetYaw = yaw;
		_yawParam.IsSnapYaw = snap;
	}
}
