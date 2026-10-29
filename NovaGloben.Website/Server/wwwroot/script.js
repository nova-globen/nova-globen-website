/* Nova Globen AB — Site Script */

/* ─── Footer year ─── */
const yearEl = document.getElementById('footer-year');
if (yearEl) yearEl.textContent = new Date().getFullYear();

/* ─── Mobile nav toggle ─── */
const navToggle = document.querySelector('.nav-toggle');
const mainNav = document.getElementById('main-nav');

if (navToggle && mainNav) {
  navToggle.addEventListener('click', () => {
    const expanded = navToggle.getAttribute('aria-expanded') === 'true';
    navToggle.setAttribute('aria-expanded', String(!expanded));
    mainNav.classList.toggle('open', !expanded);
  });

  document.addEventListener('click', (e) => {
    if (!navToggle.contains(e.target) && !mainNav.contains(e.target)) {
      navToggle.setAttribute('aria-expanded', 'false');
      mainNav.classList.remove('open');
    }
  });

  mainNav.querySelectorAll('.nav-link').forEach(link => {
    link.addEventListener('click', () => {
      navToggle.setAttribute('aria-expanded', 'false');
      mainNav.classList.remove('open');
    });
  });

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && mainNav.classList.contains('open')) {
      navToggle.setAttribute('aria-expanded', 'false');
      mainNav.classList.remove('open');
      navToggle.focus();
    }
  });
}

/* ─── Payment links ─── */
const paymentLinks = {
  consultation: null,
  fixedScopeProject: null,
  invoicePayment: null
};

/* ─── Turnstile — always get a fresh token per submission ─── */
let _cfResolve = null;

// Called by Cloudflare widget via data-callback="onTurnstileComplete"
window.onTurnstileComplete = function (token) {
  if (_cfResolve) { _cfResolve(token); _cfResolve = null; }
};

function getFreshToken() {
  return new Promise((resolve) => {
    if (typeof turnstile === 'undefined') { resolve(''); return; }

    const widget = document.querySelector('.cf-turnstile');
    if (!widget) { resolve(''); return; }

    _cfResolve = resolve;
    turnstile.reset(widget); // clears the old token and triggers a new silent challenge

    // Safety timeout — resolve with empty string if Cloudflare takes too long
    setTimeout(() => {
      if (_cfResolve) { _cfResolve(''); _cfResolve = null; }
    }, 20000);
  });
}

/* ─── Contact form ─── */
const form = document.getElementById('contact-form');
const statusEl = document.getElementById('form-status');
const submitBtn = document.getElementById('submit-btn');

const lang = document.documentElement.lang || 'en';

const messages = {
  en: {
    success: 'Thank you. Your message has been sent. We will reply as soon as possible.',
    error: 'We could not send your message right now. Please email us at <a href="mailto:info@nova-globen.se">info@nova-globen.se</a>',
    validationName: 'Please enter your name.',
    validationEmail: 'Please enter a valid email address.',
    validationMessage: 'Please enter a message (at least 20 characters).',
    validationConsent: 'Please accept the privacy policy to continue.',
    sending: 'Sending…',
    send: 'Send message',
    popupSuccessTitle: 'Message sent',
    popupErrorTitle: 'Could not send message',
    popupOk: 'Close'
  },
  sv: {
    success: 'Tack. Ditt meddelande har skickats. Vi återkommer så snart som möjligt.',
    error: 'Vi kunde inte skicka ditt meddelande just nu. Kontakta oss på <a href="mailto:info@nova-globen.se">info@nova-globen.se</a>',
    validationName: 'Ange ditt namn.',
    validationEmail: 'Ange en giltig e-postadress.',
    validationMessage: 'Ange ett meddelande (minst 20 tecken).',
    validationConsent: 'Godkänn integritetspolicyn för att fortsätta.',
    sending: 'Skickar…',
    send: 'Skicka meddelande',
    popupSuccessTitle: 'Meddelande skickat',
    popupErrorTitle: 'Kunde inte skicka',
    popupOk: 'Stäng'
  }
};

const t = messages[lang] || messages.en;

function showStatus(type, text) {
  if (!statusEl) return;
  statusEl.textContent = text;
  statusEl.className = `form-status form-status--${type}`;
  statusEl.setAttribute('data-visible', 'true');
  statusEl.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function hideStatus() {
  if (!statusEl) return;
  statusEl.removeAttribute('data-visible');
  statusEl.textContent = '';
  statusEl.className = 'form-status';
}

/* ─── Popup ─── */
const popupOverlay = document.getElementById('popup-overlay');
const popupIcon = document.getElementById('popup-icon');
const popupTitleEl = document.getElementById('popup-title');
const popupMsg = document.getElementById('popup-message');
const popupCloseBtn = document.getElementById('popup-close');
const popupOkBtn = document.getElementById('popup-btn');

function showPopup(type, html) {
  if (!popupOverlay) return;
  popupIcon.textContent = type === 'success' ? '✅' : '❌';
  popupTitleEl.textContent = type === 'success' ? t.popupSuccessTitle : t.popupErrorTitle;
  popupMsg.innerHTML = html;
  popupOkBtn.textContent = t.popupOk;
  popupCloseBtn.setAttribute('aria-label', t.popupOk);
  popupOverlay.removeAttribute('hidden');
  popupOkBtn.focus();
}

function closePopup() {
  if (!popupOverlay) return;
  popupOverlay.setAttribute('hidden', '');
  if (submitBtn) submitBtn.focus();
}

if (popupCloseBtn) popupCloseBtn.addEventListener('click', closePopup);
if (popupOkBtn) popupOkBtn.addEventListener('click', closePopup);
if (popupOverlay) {
  popupOverlay.addEventListener('click', (e) => { if (e.target === popupOverlay) closePopup(); });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && !popupOverlay.hasAttribute('hidden')) closePopup();
  });
}

/* ─── Disable / enable all form fields ─── */
function setFormDisabled(disabled) {
  if (!form) return;
  form.querySelectorAll('input, select, textarea, button').forEach(el => { el.disabled = disabled; });
}

function validateForm(data) {
  if (!data.name || data.name.trim().length < 1) return t.validationName;
  if (!data.email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(data.email)) return t.validationEmail;
  if (!data.message || data.message.trim().length < 20) return t.validationMessage;
  const consentEl = form.querySelector('#consent');
  if (consentEl && !consentEl.checked) return t.validationConsent;
  return null;
}

if (form) {
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    hideStatus();

    const fd = new FormData(form);
    const data = {
      name: fd.get('name') || '',
      company: fd.get('company') || '',
      email: fd.get('email') || '',
      phone: fd.get('phone') || '',
      serviceInterest: fd.get('serviceInterest') || '',
      message: fd.get('message') || '',
      website: fd.get('website') || '',
      turnstileToken: ''
    };

    const validationError = validateForm(data);
    if (validationError) {
      showStatus('error', validationError);
      return;
    }

    setFormDisabled(true);
    submitBtn.classList.add('btn--loading');
    submitBtn.textContent = t.sending;

    // Always request a fresh Turnstile token — never reuse a previous one
    data.turnstileToken = await getFreshToken();

    try {
      const res = await fetch('/api/contact', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
      });

      const json = await res.json();

      if (json.ok || json.Ok) {
        form.reset();
        showPopup('success', t.success);
      } else {
        showPopup('error', json.message || t.error);
      }
    } catch {
      showPopup('error', t.error);
    } finally {
      setFormDisabled(false);
      submitBtn.classList.remove('btn--loading');
      submitBtn.textContent = t.send;
    }
  });
}
