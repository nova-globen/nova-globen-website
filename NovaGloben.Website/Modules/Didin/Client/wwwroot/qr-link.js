// "Sign in with your phone" on the sign-in page of a TV or computer. Runs in the browser, so the server sees
// this device's own address and browser when the phone asks "which device is this?". Asks for a code, shows its QR,
// asks every few seconds whether the phone approved, and then posts the code and its secret to {site}/api/link/signin, which
// sets the sign-in cookie and opens the catalog. Phones sign in with the form: nothing starts below 761 px.
let timer = 0;

let base = '';

export function start(root, siteBase) {
    base = siteBase || '';
    if (!root || !matchMedia('(min-width: 761px)').matches) return;
    root.hidden = false;
    root.querySelector('.bin-qr-new').addEventListener('click', () => fresh(root));
    fresh(root);
}

function el(root, cls) { return root.querySelector(cls); }

function say(root, text, state) {
    const s = el(root, '.bin-qr-status');
    s.textContent = text;
    root.dataset.state = state;
}

async function fresh(root) {
    clearTimeout(timer);
    el(root, '.bin-qr-new').hidden = true;
    say(root, '', 'loading');
    let d;
    try {
        const r = await fetch(base + '/api/link/start', {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' }, body: '{}'
        });
        if (!r.ok) throw new Error(String(r.status));
        d = await r.json();
    } catch {
        say(root, 'Phone sign-in is not available right now. Use your username and password.', 'error');
        el(root, '.bin-qr-new').hidden = false;
        return;
    }
    el(root, '.bin-qr-img').src = d.qr;
    el(root, '.bin-qr-code').textContent = d.code;
    el(root, '.bin-qr-url').textContent = d.url.replace(/^https?:\/\//, '');
    say(root, 'Waiting for your phone...', 'waiting');
    const until = Date.now() + d.expiresIn * 1000;
    const every = Math.max(2, d.interval || 3) * 1000;

    const poll = async () => {
        if (!document.contains(root)) return;                     // signed in another way: stop
        if (Date.now() > until) return expired(root);
        let status = 'pending';
        try {
            const r = await fetch(base + '/api/link/poll', {
                method: 'POST', credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code: d.code, secret: d.secret })
            });
            if (r.ok) status = (await r.json()).status;
        } catch { /* offline for a moment: ask again */ }
        if (status === 'approved') return signIn(root, d);
        if (status === 'denied') {
            say(root, 'Denied on the phone. Show a new code to try again.', 'denied');
            el(root, '.bin-qr-new').hidden = false;
            return;
        }
        if (status === 'expired' || status === 'used') return expired(root);
        timer = setTimeout(poll, every);
    };
    timer = setTimeout(poll, every);
}

function expired(root) {
    say(root, 'This code has expired.', 'expired');
    el(root, '.bin-qr-new').hidden = false;
}

function signIn(root, d) {
    say(root, 'Approved. Signing in...', 'approved');
    const f = document.createElement('form');
    f.method = 'post';
    f.action = base + '/api/link/signin';
    for (const [k, v] of [['code', d.code], ['secret', d.secret]]) {
        const i = document.createElement('input');
        i.type = 'hidden'; i.name = k; i.value = v;
        f.appendChild(i);
    }
    document.body.appendChild(f);
    f.submit();
}
