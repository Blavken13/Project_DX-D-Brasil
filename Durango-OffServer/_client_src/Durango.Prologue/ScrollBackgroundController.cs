using System.Collections;
using System.Collections.Generic;
using Durango.Utils;
using UnityEngine;

namespace Durango.Prologue;

public class ScrollBackgroundController : Singleton<ScrollBackgroundController>
{
	private List<GameObject> _objects = new List<GameObject>();

	[SerializeField]
	private float _speed = 300000f;

	[SerializeField]
	private float _blockSize = 2000f;

	public Color _curBGColor = Color.white;

	public Color _curTreeColor = Color.white;

	public Color _curGodRayColor = Color.white;

	[SerializeField]
	private Color _dayBGColor = Color.white;

	[SerializeField]
	private Color _daytTreeColor = new Color(0.39f, 0.39f, 0.66f, 1f);

	[SerializeField]
	private Color _nightBGColor = new Color(0.12f, 0.12f, 0.2f, 1f);

	[SerializeField]
	private Color _nightTreeColor = new Color(0.12f, 0.12f, 0.2f, 1f);

	[SerializeField]
	private List<GameObject> tree_groups_normal = new List<GameObject>();

	[SerializeField]
	private List<GameObject> tree_groups_thunder = new List<GameObject>();

	[SerializeField]
	private List<GameObject> _godRays = new List<GameObject>();

	private WaitForSeconds _waitForSeconds = new WaitForSeconds(0.03f);

	private IEnumerator Start()
	{
		_curBGColor = _dayBGColor;
		_curTreeColor = _daytTreeColor;
		SetTreeVisible(bNormal: true, bThunder: false);
		int count = base.transform.GetChild(0).childCount;
		for (int i = 0; i < count; i++)
		{
			_objects.Add(base.transform.GetChild(0).GetChild(i).gameObject);
		}
		_objects.Sort((GameObject v1, GameObject v2) => (int)(v2.transform.localPosition.z - v1.transform.localPosition.z));
		float bound = _objects[count - 1].transform.localPosition.z - _blockSize;
		float prevTime = Time.time;
		int godRayCount = _godRays.Count;
		while (true)
		{
			float num = Time.time - prevTime;
			prevTime = Time.time;
			for (int num2 = 0; num2 < count; num2++)
			{
				Vector3 localPosition = _objects[num2].transform.localPosition;
				localPosition.z = (localPosition.z - num * _speed) % bound;
				_objects[num2].GetComponent<Renderer>().material.color = _curBGColor;
				_objects[num2].transform.localPosition = localPosition;
			}
			for (int num3 = 0; num3 < godRayCount; num3++)
			{
				if ((bool)_godRays[num3])
				{
					_godRays[num3].GetComponent<Renderer>().material.color = _curGodRayColor;
				}
			}
			int count2 = tree_groups_normal.Count;
			for (int num4 = 0; num4 < count2; num4++)
			{
				Renderer[] componentsInChildren = tree_groups_normal[num4].GetComponentsInChildren<Renderer>();
				int num5 = componentsInChildren.Length;
				for (int num6 = 0; num6 < num5; num6++)
				{
					componentsInChildren[num6].material.color = _curTreeColor;
				}
			}
			yield return _waitForSeconds;
		}
	}

	public void SetTreeVisible(bool bNormal, bool bThunder)
	{
		int count = tree_groups_normal.Count;
		for (int i = 0; i < count; i++)
		{
			if ((bool)tree_groups_normal[i])
			{
				tree_groups_normal[i].SetActive(bNormal);
			}
		}
		count = tree_groups_thunder.Count;
		for (int j = 0; j < count; j++)
		{
			if ((bool)tree_groups_thunder[j])
			{
				tree_groups_thunder[j].SetActive(bThunder);
			}
		}
	}

	public void PlayTunnelEffect(float _BG_TunnelDelay, float _BG_TunnelFadeTime, float _BG_TunnelDuration)
	{
		StartCoroutine(coBG_TunnelEffect(_BG_TunnelDelay, _BG_TunnelFadeTime, _BG_TunnelDuration));
	}

	private IEnumerator coBG_TunnelEffect(float _BG_TunnelDelay, float _BG_TunnelFadeTime, float _BG_TunnelDuration)
	{
		_curBGColor = _dayBGColor;
		_curTreeColor = _daytTreeColor;
		_curGodRayColor = Color.white;
		yield return new WaitForSeconds(_BG_TunnelDelay);
		TweenTick tweenTick = TweenTick.Begin(base.gameObject, _BG_TunnelFadeTime, delegate(float factor, bool isFinished)
		{
			_curBGColor = Color.Lerp(_dayBGColor, Color.clear, factor);
			_curGodRayColor = Color.Lerp(_daytTreeColor, Color.clear, factor);
		});
		tweenTick.method = UITweener.Method.EaseOut;
		tweenTick.PlayForward();
		SetTreeVisible(bNormal: false, bThunder: false);
		yield return new WaitForSeconds(_BG_TunnelDuration);
		TweenTick tweenTick2 = TweenTick.Begin(base.gameObject, _BG_TunnelFadeTime, delegate(float factor, bool isFinished)
		{
			_curBGColor = Color.Lerp(Color.clear, _nightBGColor, factor);
			_curGodRayColor = Color.Lerp(Color.clear, _nightTreeColor, factor);
		});
		tweenTick2.method = UITweener.Method.EaseOut;
		tweenTick2.PlayForward();
		SetTreeVisible(bNormal: true, bThunder: false);
	}
}
