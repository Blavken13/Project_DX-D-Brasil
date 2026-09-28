#!/usr/bin/env python3
'''
Durango Brasil — Login UI/UX patch v2 (2026-09-28)

Esta versao foi feita para o estado LOCAL do cliente em que:
  - TitleMenuGroup usa UserControl.ShowInputBox(...)
  - TitleMenuUserControlBase encaminha para _messageBox.ShowInput(...)
  - TitleMessageBoxBase cria um UIInput reutilizando o UILabel _message

O patch preserva essa abordagem (um unico modal) e melhora:
  * campo de entrada separado do texto de instrucao;
  * fundo escuro + borda amarela;
  * placeholder visivel mesmo com foco;
  * area inteira do campo clicavel;
  * botao principal desabilitado enquanto o campo esta vazio;
  * etapas claras de Login (1/2, 2/2);
  * etapas claras de Cadastro (1/3, 2/3, 3/3);
  * terminologia "nickname" na UI;
  * validacao local de nickname, senha e confirmacao;
  * botoes contextuais: Proximo / Entrar / Criar conta;
  * build e deploy automatico do Assembly-CSharp.dll.

Uso, na raiz de Project_DX-D-Brasil:
    python apply_client_login_ux_patch_v2.py

Opcional:
    --repo CAMINHO
    --no-build
    --no-backup

O script NAO exige arvore Git limpa, porque esta versao foi criada
explicitamente para preservar as alteracoes locais do fluxo de login.
Ainda assim, ele e fail-fast: so edita os blocos locais esperados.
'''

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
from pathlib import Path

CLIENT_REL = Path("Durango-OffServer")
SRC_REL = CLIENT_REL / "_client_src"

TITLE_REL = SRC_REL / "Durango.UI" / "TitleMenuGroup.cs"
CONTROL_REL = SRC_REL / "Durango.UI" / "TitleMenuUserControlBase.cs"
MESSAGE_REL = SRC_REL / "Durango.UI" / "TitleMessageBoxBase.cs"
DLL_REL = CLIENT_REL / "DurangoV2_Data" / "Managed" / "Assembly-CSharp.dll"

TITLE_MARKER = "AUTH_UX_V2_TITLE_FLOW"
CONTROL_MARKER = "AUTH_UX_V2_INPUT_API"
MESSAGE_MARKER = "AUTH_UX_V2_MESSAGE_INPUT"


def die(msg: str) -> None:
    raise SystemExit("ERRO: " + msg)


def read_text(path: Path) -> tuple[str, str]:
    raw = path.read_bytes()
    newline = "\r\n" if b"\r\n" in raw else "\n"
    try:
        text = raw.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        die(f"{path}: nao esta em UTF-8 ({exc})")
    return text.replace("\r\n", "\n"), newline


def write_text(path: Path, text: str, newline: str) -> None:
    path.write_bytes(text.replace("\n", newline).encode("utf-8"))


def backup_once(path: Path) -> None:
    dst = path.with_name(path.name + ".pre-login-ux-v2.bak")
    if not dst.exists():
        shutil.copy2(path, dst)
        print("backup:", dst)


