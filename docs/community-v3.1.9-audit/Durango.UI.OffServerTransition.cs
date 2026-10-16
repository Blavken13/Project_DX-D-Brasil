using System;
using System.Collections.Generic;
using Durango.Logic.Clusters;
using UnityEngine;

namespace Durango.UI;

public class OffServerTransition : MonoBehaviour
{
	private enum Phase
	{
		Idle,
		Closing,
		Covered,
		Opening
	}

	private const float ClawInSeconds = 0.85f;

	private const float ClawPopSeconds = 0.5f;

	private const float ClawOutSeconds = 1f;

	private const int Claws = 4;

	private const float ClawTravelDeg = 118f;

	private const float StrikeSeconds = 0.16f;

	private const float ClawStagger = 0.045f;

	private const float RipGap = 0.05f;

	private const float HeatSeconds = 0.22f;

	private static readonly int[] ClawRank = new int[4] { 2, 0, 1, 3 };

	private const int MaxSamples = 160;

	public const string ClawSound = "sabertooth_attack_claw";

	private static readonly Color ClawDark = new Color(0.10980392f, 9f / 85f, 5f / 51f, 1f);

	private static readonly int[] ClawFromVersion = new int[3] { 3, 1, 7 };

	private const string ClawPrefKey = "OffServerTransition.Claw";

	private static int _clawGate = -1;

	private static bool _clawOnce;

	private static float[] _jag;

	private readonly float[] _ss = new float[160];

	private readonly float[][] _lo = new float[4][]
	{
		new float[160],
		new float[160],
		new float[160],
		new float[160]
	};

	private readonly float[][] _hi = new float[4][]
	{
		new float[160],
		new float[160],
		new float[160],
		new float[160]
	};

	private int _samples;

	private Vector2 _u;

	private Vector2 _n;

	private const float InSeconds = 0.92f;

	private const float PopSeconds = 0.52f;

	private const float HoldSeconds = 0.1f;

	private const float OutSeconds = 1.16f;

	private const float MaxCoveredSeconds = 8f;

	private const int SmoothFramesToOpen = 4;

	private const float SmoothFrameSeconds = 0.045f;

	private const int Stripes = 7;

	private const float AngleDeg = 20f;

	private const float StaggerIn = 0.07f;

	private const float StaggerOut = 0.06f;

	private static readonly Color StripeA = new Color(0.14901961f, 0.14901961f, 12f / 85f, 1f);

	private static readonly Color StripeB = new Color(0.18039216f, 0.1764706f, 14f / 85f, 1f);

	private static OffServerTransition _instance;

	private Texture2D _logo;

	private Material _material;

	private Phase _phase;

	private float _phaseTime;

	private bool _revealRequested;

	private bool _coveredFired;

	private Action _onCovered;

	private int _smoothFrames;

	private int _savedFrameRate = int.MinValue;

	private readonly List<Vector2> _poly = new List<Vector2>(8);

	private readonly List<Vector2> _clip = new List<Vector2>(8);

	private bool _claw;

	private int _closeFrames;

	private int _openFrames;

	private float _closeWorst;

	private float _openWorst;

	private int _phaseFrames;

	private float _w;

	private float _h;

	public static bool ClawEnabled
	{
		get
		{
			if (_clawGate < 0)
			{
				_clawGate = (ResolveClawGate() ? 1 : 0);
			}
			return _clawGate == 1;
		}
	}

	private float InLength
	{
		get
		{
			if (!_claw)
			{
				return 0.92f;
			}
			return 0.85f;
		}
	}

	private float PopLength
	{
		get
		{
			if (!_claw)
			{
				return 0.52f;
			}
			return 0.5f;
		}
	}

	private float OutLength
	{
		get
		{
			if (!_claw)
			{
				return 1.16f;
			}
			return 1f;
		}
	}

	public static bool Busy
	{
		get
		{
			if (_instance != null)
			{
				return _instance._phase != Phase.Idle;
			}
			return false;
		}
	}

