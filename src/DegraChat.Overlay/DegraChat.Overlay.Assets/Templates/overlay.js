/**
 * ChatOverlay Studio — Overlay Client
 * Connects to local WebSocket server and renders chat messages.
 * Runs inside OBS Browser Source (Chromium).
 */
(function() {
    'use strict';

    const container = document.getElementById('chat-container');
    if (!container) { console.error('ChatOverlay: #chat-container not found'); return; }

    const config = window.COS_CONFIG || {};
    const wsUrl = config.wsUrl || `ws://${window.location.host}/ws`;
    const maxMessages = config.maxMessages || 30;
    const displayTimeMs = config.displayTimeMs || 0;
    const animationDurationMs = config.animationDurationMs || 300;
    const showPlatformIcon = config.showPlatformIcon !== false;
    const showSeparator = config.showSeparator || false;

    let ws;
    let reconnectDelay = 1000;

    // ---- WebSocket Connection ----

    function connect() {
        try {
            ws = new WebSocket(wsUrl);
        } catch(e) {
            console.error('ChatOverlay: WebSocket creation failed', e);
            scheduleReconnect();
            return;
        }

        ws.onopen = () => {
            console.log('ChatOverlay: Connected to', wsUrl);
            reconnectDelay = 1000;
        };

        ws.onmessage = (event) => {
            try {
                const msg = JSON.parse(event.data);
                handleMessage(msg);
            } catch(e) {
                console.error('ChatOverlay: Parse error', e);
            }
        };

        ws.onclose = () => {
            console.log('ChatOverlay: Disconnected');
            scheduleReconnect();
        };

        ws.onerror = (e) => {
            console.error('ChatOverlay: WebSocket error', e);
        };
    }

    function scheduleReconnect() {
        setTimeout(() => {
            reconnectDelay = Math.min(reconnectDelay * 1.5, 10000);
            connect();
        }, reconnectDelay);
    }

    // ---- Message Handling ----

    function handleMessage(msg) {
        switch(msg.type) {
            case 'message':
                addMessage(msg.data);
                break;
            case 'clear':
                clearChat();
                break;
            case 'connected':
                console.log('ChatOverlay: Server welcome -', msg.message);
                break;
        }
    }

    function addMessage(data) {
        const el = document.createElement('div');
        el.className = 'chat-message' +
            (data.isSystem ? ' system' : '') +
            (data.isHighlighted ? ' highlighted' : '') +
            (showShadow() ? ' shadow' : '');

        let html = '';

        // Platform icon
        if (showPlatformIcon && data.platform) {
            html += `<img class="platform-icon" src="${platformIconSvg(data.platform)}" alt="${data.platform}">`;
        }

        // Badges
        if (data.badges && data.badges.length > 0) {
            html += '<span class="badges">';
            data.badges.forEach(b => {
                html += `<img src="${b}" alt="badge">`;
            });
            html += '</span>';
        }

        // Username
        html += `<span class="username" style="color:${data.userColor || '#FFF'}">${escapeHtml(data.displayName || 'Anonymous')}</span>`;

        // Text with emotes
        html += `<span class="text">${processEmotes(data.text || '', data.emotes || {})}</span>`;

        el.innerHTML = html;

        // Insert at top (newest first in column-reverse)
        if (container.firstChild) {
            container.insertBefore(el, container.firstChild);
        } else {
            container.appendChild(el);
        }

        // Limit message count
        while (container.children.length > maxMessages) {
            container.removeChild(container.lastChild);
        }

        // Auto-hide after display time
        if (displayTimeMs > 0) {
            setTimeout(() => {
                el.style.animation = `messageOut ${animationDurationMs}ms ease forwards`;
                setTimeout(() => {
                    if (el.parentNode) el.remove();
                }, animationDurationMs);
            }, displayTimeMs);
        }
    }

    function clearChat() {
        container.innerHTML = '';
    }

    // ---- Helpers ----

    function processEmotes(text, emotes) {
        if (!emotes || Object.keys(emotes).length === 0) return escapeHtml(text);

        let result = escapeHtml(text);
        for (const [name, url] of Object.entries(emotes)) {
            const escaped = escapeHtml(name);
            const regex = new RegExp(escaped.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'g');
            result = result.replace(regex, `<img class="emote" src="${url}" alt="${escaped}">`);
        }
        return result;
    }

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    function platformIconSvg(platform) {
        const colors = {
            twitch: '#9146FF',
            goodgame: '#00CC00',
            kick: '#53FC18',
            vkplay: '#0077FF',
            youtube: '#FF0000',
            test: '#999999'
        };
        const color = colors[platform] || '#999999';
        return `data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 18 18'><circle cx='9' cy='9' r='8' fill='${encodeURIComponent(color)}'/></svg>`;
    }

    function showShadow() {
        return getComputedStyle(document.documentElement)
            .getPropertyValue('--cos-shadow-blur') !== '0px';
    }

    // ---- Start ----
    connect();

})();
