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
                            // Convert ArrayBuffer to regular array for C# interop
                            const bytes = new Uint8Array(event.data);
                            messageQueue.push(Array.from(bytes));
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
                socket.send(new Uint8Array(data).buffer);
                return true;
            } catch (err) {
                console.error('WebSocket send error:', err);
                return false;
            }
        },

        // Returns array of received messages (each is a byte array), or empty array
        getMessages() {
            const msgs = messageQueue;
            messageQueue = [];
            return msgs;
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
        }
    };
})();
