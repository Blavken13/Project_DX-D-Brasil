using System;
using Durango.UI.Control;
using L10N;
using UnityEngine;

namespace Durango.UI;

public class TitleMessageBoxBase : MonoBehaviour
{
	[SerializeField]
	protected SelectableButton _okButton;

	[SerializeField]
	private UILabel _title;

	[SerializeField]
	private UILabel _message;

	[SerializeField]
	private SelectableButton _cancelButton;

	private Action _onOk;

	private Action _onCancel;

	// AUTH_UX_V2_MESSAGE_INPUT
	private UIInput _input;

	private UILabel _inputLabel;

	private UILabel _inputPlaceholderLabel;

	private UITexture _inputBorder;

	private UITexture _inputBackground;

	private bool _inputMode;

	private Action<string> _onInputSubmit;

	private Vector3 _messageBaseLocalPosition;

	private bool _inputVisualsCreated;

	protected virtual void Awake()
	{
		if (_okButton != null)
		{
			_okButton.Clicked = OnOk;
		}
		if (_cancelButton != null)
		{
			_cancelButton.Clicked = OnCancel;
		}
	}

	private void OnOk()
	{
		if (_inputMode)
		{
			SubmitInput();
			return;
		}
		if (_onOk != null)
		{
			_onOk();
		}
		else
		{
			Close();
		}
	}

	private void OnCancel()
	{
		if (_onCancel != null)
		{
			_onCancel();
		}
		else
		{
			Close();
		}
	}

	private void OnEnable()
	{
		GameSystem<InputSystem>.Instance().On(InputCommand.Back, OnReceivedBackInputCommand);
	}

	private void OnDisable()
	{
		GameSystem<InputSystem>.Instance().Off(InputCommand.Back, OnReceivedBackInputCommand);
		if (_input != null)
		{
			_input.isSelected = false;
		}
	}

	private void OnReceivedBackInputCommand(InputCommandMessage msg)
	{
		OnCancel();
	}

	public virtual void Show(string title, string message, Action onClick, Action onCancel = null, string okButtonLabel = null, string cancelButtonLabel = null)
	{
		DisableInputMode();
		_okButton.Text = (string.IsNullOrEmpty(okButtonLabel) ? ManualTranslator.Confirm : okButtonLabel);
		_cancelButton.gameObject.SetActive(onCancel != null);
		_cancelButton.Text = (string.IsNullOrEmpty(cancelButtonLabel) ? ManualTranslator.Cancel : cancelButtonLabel);
		_onOk = onClick;
		_onCancel = onCancel;
		_title.text = title;
		if (message.Length > 2500)
		{
			message = message.Substring(0, 2500);
		}
		_message.text = message;
		base.gameObject.SetActive(value: true);
	}

	public virtual void ShowInput(
		string title,
		string instruction,
		string placeholder,
		bool isPassword,
		int limit,
		Action<string> onSubmit,
		Action onCancel = null,
		string okButtonLabel = null,
		string cancelButtonLabel = null)
	{
		Show(
			title,
			instruction ?? string.Empty,
			SubmitInput,
			onCancel,
			okButtonLabel,
			cancelButtonLabel);

		EnsureInputVisuals();

		_inputMode = true;
		_onInputSubmit = onSubmit;

		_input.enabled = true;
		_input.inputType = isPassword ? UIInput.InputType.Password : UIInput.InputType.Standard;
		_input.onReturnKey = UIInput.OnReturnKey.Submit;
		_input.validation = UIInput.Validation.None;
		_input.characterLimit = limit;
		_input.hideInput = false;
		_input.selectAllTextOnFocus = false;
		_input.label.multiLine = false;
		_input.label.overflowMethod = UILabel.Overflow.ClampContent;
		_input.defaultText = string.Empty;
		_input.value = string.Empty;

		_inputPlaceholderLabel.text = placeholder ?? string.Empty;
		ApplyInputLayout();
		UpdateInputVisualState();

		_input.isSelected = true;

		UnityEngine.Debug.Log(
			"[Auth] campo visual aberto. password=" + isPassword
			+ " limit=" + limit
			+ " placeholder=" + (placeholder ?? string.Empty));
	}