	public static void Prepare()
	{
		if (!ClawEnabled)
		{
			return;
		}
		try
		{
			SoundManager.PrepareEvent("sabertooth_attack_claw");
		}
		catch (Exception exception)
		{
			UnityEngine.Debug.LogException(exception);
		}
	}

	private static bool ResolveClawGate()
	{
		string why;
		bool flag = ResolveClawGate(out why);
		UnityEngine.Debug.Log("[OffServerTransition] " + (flag ? "claw" : "D") + " (" + why + ")");
		return flag;
	}

	private static bool ResolveClawGate(out string why)
	{
		try
		{
			string[] commandLineArgs = global::System.Environment.GetCommandLineArgs();
			foreach (string a in commandLineArgs)
			{
				if (string.Equals(a, "-claw", StringComparison.OrdinalIgnoreCase))
				{
					why = "-claw";
					return true;
				}
				if (string.Equals(a, "-noclaw", StringComparison.OrdinalIgnoreCase))
				{
					why = "-noclaw";
					return false;
				}
			}
		}
		catch
		{
		}
		string clientVersion = OffServerLink.ClientVersion;
		why = "client " + clientVersion;
		return VersionAtLeast(clientVersion, ClawFromVersion);
	}

	private static bool VersionAtLeast(string version, int[] min)
	{
		if (string.IsNullOrEmpty(version))
		{
			return false;
		}
		string[] array = version.Trim().Split('.');
		for (int i = 0; i < min.Length; i++)
		{
			int result = 0;
			if (i < array.Length)
			{
				string text = array[i];
				int j;
				for (j = 0; j < text.Length && char.IsDigit(text[j]); j++)
				{
				}
				if (j == 0 || !int.TryParse(text.Substring(0, j), out result))
				{
					return false;
				}
			}
			if (result != min[i])
			{
				return result > min[i];
			}
		}
		return true;
	}

	private static void PlayClawSound()
	{
		try
		{
			SoundManager.PlayEvent("sabertooth_attack_claw");
		}
		catch (Exception exception)
		{
			UnityEngine.Debug.LogException(exception);
		}
	}

	private Vector2 At(float s, float t)
	{
		return new Vector2(_w * 0.5f + _u.x * s + _n.x * t, _h * 0.5f + _u.y * s + _n.y * t);
	}

