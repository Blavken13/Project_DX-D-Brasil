using System;
using System.Collections;
using Durango.Utils;
using UnityEngine;

namespace Durango.Render;

public class ContactShadowModel : MonoBehaviour
{
	public Action<ContactShadowModel> OnRemove;

	[SerializeField]
	public Vector3 FootShadowOffset;

	[SerializeField]
	public float FootShadowRotBiasLeft;

	[SerializeField]
	public float FootShadowRotRatioLeft;

	[SerializeField]
	public float FootShadowRotBiasRight;

	[SerializeField]
	public float FootShadowRotRatioRight;

	[SerializeField]
	public Vector3 CenterShadowOffset;

	[SerializeField]
	public Vector3 CenterShadowRot;

	[SerializeField]
	public float ShadowRemoveHeight;

	[SerializeField]
	private GameObject _leftFootShadow;

	[SerializeField]
	private GameObject _rightFootShadow;

	[SerializeField]
	private GameObject _centerShadow;

	private static WaitForSeconds _wairForSeconds = new WaitForSeconds(0.3f);

	private static WaitForSeconds _wairForSecondsRapid = new WaitForSeconds(0.016f);

	public GameObject Target { get; set; }

	public bool IsRapidUpdateMode { get; set; }

	public bool DestroyIfInvisible { get; set; }

	private IEnumerator Start()
	{
		if (PlayerBehavior.LocalPlayer.gameObject == base.gameObject)
		{
			while (PlayerBehavior.LocalPlayer == null || !PlayerBehavior.LocalPlayer.gameObject.activeSelf)
			{
				yield return new WaitForSeconds(0.1f);
			}
			Target = PlayerBehavior.LocalPlayer.gameObject;
		}
		if (Target == null)
		{
			yield break;
		}
		Transform leftFoot = KUtility.FindTransformByName(Target, "Bip001_L_Foot");
		Transform rightFoot = KUtility.FindTransformByName(Target, "Bip001_R_Foot");
		CharacterBehavior character = Target.GetComponent<CharacterBehavior>();
		while (Target != null && Target.activeInHierarchy && character != null)
		{
			if (!character.WillBeRendered)
			{
				yield return null;
			}
			if (leftFoot == null || rightFoot == null || Target == null || (DestroyIfInvisible && Target.transform.position.y > ShadowRemoveHeight))
			{
				if (OnRemove != null)
				{
					OnRemove(this);
				}
				yield break;
			}
			float y = Mathf.Max(0f, Target.transform.position.y);
			Vector3 vector = CalcContactPosition(leftFoot.position);
			vector.y = y;
			Vector3 vector2 = Maths.Make2D(leftFoot.position);
			Vector3 vector3 = CalcContactPosition(rightFoot.position);
			vector3.y = y;
			Vector3 vector4 = Maths.Make2D(rightFoot.position);
			Vector3 position = (vector2 + vector4) * 0.5f;
			position.y = y;
			base.gameObject.transform.position = position;
			_leftFootShadow.transform.position = vector + FootShadowOffset;
			float y2 = _leftFootShadow.transform.localPosition.x * FootShadowRotRatioLeft + FootShadowRotBiasLeft;
			_leftFootShadow.transform.localRotation = Quaternion.Euler(0f, y2, 0f);
			_rightFootShadow.transform.position = vector3 + FootShadowOffset;
			float y3 = _rightFootShadow.transform.localPosition.x * FootShadowRotRatioRight + FootShadowRotBiasRight;
			_rightFootShadow.transform.localRotation = Quaternion.Euler(0f, y3, 0f);
			_centerShadow.transform.position = (vector2 + vector4) * 0.5f + CenterShadowOffset;
			_centerShadow.transform.localRotation = Quaternion.Euler(CenterShadowRot);
			if (IsRapidUpdateMode || (vector - leftFoot.position).sqrMagnitude > 5f)
			{
				yield return _wairForSecondsRapid;
			}
			else
			{
				yield return _wairForSeconds;
			}
		}
		if (OnRemove != null)
		{
			OnRemove(this);
		}
	}

	private static Vector3 CalcContactPosition(Vector3 pos)
	{
		return pos;
	}
}