def replace_one(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        die(f"{label}: trecho esperado apareceu {count} vezes")
    return text.replace(old, new, 1)


def replace_regex_one(
    text: str,
    pattern: str,
    replacement: str,
    label: str,
    *,
    flags: int = re.DOTALL,
) -> str:
    rx = re.compile(pattern, flags)
    matches = list(rx.finditer(text))
    if len(matches) != 1:
        die(f"{label}: padrao esperado apareceu {len(matches)} vezes")
    return text[:matches[0].start()] + replacement + text[matches[0].end():]


def patch_title_menu(path: Path) -> None:
    text, nl = read_text(path)

    if TITLE_MARKER not in text:
        pattern = (
            r"\tprivate void BeginLogin\(\)\n"
            r"\t\{.*?"
            r"\n\tprivate void RequestRegister\("
        )

        replacement = r'''	// AUTH_UX_V2_TITLE_FLOW
	private void BeginLogin()
	{
		ShowLoginNicknameStep();
	}

	private void ShowLoginNicknameStep()
	{
		ShowAuthInput(
			"Entrar • etapa 1 de 2\nInforme o nickname usado na sua conta.",
			"Nickname",
			isPassword: false,
			limit: 32,
			buttonText: "Próximo",
			delegate(string username)
			{
				username = (username ?? string.Empty).Trim();

				if (username.Length < 3)
				{
					ShowAuthValidationError(
						"O nickname precisa ter pelo menos 3 caracteres.",
						ShowLoginNicknameStep);
					return;
				}

				if (!Regex.IsMatch(username, "^[A-Za-z0-9._-]+$"))
				{
					ShowAuthValidationError(
						"Use somente letras, números, ponto, hífen ou underline no nickname.",
						ShowLoginNicknameStep);
					return;
				}

				ShowLoginPasswordStep(username);
			});
	}

	private void ShowLoginPasswordStep(string username)
	{
		ShowAuthInput(
			"Entrar • etapa 2 de 2\nDigite a senha da conta \"" + username + "\".",
			"Senha",
			isPassword: true,
			limit: 128,
			buttonText: "Entrar",
			delegate(string password)
			{
				password = password ?? string.Empty;

				if (password.Length == 0)
				{
					ShowAuthValidationError(
						"Digite a senha da sua conta.",
						delegate
						{
							ShowLoginPasswordStep(username);
						});
					return;
				}

				RequestLogin(username, password);
			});
	}

	private void BeginRegister()
	{
		ShowRegisterNicknameStep();
	}

	private void ShowRegisterNicknameStep()
	{
		ShowAuthInput(
			"Cadastro • etapa 1 de 3\nEscolha o nickname que você usará para entrar.",
			"Nickname",
			isPassword: false,
			limit: 32,
			buttonText: "Próximo",
			delegate(string username)
			{
				username = (username ?? string.Empty).Trim();

				if (username.Length < 3)
				{
					ShowAuthValidationError(
						"O nickname precisa ter pelo menos 3 caracteres.",
						ShowRegisterNicknameStep);
					return;
				}

				if (!Regex.IsMatch(username, "^[A-Za-z0-9._-]+$"))
				{
					ShowAuthValidationError(
						"Use somente letras, números, ponto, hífen ou underline no nickname.",
						ShowRegisterNicknameStep);
					return;
				}

				ShowRegisterPasswordStep(username);
			});
	}

	private void ShowRegisterPasswordStep(string username)
	{
		ShowAuthInput(
			"Cadastro • etapa 2 de 3\nCrie uma senha com pelo menos 8 caracteres.",
			"Senha (mínimo 8 caracteres)",
			isPassword: true,
			limit: 128,
			buttonText: "Próximo",
			delegate(string password)
			{
				password = password ?? string.Empty;

				if (password.Length < 8)
				{
					ShowAuthValidationError(
						"A senha precisa ter pelo menos 8 caracteres.",
						delegate
						{
							ShowRegisterPasswordStep(username);
						});
					return;
				}

				ShowRegisterConfirmationStep(username, password);
			});
	}

	private void ShowRegisterConfirmationStep(string username, string password)
	{
		ShowAuthInput(
			"Cadastro • etapa 3 de 3\nDigite novamente a mesma senha para confirmar.",
			"Confirmar senha",
			isPassword: true,
			limit: 128,
			buttonText: "Criar conta",
			delegate(string confirmation)
			{
				confirmation = confirmation ?? string.Empty;

				if (confirmation.Length == 0)
				{
					ShowAuthValidationError(
						"Confirme a senha antes de criar a conta.",
						delegate
						{
							ShowRegisterConfirmationStep(username, password);
						});
					return;
				}

				if (password != confirmation)
				{
					ShowAuthValidationError(
						"As senhas não coincidem. Digite novamente a confirmação.",
						delegate
						{
							ShowRegisterConfirmationStep(username, password);
						});
					return;
				}

				RequestRegister(username, password, confirmation);
			});
	}

	private void ShowAuthInput(
		string instruction,
		string placeholder,
		bool isPassword,
		int limit,
		string buttonText,
		Action<string> submitted)
	{
		UserControl.ShowInputBox(
			"Conta Durango Brasil",
			instruction,
			placeholder,
			isPassword,
			limit,
			submitted,
			ShowAuthChoice,
			buttonText);
	}

	private void ShowAuthValidationError(string message, Action retry)
	{
		UserControl.ShowMessageBox(
			"Revise os dados",
			message,
			delegate
			{
				if (_authInProgress && retry != null)
				{
					retry();
				}
			},
			null,
			"Corrigir");
	}

	private void RequestRegister('''

        text = replace_regex_one(
            text,
            pattern,
            replacement,
            "TitleMenuGroup auth local",
        )

    old_choice = '''		UserControl.ShowMessageBox(
			"Conta Durango Brasil",
			"Entre com sua conta ou crie uma nova para continuar.",
			BeginLogin,
			BeginRegister,
			"Entrar",
			"Criar conta");'''

    new_choice = '''		UserControl.ShowMessageBox(
			"Conta Durango Brasil",
			"Entre com seu nickname e senha para continuar.\\n\\n"
				+ "Ainda não tem conta? Escolha Criar conta. "
				+ "O cadastro pedirá nickname, senha e confirmação da senha.",
			BeginLogin,
			BeginRegister,
			"Entrar",
			"Criar conta");'''

    if old_choice in text:
        text = text.replace(old_choice, new_choice, 1)
    elif "O cadastro pedirá nickname, senha e confirmação da senha." not in text:
        die("TitleMenuGroup ShowAuthChoice: trecho local esperado nao encontrado")

    old_error = '''	private void ShowAuthError(string message)
	{
		UserControl.ShowMessageBox(
			"Conta Durango Brasil",
			message,
			delegate
			{
				UserControl.CloseMessageBox();
				ShowAuthChoice();
			},
			null,
			"Voltar");
	}'''

    new_error = '''	private void ShowAuthError(string message)
	{
		UserControl.ShowMessageBox(
			"Conta Durango Brasil",
			message,
			delegate
			{
				if (_authInProgress)
				{
					ShowAuthChoice();
				}
			},
			null,
			"Voltar");
	}'''

    if old_error in text:
        text = text.replace(old_error, new_error, 1)
    elif "if (_authInProgress)\n\t\t\t\t{\n\t\t\t\t\tShowAuthChoice();" not in text:
        die("TitleMenuGroup ShowAuthError: trecho esperado nao encontrado")

    wordings = {
        'return "Usuário ou senha inválidos.";':
            'return "Nickname ou senha inválidos.";',
        'return "Esse usuário já está em uso.";':
            'return "Esse nickname já está em uso.";',
        'return "O usuário precisa ter pelo menos 3 caracteres.";':
            'return "O nickname precisa ter pelo menos 3 caracteres.";',
        'return "O usuário pode ter no máximo 32 caracteres.";':
            'return "O nickname pode ter no máximo 32 caracteres.";',
        'return "Use somente letras, números, ponto, hífen ou underline no usuário.";':
            'return "Use somente letras, números, ponto, hífen ou underline no nickname.";',
    }
    for old, new in wordings.items():
        text = text.replace(old, new)

    write_text(path, text, nl)


def patch_user_control(path: Path) -> None:
    text, nl = read_text(path)

    if CONTROL_MARKER not in text:
        pattern = (
            r"\tpublic virtual void ShowInputBox\(string title, string placeholder, "
            r"bool isPassword, int limit, Action<string> submitAction, "
            r"Action cancelAction = null\)\n"
            r"\t\{.*?\n\t\}\n"
        )

        replacement = r'''	// AUTH_UX_V2_INPUT_API
	public virtual void ShowInputBox(
		string title,
		string instruction,
		string placeholder,
		bool isPassword,
		int limit,
		Action<string> submitAction,
		Action cancelAction = null,
		string okButtonLabel = "Continuar")
	{
		_messageBox.ShowInput(
			title,
			instruction,
			placeholder,
			isPassword,
			limit,
			submitAction,
			cancelAction,
			okButtonLabel,
			"Voltar");
	}
'''

        text = replace_regex_one(
            text,
            pattern,
            replacement,
            "TitleMenuUserControlBase.ShowInputBox",
        )

    write_text(path, text, nl)


def patch_message_box(path: Path) -> None:
    text, nl = read_text(path)

    if MESSAGE_MARKER not in text:
        old_fields = '''	private UIInput _input;

	private bool _inputMode;

	private Action<string> _onInputSubmit;
'''

        new_fields = '''	// AUTH_UX_V2_MESSAGE_INPUT
	private UIInput _input;

	private UILabel _inputLabel;

	private UILabel _inputPlaceholderLabel;

	private UITexture _inputBorder;

	private UITexture _inputBackground;

	private bool _inputMode;

	private Action<string> _onInputSubmit;

	private Vector3 _messageBaseLocalPosition;

	private bool _inputVisualsCreated;
'''

        text = replace_one(
            text,
            old_fields,
            new_fields,
            "TitleMessageBoxBase campos de input",
        )

    show_input_pattern = (
        r"\tpublic virtual void ShowInput\(string title, string placeholder, "
        r"bool isPassword, int limit, Action<string> onSubmit, "
        r"Action onCancel = null, string okButtonLabel = null, "
        r"string cancelButtonLabel = null\)\n"
        r"\t\{.*?\n\t\}\n\n"
        r"\tprivate void SubmitInput\(\)"
    )

    if re.search(show_input_pattern, text, re.DOTALL):
        replacement = r'''	public virtual void ShowInput(
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

	private void SubmitInput()'''

        text = replace_regex_one(
            text,
            show_input_pattern,
            replacement,
            "TitleMessageBoxBase.ShowInput",
        )
    elif "private void EnsureInputVisuals()" not in text:
        die("TitleMessageBoxBase.ShowInput: bloco local esperado nao encontrado")

    submit_pattern = (
        r"\tprivate void SubmitInput\(\)\n"
        r"\t\{.*?\n\t\}\n\n"
        r"\tprivate void DisableInputMode\(\)"
    )

    if re.search(submit_pattern, text, re.DOTALL):
        submit_replacement = r'''	private void SubmitInput()
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

	private void DisableInputMode()'''

        text = replace_regex_one(
            text,
            submit_pattern,
            submit_replacement,
            "TitleMessageBoxBase.SubmitInput",
        )

    disable_pattern = (
        r"\tprivate void DisableInputMode\(\)\n"
        r"\t\{.*?\n\t\}\n\n"
        r"\tpublic virtual void Close\(\)"
    )

    if re.search(disable_pattern, text, re.DOTALL):
        disable_replacement = r'''	private void DisableInputMode()
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

	public virtual void Close()'''

        text = replace_regex_one(
            text,
            disable_pattern,
            disable_replacement,
            "TitleMessageBoxBase.DisableInputMode",
        )

    write_text(path, text, nl)


def validate(root: Path) -> None:
    title, _ = read_text(root / TITLE_REL)
    control, _ = read_text(root / CONTROL_REL)
    message, _ = read_text(root / MESSAGE_REL)

    title_needles = [
        TITLE_MARKER,
        "Entrar • etapa 1 de 2",
        "Entrar • etapa 2 de 2",
        "Cadastro • etapa 1 de 3",
        "Cadastro • etapa 2 de 3",
        "Cadastro • etapa 3 de 3",
        '"Confirmar senha"',
        "ShowAuthValidationError",
        'buttonText: "Criar conta"',
        "UserControl.ShowInputBox(",
    ]

    control_needles = [
        CONTROL_MARKER,
        "string instruction",
        "string placeholder",
        'string okButtonLabel = "Continuar"',
        "_messageBox.ShowInput(",
    ]

    message_needles = [
        MESSAGE_MARKER,
        "private UILabel _inputLabel;",
        "private UILabel _inputPlaceholderLabel;",
        "private UITexture _inputBorder;",
        "private UITexture _inputBackground;",
        'new GameObject("Auth Input Border")',
        'new GameObject("Auth Input Background")',
        "Texture2D.whiteTexture",
        "_okButton.Disabled = empty;",
        "private void UpdateInputVisualState()",
        "private void HideInputVisuals()",
    ]

    for needle in title_needles:
        if needle not in title:
            die(f"validacao TitleMenuGroup.cs: nao contem {needle!r}")

    for needle in control_needles:
        if needle not in control:
            die(f"validacao TitleMenuUserControlBase.cs: nao contem {needle!r}")

    for needle in message_needles:
        if needle not in message:
            die(f"validacao TitleMessageBoxBase.cs: nao contem {needle!r}")

    if "_input = _message.gameObject.AddComponent<UIInput>();" in message:
        die(
            "validacao: TitleMessageBoxBase ainda reutiliza _message "
            "como o label do UIInput"
        )

    if '{ "username", username }' not in title:
        die("validacao: chave HTTP username nao encontrada")
    if '{ "password", password }' not in title:
        die("validacao: chave HTTP password nao encontrada")
    if '{ "password_confirm", confirmation }' not in title:
        die("validacao: chave HTTP password_confirm nao encontrada")


def run(cmd: list[str], cwd: Path, *, optional: bool = False) -> bool:
    print("+", " ".join(cmd))
    try:
        subprocess.run(cmd, cwd=cwd, check=True)
        return True
    except FileNotFoundError:
        if optional:
            print(f"AVISO: {cmd[0]} nao encontrado; etapa ignorada")
            return False
        die(f"{cmd[0]} nao encontrado no PATH")
    except subprocess.CalledProcessError as exc:
        die(f"comando falhou ({exc.returncode}): {' '.join(cmd)}")
    return False


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument(
        "--repo",
        default=str(Path(__file__).resolve().parent),
        help="raiz do Project_DX-D-Brasil",
    )
    ap.add_argument(
        "--no-build",
        action="store_true",
        help="aplica o patch sem compilar o cliente",
    )
    ap.add_argument(
        "--no-backup",
        action="store_true",
        help="nao cria backups .pre-login-ux-v2.bak",
    )
    args = ap.parse_args()

    root = Path(args.repo).resolve()
    required = [
        root / TITLE_REL,
        root / CONTROL_REL,
        root / MESSAGE_REL,
        root / SRC_REL / "Assembly-CSharp.csproj",
    ]

    missing = [str(p) for p in required if not p.is_file()]
    if missing:
        die(
            "arquivos obrigatorios nao encontrados. "
            "Confirme se o script esta na raiz do repo:\n  "
            + "\n  ".join(missing)
        )

    title, _ = read_text(root / TITLE_REL)
    control, _ = read_text(root / CONTROL_REL)
    message, _ = read_text(root / MESSAGE_REL)

    if TITLE_MARKER not in title and "UserControl.ShowInputBox(" not in title:
        die(
            "TitleMenuGroup.cs nao possui o ShowInputBox local esperado. "
            "Nao vou adivinhar outra implementacao."
        )

    if CONTROL_MARKER not in control and "_messageBox.ShowInput(" not in control:
        die(
            "TitleMenuUserControlBase.cs nao possui o ShowInput local esperado."
        )

    if MESSAGE_MARKER not in message:
        for needle in (
            "private UIInput _input;",
            "private bool _inputMode;",
            "private Action<string> _onInputSubmit;",
            "_input.label = _message;",
        ):
            if needle not in message:
                die(
                    "TitleMessageBoxBase.cs divergiu do diff local enviado; "
                    f"nao encontrei {needle!r}"
                )

    if not args.no_backup:
        for rel in (TITLE_REL, CONTROL_REL, MESSAGE_REL, DLL_REL):
            path = root / rel
            if path.is_file():
                backup_once(path)

    patch_title_menu(root / TITLE_REL)
    patch_user_control(root / CONTROL_REL)
    patch_message_box(root / MESSAGE_REL)

    validate(root)
    print()
    print("OK: patch de UI/UX aplicado e validado.")

    if not args.no_build:
        run(
            ["dotnet", "build", "Assembly-CSharp.csproj", "-c", "Debug"],
            root / SRC_REL,
        )

        dll = root / DLL_REL
        if not dll.is_file():
            die(
                "o build terminou, mas Assembly-CSharp.dll nao foi encontrado "
                "no diretorio Managed"
            )

        print("OK: Assembly-CSharp.dll recompilado e implantado no cliente.")

    if (root / ".git").exists():
        run(
            [
                "git",
                "-c",
                "core.whitespace=cr-at-eol",
                "diff",
                "--check",
                "--",
                TITLE_REL.as_posix(),
                CONTROL_REL.as_posix(),
                MESSAGE_REL.as_posix(),
            ],
            root,
            optional=True,
        )

        print()
        print("Status dos arquivos do patch:")
        subprocess.run(
            [
                "git",
                "status",
                "--short",
                "--",
                TITLE_REL.as_posix(),
                CONTROL_REL.as_posix(),
                MESSAGE_REL.as_posix(),
                DLL_REL.as_posix(),
            ],
            cwd=root,
            check=False,
        )

    print()
    print("Teste no client:")
    print("  Login:    1/2 Nickname -> 2/2 Senha -> Entrar")
    print("  Cadastro: 1/3 Nickname -> 2/3 Senha -> 3/3 Confirmar senha")
    print()
    print("Verifique visualmente:")
    print("  - caixa escura com borda amarela")
    print("  - placeholder visivel antes de digitar")
    print("  - clique em qualquer ponto da caixa foca o campo")
    print("  - botao principal bloqueado com campo vazio")
    print("  - senha mascarada")
    print("  - textos de etapa e instrucao visiveis")


if __name__ == "__main__":
    main()