	private void DrawClaw()
	{
		float f = MathF.PI * 59f / 90f;
		_u = new Vector2(Mathf.Cos(f), Mathf.Sin(f));
		_n = new Vector2(0f - _u.y, _u.x);
		float num = Mathf.Abs(_w * _u.x) + Mathf.Abs(_h * _u.y);
		float num2 = Mathf.Abs(_w * _n.x) + Mathf.Abs(_h * _n.y);
		float m = Mathf.Min(_w, _h);
		float num3 = num2 / 4f;
		float num4 = num * 0.2f;
		float num5 = num * 0.5f + num4 + 12f;
		float num6 = num / 46f;
		float num7 = 0f - num5 - num * 0.4f;
		float num8 = num5 + 4f;
		_samples = 0;
		for (float num9 = num7; num9 < num8; num9 += num6)
		{
			if (_samples >= 159)
			{
				break;
			}
			_ss[_samples++] = num9;
		}
		_ss[_samples++] = num8;
		EnsureJag();
		switch (_phase)
		{
		case Phase.Closing:
		{
			float phaseTime = _phaseTime;
			for (int i = 0; i < 4; i++)
			{
				Gash(i, phaseTime, 0.85f, num3, num4, num5, m, num3 * 0.5f + 2f);
			}
			_material.mainTexture = Texture2D.whiteTexture;
			_material.SetPass(0);
			GL.Begin(4);
			GL.Color(ClawDark);
			for (int j = 0; j < 4; j++)
			{
				Band(_lo[j], _hi[j]);
			}
			GL.End();
			Streaks(phaseTime, num5, num, m, num3);
			return;
		}
		case Phase.Covered:
		{
			_material.mainTexture = Texture2D.whiteTexture;
			_material.SetPass(0);
			GL.Begin(4);
			GL.Color(ClawDark);
			Solid(new Vector2(-2f, -2f));
			Solid(new Vector2(_w + 2f, -2f));
			Solid(new Vector2(_w + 2f, _h + 2f));
			Solid(new Vector2(-2f, -2f));
			Solid(new Vector2(_w + 2f, _h + 2f));
			Solid(new Vector2(-2f, _h + 2f));
			GL.End();
			float num10 = Mathf.Clamp01(_phaseTime / 0.5f);
			float num11 = LogoSize() * Mathf.Max(0.001f, EaseOutBack(num10));
			Rect rect = new Rect(_w * 0.5f - num11 * 0.5f, _h * 0.5f - num11 * 0.5f, num11, num11);
			ClipRect(rect, Vector2.zero, Vector2.zero);
			DrawPolygon(_logo, new Color(1f, 1f, 1f, Mathf.Clamp01(num10 * 3f)), Vector2.zero, rect);
			return;
		}
		}
		float phaseTime2 = _phaseTime;
		for (int k = 0; k < 4; k++)
		{
			Gash(k, phaseTime2, 1f, num3, num4, num5, m, num3 * 0.5f + 1f);
		}
		_material.mainTexture = Texture2D.whiteTexture;
		_material.SetPass(0);
		GL.Begin(4);
		GL.Color(ClawDark);
		for (int l = 0; l <= 4; l++)
		{
			Between(l, num2, textured: false, default(Rect));
		}
		GL.End();
		float num12 = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.75f, phaseTime2 / 1f));
		if (num12 > 0.003f)
		{
			float num13 = LogoSize();
			Rect logo = new Rect(_w * 0.5f - num13 * 0.5f, _h * 0.5f - num13 * 0.5f, num13, num13);
			_material.mainTexture = _logo;
			_material.SetPass(0);
			GL.Begin(4);
			GL.Color(new Color(1f, 1f, 1f, num12));
			for (int n = 0; n <= 4; n++)
			{
				Between(n, num2, textured: true, logo);
			}
			GL.End();
		}
		Streaks(phaseTime2, num5, num, m, num3);
	}

	private static void EnsureJag()
	{
		if (_jag == null)
		{
			_jag = new float[1280];
			global::System.Random random = new global::System.Random(4102026);
			for (int i = 0; i < _jag.Length; i++)
			{
				_jag[i] = (float)random.NextDouble();
			}
		}
	}

	private void ClawState(int i, float t, float length, float half, out float sTip, out float rip, out float heat)
	{
		float num = (float)ClawRank[i] * 0.045f;
		float t2 = Mathf.Clamp01((t - num) / 0.16f);
		sTip = 0f - half + 2f * half * EaseInOutQuad(t2);
		float num2 = num + 0.16f + 0.05f;
		rip = Mathf.Clamp01((t - num2) / Mathf.Max(0.01f, length - num2));
		heat = 1f - Mathf.Clamp01((t - num - 0.16f) / 0.22f);
	}

	private void Gash(int i, float t, float length, float spacing, float tipLen, float half, float m, float widthEnd)
	{
		ClawState(i, t, length, half, out var sTip, out var rip, out var _);
		float num = ((float)i - 1.5f) * spacing;
		float num2 = Mathf.Lerp(Mathf.Max(1.5f, m * 0.004f), widthEnd, EaseInOutCubic(rip));
		float num3 = spacing * 0.08f * Mathf.Sin(MathF.PI * rip) + 1.5f * (1f - rip);
		float[] array = _lo[i];
		float[] array2 = _hi[i];
		int num4 = i * 2 * 160;
		int num5 = (i * 2 + 1) * 160;
		for (int j = 0; j < _samples; j++)
		{
			float num6 = _ss[j];
			float num7 = ((num6 <= sTip) ? (num2 * Mathf.Pow(Mathf.Min(1f, (sTip - num6) / tipLen), 0.6f)) : 0f);
			float num8 = Mathf.Min(1f, num7 / 3f) * num3;
			array[j] = num - num7 - num8 * _jag[num4 + j];
			array2[j] = num + num7 + num8 * _jag[num5 + j];
		}
	}

	private void Band(float[] lo, float[] hi)
	{
		for (int i = 0; i < _samples - 1; i++)
		{
			if (!(hi[i] - lo[i] < 0.01f) || !(hi[i + 1] - lo[i + 1] < 0.01f))
			{
				Vector2 p = At(_ss[i], lo[i]);
				Vector2 p2 = At(_ss[i + 1], lo[i + 1]);
				Vector2 p3 = At(_ss[i + 1], hi[i + 1]);
				Vector2 p4 = At(_ss[i], hi[i]);
				Solid(p);
				Solid(p2);
				Solid(p3);
				Solid(p);
				Solid(p3);
				Solid(p4);
			}
		}
	}

	private void Between(int j, float en, bool textured, Rect logo)
	{
		for (int i = 0; i < _samples - 1; i++)
		{
			float num = ((j == 0) ? (0f - en) : _hi[j - 1][i]);
			float num2 = ((j == 0) ? (0f - en) : _hi[j - 1][i + 1]);
			float a = ((j == 4) ? en : _lo[j][i]);
			float a2 = ((j == 4) ? en : _lo[j][i + 1]);
			a = Mathf.Max(a, num);
			a2 = Mathf.Max(a2, num2);
			if (a - num < 0.01f && a2 - num2 < 0.01f)
			{
				continue;
			}
			Vector2 vector = At(_ss[i], num);
			Vector2 vector2 = At(_ss[i + 1], num2);
			Vector2 vector3 = At(_ss[i + 1], a2);
			Vector2 vector4 = At(_ss[i], a);
			if (!textured)
			{
				Solid(vector);
				Solid(vector2);
				Solid(vector3);
				Solid(vector);
				Solid(vector3);
				Solid(vector4);
				continue;
			}
			float num3 = Mathf.Min(Mathf.Min(vector.x, vector2.x), Mathf.Min(vector3.x, vector4.x));
			float num4 = Mathf.Max(Mathf.Max(vector.x, vector2.x), Mathf.Max(vector3.x, vector4.x));
			float num5 = Mathf.Min(Mathf.Min(vector.y, vector2.y), Mathf.Min(vector3.y, vector4.y));
			float num6 = Mathf.Max(Mathf.Max(vector.y, vector2.y), Mathf.Max(vector3.y, vector4.y));
			if (!(num4 <= logo.xMin) && !(num3 >= logo.xMax) && !(num6 <= logo.yMin) && !(num5 >= logo.yMax))
			{
				_poly.Clear();
				_poly.Add(vector);
				_poly.Add(vector2);
				_poly.Add(vector3);
				_poly.Add(vector4);
				ClipPolyToRect(logo);
				for (int k = 1; k < _poly.Count - 1; k++)
				{
					Textured(_poly[0], Vector2.zero, logo);
					Textured(_poly[k], Vector2.zero, logo);
					Textured(_poly[k + 1], Vector2.zero, logo);
				}
			}
		}
	}

	private void ClipPolyToRect(Rect r)
	{
		for (int i = 0; i < 4; i++)
		{
			if (_poly.Count < 3)
			{
				break;
			}
			_clip.Clear();
			_clip.AddRange(_poly);
			_poly.Clear();
			for (int j = 0; j < _clip.Count; j++)
			{
				Vector2 vector = _clip[j];
				Vector2 vector2 = _clip[(j + 1) % _clip.Count];
				float num = Inside(vector, r, i);
				float num2 = Inside(vector2, r, i);
				if (num >= 0f)
				{
					_poly.Add(vector);
				}
				if (num >= 0f != num2 >= 0f)
				{
					_poly.Add(vector + (vector2 - vector) * (num / (num - num2)));
				}
			}
		}
	}

	private static float Inside(Vector2 p, Rect r, int side)
	{
		return side switch
		{
			0 => p.x - r.xMin, 
			1 => r.xMax - p.x, 
			2 => p.y - r.yMin, 
			_ => r.yMax - p.y, 
		};
	}

	private void Streaks(float t, float half, float eu, float m, float spacing)
	{
		float length = ((_phase == Phase.Closing) ? 0.85f : 1f);
		float num = eu * 0.55f;
		float halfW = m * 0.006f;
		_material.mainTexture = Texture2D.whiteTexture;
		_material.SetPass(0);
		GL.Begin(4);
		for (int i = 0; i < 4; i++)
		{
			ClawState(i, t, length, half, out var sTip, out var _, out var heat);
			if (heat <= 0.01f || sTip <= 0f - half)
			{
				continue;
			}
			float num2 = ((float)i - 1.5f) * spacing;
			float num3 = Mathf.Max(0f - half, sTip - num);
			for (int j = 0; j < 2; j++)
			{
				for (int k = 0; k < 24; k++)
				{
					float num4 = Mathf.Lerp(num3, sTip, (float)k / 24f);
					float num5 = Mathf.Lerp(num3, sTip, (float)(k + 1) / 24f);
					float num6 = StreakWidth(j, num4, sTip, num, halfW);
					float num7 = StreakWidth(j, num5, sTip, num, halfW);
					Color c = StreakColor(j, (num4 - num3) / Mathf.Max(1f, sTip - num3), heat);
					Color c2 = StreakColor(j, (num5 - num3) / Mathf.Max(1f, sTip - num3), heat);
					float k2 = ((j == 0) ? 0f : 0.35f);
					for (int l = -1; l <= 1; l += 2)
					{
						Vector2 p = At(num4, num2);
						Vector2 p2 = At(num5, num2);
						Vector2 p3 = At(num5, num2 + (float)l * num7);
						Vector2 p4 = At(num4, num2 + (float)l * num6);
						Shaded(p, c);
						Shaded(p2, c2);
						Shaded(p3, Fade(c2, k2));
						Shaded(p, c);
						Shaded(p3, Fade(c2, k2));
						Shaded(p4, Fade(c, k2));
					}
				}
			}
		}
		GL.End();
	}

	private static float StreakWidth(int layer, float s, float sTip, float glowLen, float halfW)
	{
		float num = Mathf.Min(1f, (sTip - s) / (glowLen * 0.12f));
		if (layer != 0)
		{
			return halfW * 0.9f * Mathf.Sqrt(num) + 0.6f * num;
		}
		return halfW * 3.2f * Mathf.Sqrt(num) + 2f * num;
	}

	private static Color StreakColor(int layer, float u, float heat)
	{
		if (layer == 1)
		{
			return Color.Lerp(new Color(1f, 2f / 3f, 0.3529412f, 0f), new Color(1f, 50f / 51f, 0.9254902f, heat), u);
		}
		if (u < 0.7f)
		{
			return Color.Lerp(new Color(1f, 0.3529412f, 0.11764706f, 0.1f * heat), new Color(1f, 0.5019608f, 16f / 85f, 0.55f * heat), u / 0.7f);
		}
		return Color.Lerp(new Color(1f, 0.5019608f, 16f / 85f, 0.55f * heat), new Color(1f, 0.9254902f, 40f / 51f, 0.95f * heat), (u - 0.7f) / 0.3f);
	}

	private static Color Fade(Color c, float k)
	{
		c.a *= k;
		return c;
	}

	private void Shaded(Vector2 p, Color c)
	{
		GL.Color(c);
		GL.TexCoord2(0.5f, 0.5f);
		GL.Vertex3(p.x, _h - p.y, 0f);
	}

	private static float EaseInOutQuad(float t)
	{
		if (!(t < 0.5f))
		{
			return 1f - (-2f * t + 2f) * (-2f * t + 2f) * 0.5f;
		}
		return 2f * t * t;
	}

	private static float EaseInOutCubic(float t)
	{
		if (t < 0.5f)
		{
			return 4f * t * t * t;
		}
		float num = -2f * t + 2f;
		return 1f - num * num * num * 0.5f;
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (!(_instance != null))
		{
			GameObject obj = new GameObject("OffServerTransition");
			UnityEngine.Object.DontDestroyOnLoad(obj);
			_instance = obj.AddComponent<OffServerTransition>();
		}
	}

	public static void Cover(Action then)
	{
		if (_instance == null || !_instance.EnsureAssets())
		{
			then?.Invoke();
		}
		else
		{
			_instance.CoverInternal(then);
		}
	}

	public static void Reveal()
	{
		Cut();
	}

	public static void Cut()
	{
		if (!(_instance == null) && _instance.EnsureAssets())
		{
			OffServerTransition instance = _instance;
			if (instance._phase == Phase.Idle || instance._phase == Phase.Opening)
			{
				instance.CoverInternal(null);
			}
			instance._revealRequested = true;
		}
	}

	private void CoverInternal(Action then)
	{
		if (_phase == Phase.Covered && _coveredFired)
		{
			then?.Invoke();
			return;
		}
		_onCovered = (Action)Delegate.Combine(_onCovered, then);
		if (_phase != Phase.Covered)
		{
			_revealRequested = false;
			if (_phase != Phase.Closing)
			{
				SetPhase(Phase.Closing);
			}
		}
	}

	private bool EnsureAssets()
	{
		if (_logo == null)
		{
			_logo = Resources.Load<Texture2D>("offserver_transition_logo") ?? Resources.Load<Texture2D>("offserver_transition_icon");
		}
		if (_material == null)
		{
			Shader shader = Shader.Find("Sprites/Default");
			if (shader != null)
			{
				_material = new Material(shader);
				_material.hideFlags = HideFlags.HideAndDontSave;
			}
		}
		if (_logo != null)
		{
			return _material != null;
		}
		return false;
	}

	private void SetPhase(Phase phase)
	{
		if (_phase == Phase.Idle && phase != Phase.Idle && _savedFrameRate == int.MinValue)
		{
			_savedFrameRate = Application.targetFrameRate;
			Application.targetFrameRate = 60;
		}
		else if (phase == Phase.Idle && _savedFrameRate != int.MinValue)
		{
			Application.targetFrameRate = _savedFrameRate;
			_savedFrameRate = int.MinValue;
		}
		if (phase == Phase.Covered)
		{
			_coveredFired = false;
		}
		if (phase == Phase.Closing && _phase != Phase.Closing)
		{
			_claw = _clawOnce || ClawEnabled;
			_clawOnce = false;
			_closeFrames = (_openFrames = 0);
			_closeWorst = (_openWorst = 0f);
		}
		if (phase == Phase.Idle && _phase == Phase.Opening)
		{
			UnityEngine.Debug.Log(string.Format("[OffServerTransition] {0} sting · close {1} frames worst {2:0} ms · open {3} frames worst {4:0} ms · cap {5}", _claw ? "claw" : "D", _closeFrames, _closeWorst * 1000f, _openFrames, _openWorst * 1000f, Application.targetFrameRate));
		}
		_phase = phase;
		_phaseTime = 0f;
		_phaseFrames = 0;
		if (_claw && (phase == Phase.Closing || phase == Phase.Opening))
		{
			PlayClawSound();
		}
	}

	private void Update()
	{
		if (_phase == Phase.Idle)
		{
			return;
		}
		_phaseTime += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
		_smoothFrames = ((Time.unscaledDeltaTime < 0.045f) ? (_smoothFrames + 1) : 0);
		_phaseFrames++;
		if (_phase == Phase.Closing && _phaseFrames > 1)
		{
			_closeFrames++;
			_closeWorst = Mathf.Max(_closeWorst, Time.unscaledDeltaTime);
		}
		else if (_phase == Phase.Opening && _phaseFrames > 1)
		{
			_openFrames++;
			_openWorst = Mathf.Max(_openWorst, Time.unscaledDeltaTime);
		}
		switch (_phase)
		{
		case Phase.Closing:
			if (_phaseTime >= InLength)
			{
				SetPhase(Phase.Covered);
			}
			break;
		case Phase.Covered:
			if (!_coveredFired && _phaseTime >= PopLength)
			{
				_coveredFired = true;
				Action onCovered = _onCovered;
				_onCovered = null;
				try
				{
					onCovered?.Invoke();
					break;
				}
				catch (Exception exception)
				{
					UnityEngine.Debug.LogException(exception);
					break;
				}
			}
			if (_coveredFired && ((_revealRequested && _phaseTime >= PopLength + 0.1f && _smoothFrames >= 4) || _phaseTime >= 8f))
			{
				_revealRequested = false;
				SetPhase(Phase.Opening);
			}
			break;
		case Phase.Opening:
			if (_phaseTime >= OutLength)
			{
				SetPhase(Phase.Idle);
			}
			break;
		}
	}

	private void OnGUI()
	{
		if (_phase == Phase.Idle || Event.current.type != EventType.Repaint || !EnsureAssets())
		{
			return;
		}
		_w = Screen.width;
		_h = Screen.height;
		if (_claw)
		{
			GL.PushMatrix();
			GL.LoadPixelMatrix();
			DrawClaw();
			GL.PopMatrix();
			return;
		}
		float f = 0.34906584f;
		float num = _h * Mathf.Tan(f);
		float stripeW = (_w + num) / 7f;
		float far = _w + num + _w / 7f;
		GL.PushMatrix();
		GL.LoadPixelMatrix();
		switch (_phase)
		{
		case Phase.Closing:
		{
			float p2 = Mathf.Clamp01(_phaseTime / 0.92f);
			float dur2 = 0.58f;
			DrawStripes(num, stripeW, (int i) => (0f - far) * (1f - EaseOutCubic(Mathf.Clamp01((p2 - (float)i * 0.07f) / dur2))));
			break;
		}
		case Phase.Covered:
		{
			DrawStripes(num, stripeW, (int i) => 0f);
			float num6 = Mathf.Clamp01(_phaseTime / 0.52f);
			float num7 = Mathf.Max(0.001f, EaseOutBack(num6));
			float num8 = LogoSize() * num7;
			Rect rect2 = new Rect(_w * 0.5f - num8 * 0.5f, _h * 0.5f - num8 * 0.5f, num8, num8);
			ClipRect(rect2, new Vector2(_w * 0.5f, _h * 0.5f), Vector2.zero);
			DrawPolygon(_logo, new Color(1f, 1f, 1f, Mathf.Clamp01(num6 * 3f)), Vector2.zero, rect2);
			break;
		}
		default:
		{
			float p = Mathf.Clamp01(_phaseTime / 1.16f);
			float dur = 0.64f;
			DrawStripes(num, stripeW, (int i) => far * EaseInCubic(Mathf.Clamp01((p - (float)(6 - i) * 0.06f) / dur)));
			Vector2 vector = new Vector2(Mathf.Cos(f), Mathf.Sin(f));
			float num2 = (Mathf.Abs(_w * vector.x) + Mathf.Abs(_h * vector.y)) * 0.5f + 8f;
			float num3 = EaseInCubic(Mathf.Clamp01(p / 0.8f));
			float num4 = LogoSize();
			Rect rect = new Rect(_w * 0.5f - num4 * 0.5f, _h * 0.5f - num4 * 0.5f, num4, num4);
			Vector2 vector2 = new Vector2(_w * 0.5f, _h * 0.5f);
			for (int num5 = -1; num5 <= 1; num5 += 2)
			{
				ClipRect(rect, vector2 - vector * ((float)num5 * 0.75f), vector * num5);
				DrawPolygon(_logo, Color.white, vector * ((float)num5 * num2 * num3), rect);
			}
			break;
		}
		}
		GL.PopMatrix();
	}

	private float LogoSize()
	{
		return Mathf.Min(_w, _h) * 0.62f;
	}

	private void DrawStripes(float skew, float stripeW, Func<int, float> shift)
	{
		_material.mainTexture = Texture2D.whiteTexture;
		_material.SetPass(0);
		GL.Begin(4);
		for (int i = 0; i < 7; i++)
		{
			float num = 0f - skew + (float)i * stripeW + shift(i);
			float num2 = num + stripeW + 1f;
			GL.Color((i % 2 == 0) ? StripeA : StripeB);
			Vector2 p = new Vector2(num + skew, 0f);
			Vector2 p2 = new Vector2(num2 + skew, 0f);
			Vector2 p3 = new Vector2(num2, _h);
			Vector2 p4 = new Vector2(num, _h);
			Solid(p);
			Solid(p2);
			Solid(p3);
			Solid(p);
			Solid(p3);
			Solid(p4);
		}
		GL.End();
	}

	private void Solid(Vector2 p)
	{
		GL.TexCoord2(0.5f, 0.5f);
		GL.Vertex3(p.x, _h - p.y, 0f);
	}

	private void ClipRect(Rect r, Vector2 origin, Vector2 keep)
	{
		_clip.Clear();
		_clip.Add(new Vector2(r.xMin, r.yMin));
		_clip.Add(new Vector2(r.xMax, r.yMin));
		_clip.Add(new Vector2(r.xMax, r.yMax));
		_clip.Add(new Vector2(r.xMin, r.yMax));
		_poly.Clear();
		if (keep == Vector2.zero)
		{
			_poly.AddRange(_clip);
			return;
		}
		for (int i = 0; i < _clip.Count; i++)
		{
			Vector2 vector = _clip[i];
			Vector2 vector2 = _clip[(i + 1) % _clip.Count];
			float num = Vector2.Dot(vector - origin, keep);
			float num2 = Vector2.Dot(vector2 - origin, keep);
			if (num >= 0f)
			{
				_poly.Add(vector);
			}
			if (num >= 0f != num2 >= 0f)
			{
				_poly.Add(vector + (vector2 - vector) * (num / (num - num2)));
			}
		}
	}

	private void DrawPolygon(Texture texture, Color color, Vector2 shift, Rect uvRect)
	{
		if (_poly.Count >= 3)
		{
			_material.mainTexture = texture;
			_material.SetPass(0);
			GL.Begin(4);
			GL.Color(color);
			for (int i = 1; i < _poly.Count - 1; i++)
			{
				Textured(_poly[0], shift, uvRect);
				Textured(_poly[i], shift, uvRect);
				Textured(_poly[i + 1], shift, uvRect);
			}
			GL.End();
		}
	}

	private void Textured(Vector2 p, Vector2 shift, Rect uvRect)
	{
		GL.TexCoord2((p.x - uvRect.xMin) / uvRect.width, 1f - (p.y - uvRect.yMin) / uvRect.height);
		GL.Vertex3(p.x + shift.x, _h - (p.y + shift.y), 0f);
	}

	private static float EaseOutCubic(float t)
	{
		t = 1f - t;
		return 1f - t * t * t;
	}

	private static float EaseInCubic(float t)
	{
		return t * t * t;
	}

	private static float EaseOutBack(float t)
	{
		float num = t - 1f;
		return 1f + 2.70158f * num * num * num + 1.70158f * num * num;
	}
}
