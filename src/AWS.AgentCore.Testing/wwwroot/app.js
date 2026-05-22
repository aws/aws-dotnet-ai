// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

(function () {
    'use strict';

    // =========================================================================
    // State
    // =========================================================================

    const state = {
        sessions: [],
        activeSessionId: null,
        payloadTemplate: '{\n  "prompt": "{{input}}"\n}',
        parameters: [],
        parametersExpanded: true,
        theme: 'dark',
        isLoading: false,
        useStreaming: false,
        showPayloadPanel: false,
        sidebarCollapsed: false,
        abortController: null
    };

    // =========================================================================
    // Built-in Templates
    // =========================================================================

    const builtInTemplates = [
        {
            name: 'Simple Prompt',
            description: 'Single prompt field',
            template: '{\n  "prompt": "{{input}}"\n}',
            parameters: []
        },
        {
            name: 'Message + Role',
            description: 'Chat-style with role specification',
            template: '{\n  "message": "{{input}}",\n  "role": "{{role}}"\n}',
            parameters: [{ name: 'role', defaultValue: 'user', type: 'string' }]
        },
        {
            name: 'Query + Parameters',
            description: 'With model tuning parameters',
            template: '{\n  "query": "{{input}}",\n  "parameters": {\n    "temperature": {{temperature}},\n    "maxTokens": {{maxTokens}}\n  }\n}',
            parameters: [
                { name: 'temperature', defaultValue: '0.7', type: 'number' },
                { name: 'maxTokens', defaultValue: '1024', type: 'number' }
            ]
        },
        {
            name: 'Structured Input',
            description: 'Nested input with metadata',
            template: '{\n  "input": {\n    "text": "{{input}}",\n    "type": "{{messageType}}"\n  },\n  "metadata": {\n    "source": "{{source}}",\n    "userId": "{{userId}}"\n  }\n}',
            parameters: [
                { name: 'messageType', defaultValue: 'user_message', type: 'string' },
                { name: 'source', defaultValue: 'testing-ui', type: 'string' },
                { name: 'userId', defaultValue: 'dev-user-1', type: 'string' }
            ]
        },
        {
            name: 'RAG Query',
            description: 'Retrieval-augmented generation',
            template: '{\n  "question": "{{input}}",\n  "context": {\n    "knowledgeBaseId": "{{knowledgeBaseId}}",\n    "maxResults": {{maxResults}},\n    "filters": {{filters}}\n  }\n}',
            parameters: [
                { name: 'knowledgeBaseId', defaultValue: 'kb-default', type: 'string' },
                { name: 'maxResults', defaultValue: '5', type: 'number' },
                { name: 'filters', defaultValue: '{}', type: 'raw' }
            ]
        },
        {
            name: 'Multi-turn Chat',
            description: 'Conversational with history flag',
            template: '{\n  "message": "{{input}}",\n  "includeHistory": {{includeHistory}},\n  "systemPrompt": "{{systemPrompt}}"\n}',
            parameters: [
                { name: 'includeHistory', defaultValue: 'true', type: 'boolean' },
                { name: 'systemPrompt', defaultValue: 'You are a helpful assistant.', type: 'string' }
            ]
        }
    ];

    // =========================================================================
    // API Client
    // =========================================================================

    const api = {
        async invoke(payload, sessionId, userInput) {
            const res = await fetch('/api/invoke', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ payload, sessionId, userInput })
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
            return res.json();
        },

        async invokeStream(payload, sessionId, userInput, signal, onChunk) {
            const res = await fetch('/api/invoke-stream', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ payload, sessionId, userInput }),
                signal
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);

            const sessionIdHeader = res.headers.get('X-Session-Id');
            const reader = res.body.getReader();
            const decoder = new TextDecoder();
            let buffer = '';

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                buffer += decoder.decode(value, { stream: true });
                const lines = buffer.split('\n');
                buffer = lines.pop() || '';

                for (const line of lines) {
                    if (!line.startsWith('data: ')) continue;
                    const jsonStr = line.slice(6);
                    try {
                        const data = JSON.parse(jsonStr);
                        if (data.done) return sessionIdHeader;
                        if (data.chunk) onChunk(data.chunk);
                    } catch (e) { /* skip malformed */ }
                }
            }
            return sessionIdHeader;
        },

        async getSessions() {
            const res = await fetch('/api/sessions');
            if (!res.ok) return [];
            return res.json();
        },

        async createSession() {
            const res = await fetch('/api/sessions', { method: 'POST' });
            if (!res.ok) throw new Error('Failed to create session');
            return res.json();
        },

        async deleteSession(id) {
            await fetch(`/api/sessions/${id}`, { method: 'DELETE' });
        },

        async getMessages(sessionId) {
            const res = await fetch(`/api/sessions/${sessionId}/messages`);
            if (!res.ok) return [];
            return res.json();
        },

        async getConfig() {
            const res = await fetch('/api/config');
            if (!res.ok) return {};
            return res.json();
        },

        async loadPayloadConfig() {
            const res = await fetch('/api/payload-config');
            if (res.status === 204 || !res.ok) return null;
            return res.json();
        },

        async savePayloadConfig(config) {
            await fetch('/api/payload-config', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(config)
            });
        },

        async resetPayloadConfig() {
            await fetch('/api/payload-config', { method: 'DELETE' });
        }
    };

    // =========================================================================
    // DOM References
    // =========================================================================

    const $ = (sel) => document.querySelector(sel);
    const $$ = (sel) => document.querySelectorAll(sel);

    // =========================================================================
    // Theme Management
    // =========================================================================

    function initTheme() {
        const saved = getCookie('agentcore-theme');
        if (saved) {
            state.theme = saved;
        }
        applyTheme();
    }

    function toggleTheme() {
        state.theme = state.theme === 'dark' ? 'light' : 'dark';
        applyTheme();
        setCookie('agentcore-theme', state.theme, 365);
    }

    function applyTheme() {
        document.documentElement.setAttribute('data-theme', state.theme);
        const isDark = state.theme === 'dark';
        const logoSrc = isDark ? 'aws-light.svg' : 'aws.svg';

        const headerLogo = $('#header-logo');
        const welcomeLogo = $('#welcome-logo');
        if (headerLogo) headerLogo.src = logoSrc;
        if (welcomeLogo) welcomeLogo.src = logoSrc;

        const sunIcon = $('#theme-icon-sun');
        const moonIcon = $('#theme-icon-moon');
        if (sunIcon) sunIcon.classList.toggle('hidden', !isDark);
        if (moonIcon) moonIcon.classList.toggle('hidden', isDark);
    }

    // =========================================================================
    // Cookie Utilities
    // =========================================================================

    function getCookie(name) {
        const match = document.cookie.match(new RegExp('(^| )' + name + '=([^;]+)'));
        return match ? match[2] : null;
    }

    function setCookie(name, value, days) {
        const maxAge = days * 24 * 60 * 60;
        document.cookie = `${name}=${value}; path=/; max-age=${maxAge}; SameSite=Lax`;
    }

    // =========================================================================
    // Session Management
    // =========================================================================

    async function loadSessions() {
        state.sessions = await api.getSessions();
        renderSessions();
    }

    async function createNewSession() {
        const session = await api.createSession();
        state.activeSessionId = session.id;
        state.sessions.unshift({ id: session.id, title: session.title, lastMessageAt: new Date().toISOString() });
        renderSessions();
        renderMessages([]);
        showWelcome();
        focusInput();
    }

    async function switchSession(sessionId) {
        state.activeSessionId = sessionId;
        renderSessions();
        const messages = await api.getMessages(sessionId);
        renderMessages(messages);
        if (messages.length === 0) {
            showWelcome();
        } else {
            hideWelcome();
        }
        updateSessionTitleBadge();
        focusInput();
    }

    async function deleteSession(sessionId) {
        await api.deleteSession(sessionId);
        state.sessions = state.sessions.filter(s => s.id !== sessionId);
        if (state.activeSessionId === sessionId) {
            state.activeSessionId = state.sessions.length > 0 ? state.sessions[0].id : null;
            if (state.activeSessionId) {
                await switchSession(state.activeSessionId);
            } else {
                renderMessages([]);
                showWelcome();
            }
        }
        renderSessions();
    }

    // =========================================================================
    // Rendering - Sessions
    // =========================================================================

    function renderSessions() {
        const container = $('#sessions-list');
        if (!container) return;

        container.innerHTML = state.sessions.map(session => `
            <div class="session-item ${session.id === state.activeSessionId ? 'active' : ''}" data-session-id="${session.id}">
                <div class="session-item-content">
                    <span class="session-title">${escapeHtml(session.title)}</span>
                </div>
                <button class="session-delete-btn" data-delete-session="${session.id}" title="Delete session">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                    </svg>
                </button>
            </div>
        `).join('');

        // Attach click handlers
        container.querySelectorAll('.session-item').forEach(el => {
            el.addEventListener('click', (e) => {
                if (e.target.closest('.session-delete-btn')) return;
                switchSession(el.dataset.sessionId);
            });
        });

        container.querySelectorAll('.session-delete-btn').forEach(el => {
            el.addEventListener('click', (e) => {
                e.stopPropagation();
                deleteSession(el.dataset.deleteSession);
            });
        });
    }

    // =========================================================================
    // Rendering - Messages
    // =========================================================================

    function renderMessages(messages) {
        const container = $('#messages-inner');
        if (!container) return;

        container.innerHTML = messages.map(msg => createMessageHtml(msg)).join('');
        scrollToBottom();
    }

    function createMessageHtml(msg) {
        const isUser = msg.role === 'user';
        const isEmpty = !msg.content;
        const showThinking = !isUser && isEmpty && state.isLoading;
        const contentHtml = showThinking ? getThinkingHtml() : (isUser ? escapeHtml(msg.content) : renderMarkdown(msg.content));
        const timeStr = msg.timestamp ? new Date(msg.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';

        return `
            <div class="message-row ${isUser ? 'user' : 'assistant'}">
                <div class="message-wrapper">
                    <div class="message-avatar">
                        ${isUser ? `
                            <div class="avatar user-avatar">
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
                                    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                                    <circle cx="12" cy="7" r="4"/>
                                </svg>
                            </div>
                        ` : `
                            <div class="avatar assistant-avatar">
                                <img src="${state.theme === 'dark' ? 'aws-light.svg' : 'aws.svg'}" width="16" height="16" alt="AWS" />
                            </div>
                        `}
                    </div>
                    <div class="message-content">
                        <div class="message-meta">
                            <span class="message-role">${isUser ? 'You' : 'Agent'}</span>
                            <span class="message-time">${timeStr}</span>
                        </div>
                        <div class="message-text ${isUser ? '' : 'markdown-content'}">
                            ${contentHtml}
                        </div>
                        ${!isUser && msg.content ? `
                            <div class="message-actions">
                                <button class="msg-action-btn copy-btn" data-copy="${escapeAttr(msg.content)}" title="Copy response">
                                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                        <rect x="9" y="9" width="13" height="13" rx="2" ry="2"/>
                                        <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>
                                    </svg>
                                </button>
                            </div>
                        ` : ''}
                    </div>
                </div>
            </div>
        `;
    }

    function appendMessage(msg) {
        const container = $('#messages-inner');
        if (!container) return;
        container.insertAdjacentHTML('beforeend', createMessageHtml(msg));
        scrollToBottom();
    }

    function updateLastAssistantMessage(content) {
        const container = $('#messages-inner');
        if (!container) return;

        const lastAssistant = container.querySelector('.message-row.assistant:last-child .message-text');
        if (lastAssistant) {
            lastAssistant.innerHTML = content ? renderMarkdown(content) : getThinkingHtml();
        }

        // Update copy button
        const lastActions = container.querySelector('.message-row.assistant:last-child .message-actions');
        if (lastActions && content) {
            lastActions.innerHTML = `
                <button class="action-btn copy-btn" data-copy="${escapeAttr(content)}" title="Copy response">
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <rect x="9" y="9" width="13" height="13" rx="2" ry="2"/>
                        <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>
                    </svg>
                </button>
            `;
        }

        scrollToBottom();
    }

    function getThinkingHtml() {
        return `
            <div class="thinking-indicator">
                <div class="thinking-dot"></div>
                <div class="thinking-dot"></div>
                <div class="thinking-dot"></div>
            </div>
        `;
    }

    // =========================================================================
    // Rendering - Parameters
    // =========================================================================

    function renderParameters() {
        const paramInputs = $('#param-inputs');
        if (!paramInputs) return;

        if (state.parameters.length === 0) {
            paramInputs.classList.add('hidden');
            paramInputs.innerHTML = '';
            return;
        }

        paramInputs.classList.remove('hidden');
        paramInputs.innerHTML = state.parameters.map(param => `
            <div class="param-input-group">
                <label class="param-input-label" for="param-${param.name}">${escapeHtml(param.name)}</label>
                <input type="text"
                       id="param-${param.name}"
                       class="param-input-field"
                       placeholder="${param.defaultValue ? escapeAttr(param.defaultValue) : 'Enter ' + param.name + '...'}"
                       value="${escapeAttr(param.currentValue || '')}"
                       data-param-name="${escapeAttr(param.name)}" />
            </div>
        `).join('');

        paramInputs.querySelectorAll('.param-input-field').forEach(input => {
            input.addEventListener('input', (e) => {
                const name = e.target.dataset.paramName;
                const param = state.parameters.find(p => p.name === name);
                if (param) {
                    param.currentValue = e.target.value;
                    renderPayloadPreview();
                }
            });
        });
    }

    function renderParametersPanel() {
        const paramsBody = $('#params-body');
        const paramsCount = $('#params-count');
        const paramsChevron = $('#params-chevron');
        const paramsList = $('#params-list');
        const paramsEmpty = $('#params-empty');

        if (paramsCount) {
            if (state.parameters.length > 0) {
                paramsCount.textContent = state.parameters.length;
                paramsCount.classList.remove('hidden');
            } else {
                paramsCount.classList.add('hidden');
            }
        }

        if (paramsChevron) {
            paramsChevron.classList.toggle('expanded', state.parametersExpanded);
        }

        if (paramsBody) {
            paramsBody.classList.toggle('hidden', !state.parametersExpanded);
        }

        if (!paramsList) return;

        if (state.parameters.length === 0) {
            if (paramsEmpty) paramsEmpty.classList.remove('hidden');
            paramsList.querySelectorAll('.param-card').forEach(el => el.remove());
            return;
        }

        if (paramsEmpty) paramsEmpty.classList.add('hidden');

        // Remove old param cards
        paramsList.querySelectorAll('.param-card').forEach(el => el.remove());

        state.parameters.forEach(param => {
            const card = document.createElement('div');
            card.className = `param-card ${param.isExpanded ? 'expanded' : ''}`;
            card.innerHTML = `
                <div class="param-card-header">
                    <svg class="collapse-chevron ${param.isExpanded ? 'expanded' : ''}" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                        <polyline points="9 18 15 12 9 6"/>
                    </svg>
                    <div class="param-card-name">
                        <code>{{${escapeHtml(param.name)}}}</code>
                    </div>
                    <span class="param-type-badge">${escapeHtml(param.type)}</span>
                    <button class="param-delete-btn" title="Remove parameter">
                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                        </svg>
                    </button>
                </div>
                ${param.isExpanded ? `
                    <div class="param-card-fields">
                        <div class="param-field">
                            <label>Name</label>
                            <input type="text" value="${escapeAttr(param.name)}" class="param-name-input" placeholder="paramName" spellcheck="false" />
                        </div>
                        <div class="param-field">
                            <label>Default value</label>
                            <input type="text" value="${escapeAttr(param.defaultValue)}" class="param-default-input" placeholder="Optional default" />
                        </div>
                        <div class="param-field">
                            <label>Type</label>
                            <select class="param-type-select">
                                <option value="string" ${param.type === 'string' ? 'selected' : ''}>String</option>
                                <option value="number" ${param.type === 'number' ? 'selected' : ''}>Number</option>
                                <option value="boolean" ${param.type === 'boolean' ? 'selected' : ''}>Boolean</option>
                                <option value="raw" ${param.type === 'raw' ? 'selected' : ''}>Raw JSON</option>
                            </select>
                        </div>
                    </div>
                ` : ''}
            `;

            // Event: toggle expand
            card.querySelector('.param-card-header').addEventListener('click', (e) => {
                if (e.target.closest('.param-delete-btn')) return;
                param.isExpanded = !param.isExpanded;
                renderParametersPanel();
            });

            // Event: delete
            card.querySelector('.param-delete-btn').addEventListener('click', (e) => {
                e.stopPropagation();
                state.parameters = state.parameters.filter(p => p.name !== param.name);
                renderParametersPanel();
                renderParameters();
                renderPayloadPreview();
            });

            if (param.isExpanded) {
                // Event: rename
                const nameInput = card.querySelector('.param-name-input');
                if (nameInput) {
                    nameInput.addEventListener('change', (e) => {
                        const newName = e.target.value.trim().replace(/[^a-zA-Z0-9_]/g, '');
                        if (newName && !state.parameters.some(p => p.name === newName && p !== param)) {
                            param.name = newName;
                            renderParametersPanel();
                            renderParameters();
                            renderPayloadPreview();
                        }
                    });
                }

                // Event: default value
                const defaultInput = card.querySelector('.param-default-input');
                if (defaultInput) {
                    defaultInput.addEventListener('input', (e) => {
                        param.defaultValue = e.target.value;
                        renderPayloadPreview();
                    });
                }

                // Event: type change
                const typeSelect = card.querySelector('.param-type-select');
                if (typeSelect) {
                    typeSelect.addEventListener('change', (e) => {
                        param.type = e.target.value;
                        renderParametersPanel();
                        renderPayloadPreview();
                    });
                }
            }

            paramsList.appendChild(card);
        });
    }

    // =========================================================================
    // Rendering - Payload Preview
    // =========================================================================

    function renderPayloadPreview() {
        const preview = $('#live-preview');
        const welcomePreview = $('#welcome-payload-code');
        const welcomeParams = $('#welcome-params');

        const chatInput = $('#chat-input');
        const userInput = chatInput ? chatInput.value : '';
        const previewText = getPayloadPreview(userInput);

        if (preview) preview.textContent = previewText;
        if (welcomePreview) welcomePreview.textContent = state.payloadTemplate;

        if (welcomeParams) {
            welcomeParams.innerHTML = state.parameters.map(p =>
                `<span class="param-chip">${escapeHtml(p.name)}</span>`
            ).join('');
        }

        // Auto-save payload config (debounced)
        scheduleConfigSave();
    }

    let configSaveTimeout = null;
    function scheduleConfigSave() {
        if (configSaveTimeout) clearTimeout(configSaveTimeout);
        configSaveTimeout = setTimeout(() => {
            api.savePayloadConfig({
                template: state.payloadTemplate,
                parameters: state.parameters
            }).catch(() => {});
        }, 1000);
    }

    async function resetPayloadConfig() {
        await api.resetPayloadConfig();
        state.payloadTemplate = '{\n  "prompt": "{{input}}"\n}';
        state.parameters = [];
        const editor = $('#payload-editor');
        if (editor) editor.value = state.payloadTemplate;
        initCodeEditor();
        renderParameters();
        renderParametersPanel();
        renderPayloadPreview();
    }

    function getPayloadPreview(userInput) {
        let payload = state.payloadTemplate;
        payload = replaceParameter(payload, 'input', userInput || 'Hello, agent!', 'string');

        for (const param of state.parameters) {
            const value = param.currentValue || param.defaultValue || `<${param.name}>`;
            payload = replaceParameter(payload, param.name, value, param.type);
        }

        try {
            const parsed = JSON.parse(payload);
            return JSON.stringify(parsed, null, 2);
        } catch {
            return payload;
        }
    }

    // =========================================================================
    // Payload Building
    // =========================================================================

    function buildPayload(userInput) {
        let payload = state.payloadTemplate;
        payload = replaceParameter(payload, 'input', userInput, 'string');

        for (const param of state.parameters) {
            const value = param.currentValue || param.defaultValue;
            payload = replaceParameter(payload, param.name, value, param.type);
        }

        try {
            JSON.parse(payload);
            hideEditorError();
            return payload;
        } catch (e) {
            showEditorError(`Invalid JSON: ${e.message}`);
            return JSON.stringify({ prompt: userInput });
        }
    }

    function replaceParameter(template, name, value, type) {
        const placeholder = `{{${name}}}`;
        switch (type) {
            case 'number':
                return template.split(placeholder).join(value || '0');
            case 'boolean':
                return template.split(placeholder).join((value || 'false').toLowerCase());
            case 'raw':
                return template.split(placeholder).join(value || 'null');
            default: // string
                return template.split(placeholder).join(escapeJsonString(value || ''));
        }
    }

    function escapeJsonString(input) {
        return input
            .replace(/\\/g, '\\\\')
            .replace(/"/g, '\\"')
            .replace(/\n/g, '\\n')
            .replace(/\r/g, '\\r')
            .replace(/\t/g, '\\t');
    }

    // =========================================================================
    // Format JSON
    // =========================================================================

    function formatJson() {
        const editor = $('#payload-editor');
        if (!editor) return;

        const template = editor.value;
        const formatted = formatJsonTemplate(template);
        if (formatted !== null) {
            editor.value = formatted;
            state.payloadTemplate = formatted;
            hideEditorError();
            updateEditorGutter();
            renderPayloadPreview();
        } else {
            showEditorError('Cannot format: template is not valid JSON structure.');
        }
    }

    function formatJsonTemplate(template) {
        const quotedPlaceholders = {};
        const unquotedPlaceholders = {};
        let i = 0;

        // Replace quoted "{{name}}" with placeholder strings
        let replaced = template.replace(/"(\{\{\w+\}\})"/g, (match) => {
            const token = `"__QPH${i}__"`;
            quotedPlaceholders[`"__QPH${i}__"`] = match;
            i++;
            return token;
        });

        // Replace bare {{name}} with placeholder numbers
        replaced = replaced.replace(/\{\{\w+\}\}/g, (match) => {
            const token = `9${String(i).padStart(6, '0')}`;
            unquotedPlaceholders[token] = match;
            i++;
            return token;
        });

        try {
            const parsed = JSON.parse(replaced);
            let formatted = JSON.stringify(parsed, null, 2);

            for (const [token, original] of Object.entries(quotedPlaceholders)) {
                formatted = formatted.split(token).join(original);
            }
            for (const [token, original] of Object.entries(unquotedPlaceholders)) {
                formatted = formatted.split(token).join(original);
            }

            return formatted;
        } catch {
            return null;
        }
    }

    // =========================================================================
    // Template Application
    // =========================================================================

    function applyTemplate(template) {
        state.payloadTemplate = template.template;
        state.parameters = (template.parameters || []).map(p => ({
            name: p.name,
            defaultValue: p.defaultValue,
            currentValue: p.defaultValue,
            type: p.type,
            isExpanded: false
        }));

        const editor = $('#payload-editor');
        if (editor) {
            editor.value = state.payloadTemplate;
            updateEditorGutter();
        }

        hideEditorError();
        renderParameters();
        renderParametersPanel();
        renderPayloadPreview();
    }

    function renderTemplateList() {
        const container = $('#template-list');
        if (!container) return;

        container.innerHTML = builtInTemplates.map((t, idx) => `
            <button class="template-btn" data-template-idx="${idx}">
                <span class="template-name">${escapeHtml(t.name)}</span>
                <span class="template-desc">${escapeHtml(t.description)}</span>
            </button>
        `).join('');

        container.querySelectorAll('.template-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const idx = parseInt(btn.dataset.templateIdx);
                applyTemplate(builtInTemplates[idx]);
            });
        });
    }

    // =========================================================================
    // Code Editor Logic
    // =========================================================================

    function initCodeEditor() {
        const editor = $('#payload-editor');
        if (!editor) return;

        editor.value = state.payloadTemplate;
        updateEditorGutter();

        editor.addEventListener('input', () => {
            state.payloadTemplate = editor.value;
            updateEditorGutter();
            renderPayloadPreview();
        });

        editor.addEventListener('keydown', (e) => {
            if (e.key === 'Tab') {
                e.preventDefault();
                const start = editor.selectionStart;
                const end = editor.selectionEnd;
                editor.value = editor.value.substring(0, start) + '  ' + editor.value.substring(end);
                editor.selectionStart = editor.selectionEnd = start + 2;
                state.payloadTemplate = editor.value;
                updateEditorGutter();
                renderPayloadPreview();
            } else if (e.key === 'Enter') {
                e.preventDefault();
                const start = editor.selectionStart;
                const beforeCursor = editor.value.substring(0, start);
                const currentLine = beforeCursor.split('\n').pop() || '';
                const indent = currentLine.match(/^\s*/)[0];
                const lastChar = beforeCursor.trim().slice(-1);
                let newIndent = indent;
                if (lastChar === '{' || lastChar === '[') {
                    newIndent = indent + '  ';
                }
                editor.value = editor.value.substring(0, start) + '\n' + newIndent + editor.value.substring(editor.selectionEnd);
                editor.selectionStart = editor.selectionEnd = start + 1 + newIndent.length;
                state.payloadTemplate = editor.value;
                updateEditorGutter();
                renderPayloadPreview();
            } else if (e.key === '{') {
                const start = editor.selectionStart;
                const end = editor.selectionEnd;
                if (start === end) {
                    e.preventDefault();
                    editor.value = editor.value.substring(0, start) + '{}' + editor.value.substring(end);
                    editor.selectionStart = editor.selectionEnd = start + 1;
                    state.payloadTemplate = editor.value;
                    updateEditorGutter();
                    renderPayloadPreview();
                }
            } else if (e.key === '[') {
                const start = editor.selectionStart;
                const end = editor.selectionEnd;
                if (start === end) {
                    e.preventDefault();
                    editor.value = editor.value.substring(0, start) + '[]' + editor.value.substring(end);
                    editor.selectionStart = editor.selectionEnd = start + 1;
                    state.payloadTemplate = editor.value;
                    updateEditorGutter();
                    renderPayloadPreview();
                }
            } else if (e.key === '"') {
                const start = editor.selectionStart;
                const end = editor.selectionEnd;
                if (start === end) {
                    e.preventDefault();
                    editor.value = editor.value.substring(0, start) + '""' + editor.value.substring(end);
                    editor.selectionStart = editor.selectionEnd = start + 1;
                    state.payloadTemplate = editor.value;
                    updateEditorGutter();
                    renderPayloadPreview();
                }
            }
        });

        editor.addEventListener('scroll', () => {
            const gutter = $('#editor-gutter');
            if (gutter) gutter.scrollTop = editor.scrollTop;
        });
    }

    function updateEditorGutter() {
        const editor = $('#payload-editor');
        const gutter = $('#editor-gutter');
        if (!editor || !gutter) return;

        const lineCount = Math.max(1, editor.value.split('\n').length);
        let html = '';
        for (let i = 1; i <= lineCount; i++) {
            html += `<div class="gutter-line">${i}</div>`;
        }
        gutter.innerHTML = html;
    }

    // =========================================================================
    // Message Sending
    // =========================================================================

    async function sendMessage(inputOverride) {
        const chatInput = $('#chat-input');
        const input = inputOverride || (chatInput ? chatInput.value.trim() : '');
        if (!input || state.isLoading) return;

        if (chatInput) chatInput.value = '';
        setLoading(true);
        hideWelcome();

        // Ensure we have a session
        if (!state.activeSessionId) {
            const session = await api.createSession();
            state.activeSessionId = session.id;
            state.sessions.unshift({ id: session.id, title: session.title, lastMessageAt: new Date().toISOString() });
            renderSessions();
        }

        const payload = buildPayload(input);

        // Append user message to UI
        appendMessage({ role: 'user', content: input, timestamp: new Date().toISOString() });

        // Append thinking assistant message
        appendMessage({ role: 'assistant', content: '', timestamp: new Date().toISOString() });

        try {
            state.abortController = new AbortController();

            if (state.useStreaming) {
                let fullContent = '';
                const returnedSessionId = await api.invokeStream(
                    payload,
                    state.activeSessionId,
                    input,
                    state.abortController.signal,
                    (chunk) => {
                        fullContent += chunk;
                        updateLastAssistantMessage(fullContent);
                    }
                );
                if (returnedSessionId) state.activeSessionId = returnedSessionId;
            } else {
                const result = await api.invoke(payload, state.activeSessionId, input);
                updateLastAssistantMessage(result.content);
                if (result.sessionId) state.activeSessionId = result.sessionId;
            }

            // Refresh sessions list to get updated titles
            await loadSessions();
            updateSessionTitleBadge();
        } catch (e) {
            if (e.name === 'AbortError') {
                updateLastAssistantMessage('*Request cancelled.*');
            } else {
                updateLastAssistantMessage(`**Error:** ${e.message}`);
            }
        } finally {
            setLoading(false);
            state.abortController = null;
            focusInput();
        }
    }

    function cancelRequest() {
        if (state.abortController) {
            state.abortController.abort();
        }
    }

    // =========================================================================
    // UI State Helpers
    // =========================================================================

    function setLoading(loading) {
        state.isLoading = loading;
        const sendBtn = $('#send-btn');
        const stopBtn = $('#stop-btn');
        const chatInput = $('#chat-input');
        const inputWrapper = $('#input-wrapper');

        if (sendBtn) sendBtn.classList.toggle('hidden', loading);
        if (stopBtn) stopBtn.classList.toggle('hidden', !loading);
        if (chatInput) chatInput.disabled = loading;
        if (inputWrapper) inputWrapper.classList.toggle('loading', loading);

        // Disable param inputs
        $$('.param-input-field').forEach(input => { input.disabled = loading; });
    }

    function showWelcome() {
        const welcome = $('#welcome-screen');
        const messages = $('#messages-container');
        if (welcome) welcome.classList.remove('hidden');
        if (messages) messages.classList.add('hidden');
    }

    function hideWelcome() {
        const welcome = $('#welcome-screen');
        const messages = $('#messages-container');
        if (welcome) welcome.classList.add('hidden');
        if (messages) messages.classList.remove('hidden');
    }

    function updateSessionTitleBadge() {
        const badge = $('#session-title-badge');
        if (!badge) return;

        const session = state.sessions.find(s => s.id === state.activeSessionId);
        if (session && session.title !== 'New Chat') {
            badge.textContent = session.title;
            badge.classList.remove('hidden');
        } else {
            badge.classList.add('hidden');
        }
    }

    function showEditorError(msg) {
        const el = $('#editor-error');
        if (el) {
            el.textContent = msg;
            el.classList.remove('hidden');
        }
    }

    function hideEditorError() {
        const el = $('#editor-error');
        if (el) el.classList.add('hidden');
    }

    function togglePayloadPanel() {
        state.showPayloadPanel = !state.showPayloadPanel;
        const panel = $('#payload-panel');
        const toggleBtn = $('#payload-toggle-btn');
        if (panel) panel.classList.toggle('hidden', !state.showPayloadPanel);
        if (toggleBtn) toggleBtn.classList.toggle('active', state.showPayloadPanel);
    }

    function toggleSidebar() {
        state.sidebarCollapsed = !state.sidebarCollapsed;
        const sidebar = $('#sidebar');
        const openBtn = $('#sidebar-open-btn');
        if (sidebar) sidebar.classList.toggle('collapsed', state.sidebarCollapsed);
        if (openBtn) openBtn.classList.toggle('hidden', !state.sidebarCollapsed);
    }

    function updateSendBtnState() {
        const chatInput = $('#chat-input');
        const sendBtn = $('#send-btn');
        if (!chatInput || !sendBtn) return;
        sendBtn.classList.toggle('disabled', !chatInput.value.trim());
    }

    function focusInput() {
        const chatInput = $('#chat-input');
        if (chatInput) chatInput.focus();
    }

    function scrollToBottom() {
        const container = $('#messages-container');
        if (container) {
            container.scrollTo({ top: container.scrollHeight, behavior: 'smooth' });
        }
    }

    // =========================================================================
    // Markdown Rendering
    // =========================================================================

    function renderMarkdown(content) {
        if (!content) return '';
        try {
            if (typeof marked !== 'undefined' && marked.parse) {
                const raw = marked.parse(content);
                if (typeof DOMPurify !== 'undefined' && DOMPurify.sanitize) {
                    return DOMPurify.sanitize(raw);
                }
            }
        } catch (e) { /* fallback */ }
        return escapeHtml(content).replace(/\n/g, '<br>');
    }

    // =========================================================================
    // Utilities
    // =========================================================================

    function escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function escapeAttr(text) {
        if (!text) return '';
        return text.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/'/g, '&#39;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    // =========================================================================
    // Event Bindings
    // =========================================================================

    function bindEvents() {
        // Theme toggle
        const themeBtn = $('#theme-toggle-btn');
        if (themeBtn) themeBtn.addEventListener('click', toggleTheme);

        // Sidebar
        const newSessionBtn = $('#new-session-btn');
        if (newSessionBtn) newSessionBtn.addEventListener('click', createNewSession);

        const sidebarToggleBtn = $('#sidebar-toggle-btn');
        if (sidebarToggleBtn) sidebarToggleBtn.addEventListener('click', toggleSidebar);

        const sidebarOpenBtn = $('#sidebar-open-btn');
        if (sidebarOpenBtn) sidebarOpenBtn.addEventListener('click', toggleSidebar);

        // Payload panel
        const payloadToggleBtn = $('#payload-toggle-btn');
        if (payloadToggleBtn) payloadToggleBtn.addEventListener('click', togglePayloadPanel);

        const payloadCloseBtn = $('#payload-close-btn');
        if (payloadCloseBtn) payloadCloseBtn.addEventListener('click', togglePayloadPanel);

        const openPayloadBtn = $('#open-payload-btn');
        if (openPayloadBtn) openPayloadBtn.addEventListener('click', togglePayloadPanel);

        // Format JSON
        const formatBtn = $('#format-json-btn');
        if (formatBtn) formatBtn.addEventListener('click', formatJson);

        const resetConfigBtn = $('#reset-config-btn');
        if (resetConfigBtn) resetConfigBtn.addEventListener('click', resetPayloadConfig);

        // Parameters section
        const paramsSectionHeader = $('#params-section-header');
        if (paramsSectionHeader) {
            paramsSectionHeader.addEventListener('click', (e) => {
                if (e.target.closest('#add-param-btn')) return;
                state.parametersExpanded = !state.parametersExpanded;
                renderParametersPanel();
            });
        }

        const addParamBtn = $('#add-param-btn');
        if (addParamBtn) {
            addParamBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                const name = `param${state.parameters.length + 1}`;
                state.parameters.push({
                    name,
                    defaultValue: '',
                    currentValue: '',
                    type: 'string',
                    isExpanded: true
                });
                renderParametersPanel();
                renderParameters();
                renderPayloadPreview();
            });
        }

        // Chat input
        const chatInput = $('#chat-input');
        if (chatInput) {
            chatInput.addEventListener('keydown', (e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    sendMessage();
                }
            });
            chatInput.addEventListener('input', () => {
                updateSendBtnState();
                renderPayloadPreview();
                // Auto-resize
                chatInput.style.height = 'auto';
                chatInput.style.height = Math.min(chatInput.scrollHeight, 200) + 'px';
            });
        }

        // Send/Stop buttons
        const sendBtn = $('#send-btn');
        if (sendBtn) sendBtn.addEventListener('click', () => sendMessage());

        const stopBtn = $('#stop-btn');
        if (stopBtn) stopBtn.addEventListener('click', cancelRequest);

        // Quick action: send default
        const sendDefaultBtn = $('#send-default-btn');
        if (sendDefaultBtn) {
            sendDefaultBtn.addEventListener('click', () => {
                sendMessage('Hello! Can you tell me what you can help me with?');
            });
        }

        // Copy buttons (delegated)
        document.addEventListener('click', (e) => {
            const copyBtn = e.target.closest('.copy-btn');
            if (copyBtn) {
                const text = copyBtn.dataset.copy;
                if (text) navigator.clipboard.writeText(text);
            }
        });

        // Keyboard shortcuts
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && state.showPayloadPanel) {
                togglePayloadPanel();
            }
        });

        // Streaming badge
        const streamingBadge = $('#streaming-badge');
        if (streamingBadge && state.useStreaming) {
            streamingBadge.classList.remove('hidden');
        }
    }

    // =========================================================================
    // Initialization
    // =========================================================================

    async function init() {
        initTheme();

        // Get server config
        try {
            const config = await api.getConfig();
            state.useStreaming = config.useStreaming || false;
            state.agentName = config.agentName || 'default';
            const streamingBadge = $('#streaming-badge');
            if (streamingBadge) streamingBadge.classList.toggle('hidden', !state.useStreaming);
        } catch (e) { /* use defaults */ }

        // Load persisted payload configuration for this agent
        try {
            const savedConfig = await api.loadPayloadConfig();
            if (savedConfig) {
                state.payloadTemplate = savedConfig.template || state.payloadTemplate;
                state.parameters = savedConfig.parameters || state.parameters;
            }
        } catch (e) { /* use defaults */ }

        // Load sessions
        await loadSessions();
        if (state.sessions.length > 0) {
            state.activeSessionId = state.sessions[0].id;
            const messages = await api.getMessages(state.activeSessionId);
            if (messages.length > 0) {
                renderMessages(messages);
                hideWelcome();
            }
        }

        bindEvents();
        initCodeEditor();
        renderTemplateList();
        renderParameters();
        renderParametersPanel();
        renderPayloadPreview();
        updateSessionTitleBadge();
        focusInput();
    }

    // Start when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
