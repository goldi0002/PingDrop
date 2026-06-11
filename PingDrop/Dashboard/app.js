(function () {
    var activeSection;
    class PingDropSSE {
        constructor(options = {}) {
            this.eventSource = null;
            this.url = null;
            this.handlers = new Map();
            this.reconnectDelay = 1000;
            this.maxReconnectDelay = 30000;
            this.reconnectTimer = null;
            this.isManualDisconnect = false;
            this.logger = options.logger || console;
        }
        on(eventType, handler) {
            if (!this.handlers.has(eventType)) {
                this.handlers.set(eventType, []);
                this._ensureEventListener(eventType);
            }
            this.handlers.get(eventType).push(handler);
            return this;
        }
        off(eventType, handler) {
            const list = this.handlers.get(eventType);
            if (list) {
                const idx = list.indexOf(handler);
                if (idx > -1) list.splice(idx, 1);
                if (list.length === 0) {
                    this.handlers.delete(eventType);
                    if (this.eventSource) {
                        this.eventSource.removeEventListener(eventType, this._eventListener);
                    }
                }
            }
            return this;
        }
        connect(url) {
            this.url = url;
            this.isManualDisconnect = false;
            this._createConnection();
            return this;
        }
        disconnect() {
            this.isManualDisconnect = true;
            this._clearReconnectTimer();
            if (this.eventSource) {
                this.eventSource.close();
                this.eventSource = null;
                this.logger.log('SSE connection closed manually');
            }
            return this;
        }
        _createConnection() {
            if (this.eventSource) this.eventSource.close();
            this.eventSource = new EventSource(this.url);
            this.eventSource.onopen = () => {
                this.logger.log('SSE connection opened');
                this.reconnectDelay = 1000;
            };
            this.eventSource.onerror = (e) => {
                this.logger.error('SSE error', e);
                if (this.eventSource.readyState === EventSource.CLOSED) {
                    this.logger.log('SSE connection closed by server/network');
                    this._scheduleReconnect();
                }
            };
            for (const eventType of this.handlers.keys()) {
                this._ensureEventListener(eventType);
            }
        }
        _ensureEventListener(eventType) {
            this.eventSource?.removeEventListener(eventType, this._eventListener);
            this._eventListener = this._handleEvent.bind(this, eventType);
            this.eventSource?.addEventListener(eventType, this._eventListener);
        }
        _handleEvent(eventType, event) {
            const handlers = this.handlers.get(eventType);
            if (!handlers) return;
            let payload = null;
            try { payload = JSON.parse(event.data); } catch (e) {
                this.logger.warn('Failed to parse SSE data:', event.data, e);
                return;
            }
            for (const handler of handlers) {
                try { handler(payload, event); } catch (err) {
                    this.logger.error(`Error in event handler for '${eventType}'`, err);
                }
            }
        }
        _scheduleReconnect() {
            if (this.isManualDisconnect || this.reconnectTimer) return;
            this.reconnectTimer = setTimeout(() => {
                this.reconnectTimer = null;
                if (!this.isManualDisconnect) {
                    this.logger.log(`Attempting reconnect in ${this.reconnectDelay}ms...`);
                    this._createConnection();
                    this.reconnectDelay = Math.min(this.reconnectDelay * 2, this.maxReconnectDelay);
                }
            }, this.reconnectDelay);
        }
        _clearReconnectTimer() {
            if (this.reconnectTimer) { clearTimeout(this.reconnectTimer); this.reconnectTimer = null; }
        }
    }
    class PingDropCommon {
        constructor() {
            this.$toastContainer = document.getElementById('toastContainer');
            this.$logBox = document.getElementById('logBox');
            this.logLines = [];
            this.MAX_LOG_LINES = 200;
            this.logPaused = false;
        }
        showToast(message, type = 'info') {
            const toast = document.createElement('div');
            toast.className = 'toast-custom';
            const icons = { info: 'ri-information-line', success: 'ri-check-line', error: 'ri-error-warning-line', warning: 'ri-alert-line' };
            toast.innerHTML = `<i class="${icons[type] || icons.info}" style="color:var(--accent)"></i> ${message}`;
            this.$toastContainer.appendChild(toast);
            setTimeout(() => { toast.classList.add('fade-out'); setTimeout(() => toast.remove(), 300); }, 2800);
        }
        addLogLine(level, message) {
            const cls = 'log-' + level.toLowerCase();
            this.logLines.push({ cls, message: `[${level.toUpperCase()}] ${message}` });
            if (this.logLines.length > this.MAX_LOG_LINES) this.logLines.shift();
            if (!this.logPaused && activeSection === 'logs') {
                this.renderLogLine(this.logLines[this.logLines.length - 1]);
            }
            if (!this.logPaused && activeSection !== 'logs' && this.logLines.length <= this.MAX_LOG_LINES) {
                if (this.$logBox.children.length > this.MAX_LOG_LINES + 10) {
                    while (this.$logBox.children.length > this.MAX_LOG_LINES) this.$logBox.removeChild(this.$logBox.firstChild);
                }
            }
        }
        renderLogLine(line) {
            const span = document.createElement('span');
            span.className = 'log-line ' + line.cls;
            span.textContent = line.message;
            this.$logBox.appendChild(span);
            this.$logBox.appendChild(document.createTextNode('\n'));
            if (this.$logBox.children.length > this.MAX_LOG_LINES + 20) {
                while (this.$logBox.children.length > this.MAX_LOG_LINES) {
                    this.$logBox.removeChild(this.$logBox.firstChild);
                    if (this.$logBox.firstChild && this.$logBox.firstChild.nodeType === 3) this.$logBox.removeChild(this.$logBox.firstChild);
                }
            }
            if (!this.logPaused) this.scrollLogToBottom();
        }
        scrollLogToBottom() {
            requestAnimationFrame(() => { this.$logBox.scrollTop = this.$logBox.scrollHeight; });
        }
        rebuildLogBox() {
            this.$logBox.innerHTML = '';
            this.logLines.forEach(line => {
                const span = document.createElement('span');
                span.className = 'log-line ' + line.cls;
                span.textContent = line.message;
                this.$logBox.appendChild(span);
                this.$logBox.appendChild(document.createTextNode('\n'));
            });
            this.scrollLogToBottom();
        }
    }
    class PingDropTheme {
        constructor() {
            this.$body = document.body;
            this.$themeToggle = document.getElementById('themeToggle');
            this.$themeIcon = document.getElementById('themeIcon');
            this.STORAGE_KEY = 'pingdrop-theme';
        }
        Initialize() {
            const saved = localStorage.getItem(this.STORAGE_KEY);
            if (saved === "light") { this.setTheme('light'); } else { this.setTheme('dark'); }
            this.$themeToggle.addEventListener('click', () => this.toggleTheme());
        }
        setTheme(theme) {
            if (theme === 'light') {
                this.$body.classList.add('theme-light');
                this.$body.classList.remove('theme-dark');
                this.$themeIcon.classList.remove('ri-sun-line');
                this.$themeIcon.classList.add('ri-moon-line');
            } else {
                this.$body.classList.add('theme-dark');
                this.$body.classList.remove('theme-light');
                this.$themeIcon.classList.remove('ri-moon-line');
                this.$themeIcon.classList.add('ri-sun-line');
            }
            localStorage.setItem(this.STORAGE_KEY, theme);
        }
        toggleTheme() {
            const isLight = this.$body.classList.contains('theme-light');
            this.setTheme(isLight ? 'dark' : 'light');
        }
    }
    class PingDropService extends PingDropCommon {
        constructor() {
            super();
            if (!window.pingdrop.baseUrl) { throw new Error("PingDropService requires window.pingdrop.baseUrl"); }
            this.baseUrl = window.pingdrop.baseUrl;
        }
        getQstring(params) {
            if (params == null) return '';
            let json; try { json = JSON.stringify(params); } catch (e) { return ''; }
            if (json === '{}' || json === '[]') return '';
            return `?q=${encodeURIComponent(json)}`;
        }
        async get(path, params = null) {
            const response = await fetch(`${this.baseUrl}/${path}${this.getQstring(params)}`, {
                headers: { Accept: 'application/json' }, cache: 'no-store', method: 'GET'
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            return response.json();
        }
        async post(path, data = null) {
            const response = await fetch(`${this.baseUrl}/${path}`, {
                method: 'POST',
                cache: 'no-store',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: data == null ? null : JSON.stringify(data)
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }
            const contentType = response.headers.get('content-type');
            if (contentType && contentType.includes('application/json')) {
                return response.json();
            }
            return response.text();
        }
        async getAbout() {
            try { const data = await this.get('about'); document.dispatchEvent(new CustomEvent('aboutUpdated', { detail: data })); }
            catch (e) { this.showToast('Failed to load about info', 'error'); }
        }
        async getMetrics() {
            try { const data = await this.get('metrics'); document.dispatchEvent(new CustomEvent('metricsUpdated', { detail: data })); }
            catch (error) { this.showToast('Failed to load metrics', 'error'); }
        }
        async getConnections(status, searchTxt = '') {
            try { const data = await this.get('connections', { status, searchTxt }); document.dispatchEvent(new CustomEvent('connectionsUpdated', { detail: data })); }
            catch (e) { this.showToast('Failed to load connections', 'error'); }
        }
        async sendNotification(notificationObj) {
            try {
                const data = await this.post("send-notification", notificationObj);
                return data;
            } catch (e) {
                this.showToast("Failed to send notification", "error");
            }
        }
    }
    class PingDropDashboard {
        constructor() {
            this.$overviewMetricDiv = document.getElementById('overviewMetrics');
            this.$connectionsTableBody = document.getElementById("connTable").querySelector('tbody');
            this.$connectionsTableEmpty = document.getElementById('connEmpty');
            this.$activeConnectionCount = document.getElementById('connCount');
            this.$aboutTable = document.getElementById('aboutPingdropTbl');
        }
        renderMetrics(metrics) {
            if (!metrics) return;
            this.$overviewMetricDiv.innerHTML = metrics.map(m =>
                `<div class="metric-card">
                            <div class="metric-icon-sm"><i class="${m.icon}"></i></div>
                            <div class="metric-value-lg">${m.value}</div>
                            <div class="metric-label-sm">${m.label}</div>
                            <div class="metric-sub-sm">${m.sub}</div>
                        </div>`).join('');
        }
        renderConnectionTable(connections) {
            if (!connections) { this.$connectionsTableEmpty.style.display = 'block'; return; }
            this.$connectionsTableEmpty.style.display = 'none';
            this.$connectionsTableBody.innerHTML = '';
            connections.forEach(c => {
                const badgeClass = c.status === 'Active' ? 'badge-online' : c.status === 'Idle' ? 'badge-idle' : 'badge-offline';
                this.$connectionsTableBody.innerHTML += `<tr>
                            <td><code style="font-size:0.72rem">${c.connectionId}</code></td>
                            <td>${c.targetKey}</td><td>${c.connectedAt}</td><td>${c.lastActivityAt}</td>
                            <td>${c.eventsDelivered}</td><td>${c.idleDuration}</td><td>${c.subscribedEvents}</td>
                            <td><span class="badge-status ${badgeClass}">${c.status}</span></td></tr>`;
            });
            const activeCount = connections.filter(c => c.status === 'Active').length;
            this.$activeConnectionCount.textContent = activeCount + ' active';
        }
        renderAboutTable(about) {
            this.$aboutTable.innerHTML = '';
            const rows = [
                ['PingDrop Version', about.pdVersion], ['.NET Version', about.dotnetVersion],
                ['Process ID', about.processId], ['Machine Name', about.machineName],
                ['Start Time', about.startedAt], ['OS', about.os]
            ];
            const tbody = document.createElement('tbody');
            tbody.innerHTML = rows.map(([key, value]) => `<tr><td><strong>${key}</strong></td><td>${value}</td></tr>`).join('');
            this.$aboutTable.appendChild(tbody);
        }
    }
    class PingDrop extends PingDropCommon {
        constructor() {
            super();
            new PingDropTheme().Initialize();
            this.sse = new PingDropSSE({ logger: console });
            this.$sideBar = document.getElementById("sidebar");
            this.$sideBarOverlay = document.getElementById("sidebarOverlay");
            this.$hamburgerBtn = document.getElementById("hamburgerBtn");
            this.$navLinks = document.querySelectorAll('.sidebar-nav li a[data-section]');
            this.$connectionTableStatusFilter = document.getElementById('connStatusFilter');
            this.$connectionTableSearchInput = document.getElementById('connSearchInput');

            this.$noitificationPanelTargetType = document.getElementById('notifyTargetType');
            this.$notificationPanelTargetLabel = document.getElementById('notifyTargetLabel');
            this.$notificationPanelTargetInput = document.getElementById('notifyTargetId');
            this.$notificationPanelTargetContainer = document.getElementById('notifyTargetContainer');
            this.$notificationPanelPreview = document.getElementById("notifyPreview");
            this.$notificationPanelPreviewContent = document.getElementById("notifyPreviewContent");
            this.$notificationPanelEventName = document.getElementById("notifyEventName");
            this.$notificationPanelPayload = document.getElementById("notifyPayload");
            this.$sendNotificationBtn = document.getElementById("sendNotificationBtn");

            this.$api = new PingDropService();
            this.$dashboard = new PingDropDashboard();
            this.Initialize();
        }
        Initialize() {
            this.events();
            this.sse.connect(`${window.pingdrop.baseUrl}/sse`);
            this.$api.getMetrics();
            this.$api.getConnections('All');
            this.$api.getAbout();
            const initialLogs = [
                { level: 'info', msg: 'Listening on /sse endpoint' },
                { level: 'info', msg: 'Notification service healthy' },
            ];
            initialLogs.forEach(l => this.addLogLine(l.level, l.msg));
            this.rebuildLogBox();
        }
        openSideBar() {
            this.$sideBar.classList.add('open');
            this.$sideBarOverlay.classList.add('show');
            return this;
        }
        closeSideBar() {
            this.$sideBar.classList.remove('open');
            this.$sideBarOverlay.classList.remove('show');
            return this;
        }
        updateNotificationTargetUi() {
            switch (this.$noitificationPanelTargetType.value) {
                case 'key':
                    this.$notificationPanelTargetContainer.style.display = '';
                    this.$notificationPanelTargetLabel.textContent = 'Target Key';
                    this.$notificationPanelTargetInput.placeholder = 'e.g. user-101';
                    break;

                case 'connection':
                    this.$notificationPanelTargetContainer.style.display = '';
                    this.$notificationPanelTargetLabel.textContent = 'Connection ID';
                    this.$notificationPanelTargetInput.placeholder = 'e.g. 101'
                    break;

                case 'broadcast':
                    this.$notificationPanelTargetContainer.style.display = 'none';
                    break;
            }
        }
        getNotificationPreviewObj() {
            const targetType = this.$noitificationPanelTargetType?.value || 'user';
            const targetId = this.$noitificationPanelTargetType?.value === 'broadcast' ? 'all' : (this.$notificationPanelTargetInput?.value ||
                '...');
            const eventName = this.$notificationPanelEventName?.value || 'notification.test';
            let payload = {};
            try {
                payload = JSON.parse(notifyPayload?.value || '{}');
            } catch (e) {
                payload = { error: 'Invalid JSON' };
            }
            const preview = {
                targetType,
                targetId,
                eventName,
                payload,
                timestamp: new Date().toISOString(),
            };
            return preview;
        }
        updateNotifyPreview() {
            this.$notificationPanelPreviewContent.textContent = JSON.stringify(this.getNotificationPreviewObj(), null, 2);
            this.$notificationPanelPreview.style.display = 'block';
        }

        events() {
            this.sseEventListeners();
            this.pingDropEventListerners();
            this.$hamburgerBtn.addEventListener('click', () => {
                this.$sideBar.classList.contains('open') ? this.closeSideBar() : this.openSideBar();
            });
            this.$sideBarOverlay.addEventListener('click', () => this.closeSideBar());
            this.$navLinks.forEach(link => {
                link.addEventListener('click', (e) => {
                    e.preventDefault();
                    const section = link.dataset.section;
                    this.switchSection(section);
                    this.closeSideBar();
                });
            });
            this.$connectionTableStatusFilter?.addEventListener('change', (e) => {
                const searchTxt = this.$connectionTableSearchInput.value.trim();
                this.$api.getConnections(e.currentTarget.value, searchTxt);
            });
            this.$connectionTableSearchInput?.addEventListener('input', (e) => {
                const status = this.$connectionTableStatusFilter.value;
                this.$api.getConnections(status, e.currentTarget.value.trim());
            });
            this.$noitificationPanelTargetType.addEventListener('change', () => {
                this.updateNotificationTargetUi();
                this.updateNotifyPreview();
            });
            this.updateNotificationTargetUi();

            this.$notificationPanelTargetInput?.addEventListener('input', () => this.updateNotifyPreview());
            this.$notificationPanelEventName?.addEventListener('input', () => this.updateNotifyPreview());
            this.$notificationPanelPayload?.addEventListener('input', () => this.updateNotifyPreview());

            this.$sendNotificationBtn?.addEventListener('click', async () => {
                const targetType = this.$noitificationPanelTargetType.value;
                const targetId = targetType === 'broadcast' ? 'all' : this.$notificationPanelTargetInput?.value;
                const eventName = this.$notificationPanelEventName.value;
                if (!targetId && targetType !== 'broadcast') {
                    this.showToast('Please enter a target ID', 'warning');
                    return;
                }
                if (!eventName) {
                    this.showToast('Please enter an event name', 'warning');
                    return;
                }
                let payload;
                try {
                    payload = JSON.parse(this.$notificationPanelPayload.value);
                } catch (e) {
                    this.showToast('Invalid JSON payload', 'error');
                    return;
                }
                var result = await this.$api.sendNotification(this.getNotificationPreviewObj());
                if (result.isSuccess) {
                    this.showToast(`Notification sent to ${targetType === 'broadcast' ? 'all' : targetId} · Event: ${eventName}`, 'success');
                    this.addLogLine('info', `Test notification sent to ${targetType === 'broadcast' ? 'all connections' : targetId} · ${eventName}`);
                } else {
                    this.showToast('Failed to send notification', 'error');
                    this.addLogLine('error', `Failed to send test notification: ${result.error || 'Unknown error'}`);
                }
            });
        }
        switchSection(section) {
            document.querySelectorAll('.dashboard-section').forEach(s => s.classList.remove('active'));
            const target = document.getElementById('section-' + section);
            if (target) target.classList.add('active');
            this.$navLinks.forEach(l => l.classList.remove('active'));
            const activeLink = document.querySelector(`.sidebar-nav li a[data-section="${section}"]`);
            if (activeLink) activeLink.classList.add('active');
            this.activeSection = section;
            activeSection = section;
            if (section === 'logs') this.scrollLogToBottom();
            if (section === 'connections') this.$api.getConnections('All');
            if (section === 'about') this.$api.getAbout();
        }
        pingDropEventListerners() {
            document.addEventListener('metricsUpdated', (e) => { if (e.detail.isSuccess) this.$dashboard.renderMetrics(e.detail.data); });
            document.addEventListener('connectionsUpdated', (e) => { if (e.detail.isSuccess) this.$dashboard.renderConnectionTable(e.detail.data); });
            document.addEventListener('aboutUpdated', (e) => { if (e.detail.isSuccess) this.$dashboard.renderAboutTable(e.detail.data); });
        }
        sseEventListeners() {
            this.sse.on('message', (data) => { console.log('New message:', data); });
            this.sse.on("metrics", (data) => {
                var parsedData = null;
                try {
                    debugger;
                    parsedData = typeof data === 'string' ? JSON.parse(data) : data;
                    if (parsedData.isSuccess) this.$dashboard.renderMetrics(parsedData.data);
                } catch (e) {
                    console.warn('Failed to parse metrics SSE data:', data, e);
                }
            });
            this.sse.on('log', (data) => { this.addLogLine(data.level, data.msg); this.rebuildLogBox(); });
        }
    }
    new PingDrop();
})();