	private void EnsureInputVisuals()
	{
		if (_inputVisualsCreated)
		{
			return;
		}

		_messageBaseLocalPosition = _message.transform.localPosition;
		Transform parent = _message.transform.parent;

		GameObject inputObject = (GameObject)UnityEngine.Object.Instantiate(_message.gameObject);
		inputObject.name = "Auth Input Text";
		inputObject.transform.parent = parent;
		inputObject.transform.localRotation = _message.transform.localRotation;
		inputObject.transform.localScale = _message.transform.localScale;

		_inputLabel = inputObject.GetComponent<UILabel>();
		_inputLabel.text = string.Empty;
		_inputLabel.depth = _message.depth + 3;
		_inputLabel.alignment = NGUIText.Alignment.Left;
		_inputLabel.color = Color.white;

		_input = inputObject.GetComponent<UIInput>();
		if (_input == null)
		{
			_input = inputObject.AddComponent<UIInput>();
		}
		_input.label = _inputLabel;

		EventDelegate.Add(_input.onSubmit, SubmitInput);
		EventDelegate.Add(_input.onChange, UpdateInputVisualState);

		GameObject placeholderObject = (GameObject)UnityEngine.Object.Instantiate(_message.gameObject);
		placeholderObject.name = "Auth Input Placeholder";
		placeholderObject.transform.parent = parent;
		placeholderObject.transform.localRotation = _message.transform.localRotation;
		placeholderObject.transform.localScale = _message.transform.localScale;

		_inputPlaceholderLabel = placeholderObject.GetComponent<UILabel>();
		_inputPlaceholderLabel.depth = _message.depth + 4;
		_inputPlaceholderLabel.alignment = NGUIText.Alignment.Left;
		_inputPlaceholderLabel.color = new Color(1f, 1f, 1f, 0.52f);

		GameObject borderObject = new GameObject("Auth Input Border");
		borderObject.layer = _message.gameObject.layer;
		borderObject.transform.parent = parent;
		borderObject.transform.localRotation = Quaternion.identity;
		borderObject.transform.localScale = Vector3.one;
		_inputBorder = borderObject.AddComponent<UITexture>();
		_inputBorder.mainTexture = Texture2D.whiteTexture;
		_inputBorder.depth = _message.depth + 1;
		_inputBorder.color = new Color(1f, 0.76f, 0.18f, 0.98f);

		GameObject backgroundObject = new GameObject("Auth Input Background");
		backgroundObject.layer = _message.gameObject.layer;
		backgroundObject.transform.parent = parent;
		backgroundObject.transform.localRotation = Quaternion.identity;
		backgroundObject.transform.localScale = Vector3.one;
		_inputBackground = backgroundObject.AddComponent<UITexture>();
		_inputBackground.mainTexture = Texture2D.whiteTexture;
		_inputBackground.depth = _message.depth + 2;
		_inputBackground.color = new Color(0.025f, 0.035f, 0.035f, 0.96f);

		BoxCollider collider = backgroundObject.AddComponent<BoxCollider>();
		collider.isTrigger = true;

		UIEventListener.Get(backgroundObject).onClick = delegate(GameObject go)
		{
			if (_inputMode && _input != null)
			{
				_input.isSelected = true;
			}
		};

		_inputVisualsCreated = true;
		HideInputVisuals();
	}

	private void ApplyInputLayout()
	{
		if (!_inputVisualsCreated)
		{
			return;
		}

		int fieldWidth = Mathf.Clamp(_message.width, 420, 680);
		int fieldHeight = Mathf.Max(56, _inputLabel.fontSize + 24);

		_message.transform.localPosition =
			_messageBaseLocalPosition + Vector3.up * 34f;

		Vector3 fieldPosition =
			_messageBaseLocalPosition + Vector3.down * 38f;

		_inputLabel.transform.localPosition = fieldPosition;
		_inputPlaceholderLabel.transform.localPosition = fieldPosition;
		_inputBorder.transform.localPosition = fieldPosition;
		_inputBackground.transform.localPosition = fieldPosition;

		_inputLabel.width = fieldWidth - 36;
		_inputLabel.height = Mathf.Max(_inputLabel.fontSize + 10, 32);
		_inputPlaceholderLabel.width = fieldWidth - 36;
		_inputPlaceholderLabel.height = _inputLabel.height;

		_inputBorder.SetDimensions(fieldWidth + 4, fieldHeight + 4);
		_inputBackground.SetDimensions(fieldWidth, fieldHeight);

		BoxCollider collider = _inputBackground.GetComponent<BoxCollider>();
		if (collider != null)
		{
			collider.center = Vector3.zero;
			collider.size = new Vector3(fieldWidth, fieldHeight, 1f);
		}

		_inputLabel.gameObject.SetActive(value: true);
		_inputPlaceholderLabel.gameObject.SetActive(value: true);
		_inputBorder.gameObject.SetActive(value: true);
		_inputBackground.gameObject.SetActive(value: true);
	}

	private void UpdateInputVisualState()
	{
		if (!_inputMode || _input == null)
		{
			return;
		}

		bool empty = string.IsNullOrEmpty(_input.value);

		if (_inputPlaceholderLabel != null)
		{
			_inputPlaceholderLabel.gameObject.SetActive(empty);
		}

		if (_okButton != null)
		{
			_okButton.Disabled = empty;
		}
	}

	private void HideInputVisuals()
	{
		if (_inputLabel != null)
		{
			_inputLabel.gameObject.SetActive(value: false);
		}
		if (_inputPlaceholderLabel != null)
		{
			_inputPlaceholderLabel.gameObject.SetActive(value: false);
		}
		if (_inputBorder != null)
		{
			_inputBorder.gameObject.SetActive(value: false);
		}
		if (_inputBackground != null)
		{
			_inputBackground.gameObject.SetActive(value: false);
		}
	}

	private void SubmitInput()
	{
		if (!_inputMode || _input == null)
		{
			return;
		}

		if (string.IsNullOrEmpty(_input.value))
		{
			UpdateInputVisualState();
			_input.isSelected = true;
			return;
		}

		string value = _input.value;
		Action<string> callback = _onInputSubmit;

		DisableInputMode();

		if (callback != null)
		{
			callback(value);
		}
	}

	private void DisableInputMode()
	{
		if (_input != null)
		{
			_input.isSelected = false;
			_input.enabled = false;
			_input.value = string.Empty;
		}

		_inputMode = false;
		_onInputSubmit = null;

		if (_inputVisualsCreated)
		{
			HideInputVisuals();
			_message.transform.localPosition = _messageBaseLocalPosition;
		}

		if (_okButton != null)
		{
			_okButton.Disabled = false;
		}
	}

	public virtual void Close()
	{
		DisableInputMode();
		base.gameObject.SetActive(value: false);
	}
}
