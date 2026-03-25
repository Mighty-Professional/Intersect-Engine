// WebSocket networking interop for Intersect Engine Web Client
window.IntersectWebSocket = (() => {
    let socket = null;
    let connected = false;
    let messageQueue = [];
    let pingMs = 0;
    let lastPingSent = 0;

    return {
        connect(url) {
            return new Promise((resolve, reject) => {
                try {
                    socket = new WebSocket(url);
                    socket.binaryType = 'arraybuffer';

                    socket.onopen = () => {
                        connected = true;
                        console.log('WebSocket connected to:', url);
                        resolve(true);
                    };

                    socket.onmessage = (event) => {
                        if (event.data instanceof ArrayBuffer) {
                            // Encode as base64 for Blazor JSON interop (byte[] must be base64)
                            const bytes = new Uint8Array(event.data);
                            let binary = '';
                            for (let i = 0; i < bytes.length; i++) {
                                binary += String.fromCharCode(bytes[i]);
                            }
                            messageQueue.push(btoa(binary));
                        }
                    };

                    socket.onclose = (event) => {
                        connected = false;
                        console.log('WebSocket closed:', event.code, event.reason);
                        // Push a null to signal disconnection
                        messageQueue.push(null);
                    };

                    socket.onerror = (event) => {
                        console.error('WebSocket error:', event);
                        if (!connected) {
                            reject(new Error('WebSocket connection failed'));
                        }
                    };
                } catch (err) {
                    reject(err);
                }
            });
        },

        isConnected() {
            return connected && socket !== null && socket.readyState === WebSocket.OPEN;
        },

        send(data) {
            if (!connected || !socket || socket.readyState !== WebSocket.OPEN) return false;
            try {
                // data comes as base64 string from Blazor JSON interop
                let bytes;
                if (typeof data === 'string') {
                    const binary = atob(data);
                    bytes = new Uint8Array(binary.length);
                    for (let i = 0; i < binary.length; i++) {
                        bytes[i] = binary.charCodeAt(i);
                    }
                } else {
                    bytes = new Uint8Array(data);
                }
                socket.send(bytes.buffer);
                return true;
            } catch (err) {
                console.error('WebSocket send error:', err);
                return false;
            }
        },

        // Returns the next message as a base64 string, empty string if disconnected, or null if no messages
        getNextMessage() {
            if (messageQueue.length === 0) return null;
            const msg = messageQueue.shift();
            if (msg === null) return ''; // empty string signals disconnection
            return msg;
        },

        // Returns number of queued messages
        getQueueLength() {
            return messageQueue.length;
        },

        disconnect() {
            connected = false;
            if (socket) {
                try { socket.close(1000, 'Client disconnect'); } catch (_) { }
                socket = null;
            }
        },

        getPing() {
            return pingMs;
        },

        sendPing() {
            lastPingSent = performance.now();
        },

        receivedPong() {
            if (lastPingSent > 0) {
                pingMs = Math.round(performance.now() - lastPingSent);
                lastPingSent = 0;
            }
        },

        // Check if server is reachable via HTTP API
        async checkServerStatus(apiUrl) {
            try {
                const resp = await fetch(apiUrl, { mode: 'cors', signal: AbortSignal.timeout(3000) });
                return resp.ok;
            } catch {
                return false;
            }
        }
    };
})();
