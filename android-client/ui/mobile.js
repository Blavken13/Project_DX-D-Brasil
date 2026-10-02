(() => {
  'use strict';
  const $ = (selector) => document.querySelector(selector);
  const bridge = window.PrimalAndroid || null;
  let busy = false;
  let register = false;
  let authenticated = false;
  let updateRequired = false;
  let continueAfterAuth = false;

  function notice(text, ok) {
    $('#aviso').textContent = text || '';
    $('#aviso').classList.toggle('ok', !!ok);
    if (text && !ok) continueAfterAuth = false;
  }

  function lock(value) {
    busy = !!value;
    $('#barra').hidden = !busy;
    $('#auth-form').setAttribute('aria-busy', String(busy));
    ['#submit-auth', '#switch-auth', '#sair', '#email', '#senha', '#confirmar-senha'].forEach((selector) => {
      $(selector).disabled = busy;
    });
    $('#jogar').disabled = busy || updateRequired;
    $('#submit-auth').disabled = busy || updateRequired;
  }

  function mode(create) {
    if (busy) return;
    register = !!create;
    document.body.classList.toggle('register-mode', register);
    continueAfterAuth = false;
    $('#form-title').textContent = register ? 'Criar conta' : 'Entrar';
    $('#form-description').textContent = register ? 'Sua aventura começa aqui.' : 'Bem-vindo ao alfa de Lost Horizon.';
    $('#confirmation-field').hidden = !register;
    $('#confirmar-senha').required = register;
    $('#confirmar-senha').value = '';
    $('#senha').autocomplete = register ? 'new-password' : 'current-password';
    $('#senha').placeholder = register ? 'Mínimo 8 caracteres' : 'Sua senha';
    $('#senha').enterKeyHint = register ? 'next' : 'go';
    $('#submit-auth').textContent = register ? 'CRIAR CONTA' : 'ENTRAR';
    $('#switch-auth').textContent = register ? 'Já tenho uma conta' : 'Criar uma conta';
    notice('');
  }

  function play() {
    if (!bridge || busy || updateRequired || !authenticated) return;
    $('#background-video').pause();
    bridge.play();
  }

  function submit(event) {
    event.preventDefault();
    if (busy || updateRequired) return;
    const username = $('#email').value.trim();
    const password = $('#senha').value;
    if (!/^[A-Za-z0-9_.-]{3,32}$/.test(username)) {
      notice('Use um usuário de 3 a 32 letras, números, ponto, hífen ou sublinhado.');
      $('#email').focus();
      return;
    }
    if (!password || password.length > 128) {
      notice('Informe sua senha.');
      $('#senha').focus();
      return;
    }
    if (register && password.length < 8) {
      notice('A senha precisa ter pelo menos 8 caracteres.');
      $('#senha').focus();
      return;
    }
    if (register && password !== $('#confirmar-senha').value) {
      notice('As senhas não coincidem.');
      $('#confirmar-senha').focus();
      return;
    }
    if (!bridge) {
      notice('Prévia do login. Abra o aplicativo para entrar ou criar sua conta.');
      return;
    }
    notice('');
    continueAfterAuth = true;
    document.activeElement.blur();
    const action = register ? 'signUp' : 'signIn';
    bridge[action](username, password);
  }

  function resumeVideo() {
    const video = $('#background-video');
    if (document.hidden) { video.pause(); return; }
    video.muted = true;
    const attempt = video.play();
    if (attempt && attempt.catch) attempt.catch(() => {});
  }

  // Preserve the Java callback contract. News is deliberately absent from this screen.
  window.PRIMAL = {
    news() {},
    status(online) {
      const connected = typeof online === 'number' && online >= 0;
      $('#dot').className = 'dot ' + (connected ? 'on' : 'off');
      $('#online').textContent = connected ? 'Lost Horizon · Servidor online' : 'Servidor indisponível no momento';
    },
    account(username) {
      authenticated = !!username;
      $('#fora').hidden = authenticated;
      $('#dentro').hidden = !authenticated;
      $('#email-conta').textContent = username || '';
      if (authenticated) {
        $('#senha').value = '';
        $('#confirmar-senha').value = '';
        if (continueAfterAuth) {
          continueAfterAuth = false;
          setTimeout(play, 0);
        }
      } else {
        continueAfterAuth = false;
        mode(false);
        resumeVideo();
      }
    },
    rememberEmail(username) { if (username && !$('#email').value) $('#email').value = username; },
    busy(value) { lock(value); if (!busy) resumeVideo(); },
    notice,
    update(text, button) {
      updateRequired = !!text;
      $('#atualiza').hidden = !text;
      $('#atualiza-texto').textContent = text || '';
      $('#atualiza-botao').hidden = !button;
      lock(busy);
    },
    version(text) { $('#versao').textContent = text || 'Lost Horizon · Alfa'; }
  };

  document.addEventListener('DOMContentLoaded', () => {
    $('#auth-form').addEventListener('submit', submit);
    $('#switch-auth').addEventListener('click', () => mode(!register));
    $('#jogar').addEventListener('click', play);
    $('#sair').addEventListener('click', () => {
      if (bridge && !busy) {
        $('#email').value = '';
        $('#senha').value = '';
        $('#confirmar-senha').value = '';
        notice(''); bridge.signOut();
      }
    });
    $('#atualiza-botao').addEventListener('click', () => { if (bridge) bridge.update(); });
    document.addEventListener('visibilitychange', resumeVideo);
    document.addEventListener('pointerdown', () => { if ($('#background-video').paused && !busy) resumeVideo(); }, { passive:true });
    $('#background-video').addEventListener('error', () => { $('#background-video').hidden = true; });
    if (window.visualViewport) {
      let normalHeight = window.innerHeight;
      const resize = () => {
        const editing = document.activeElement && document.activeElement.tagName === 'INPUT';
        if (!editing) normalHeight = window.innerHeight;
        document.documentElement.style.setProperty('--viewport-height', window.visualViewport.height + 'px');
        document.body.classList.toggle('keyboard-visible', editing && window.visualViewport.height < normalHeight * .7);
      };
      window.visualViewport.addEventListener('resize', resize);
      document.addEventListener('focusin', resize);
      document.addEventListener('focusout', resize);
      resize();
    }
    resumeVideo();
    if (bridge) bridge.ready();
    else {
      $('#online').textContent = 'Prévia local';
      window.PRIMAL.version('Lost Horizon · Prévia');
    }
  });
})();
