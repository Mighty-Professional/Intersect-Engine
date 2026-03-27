// Web Audio API Interop for Intersect Engine
window.IntersectAudio = (() => {
    let audioCtx = null;
    let masterGain = null;

    const audioBuffers = new Map();
    let nextBufferId = 1;

    const activeInstances = new Map();
    let nextInstanceId = 1;

    let musicElement = null;
    let musicGain = null;

    return {
        init() {
            audioCtx = new (window.AudioContext || window.webkitAudioContext)();
            masterGain = audioCtx.createGain();
            masterGain.connect(audioCtx.destination);

            musicGain = audioCtx.createGain();
            musicGain.connect(masterGain);
            return true;
        },

        resume() {
            if (audioCtx && audioCtx.state === 'suspended') {
                audioCtx.resume();
            }
        },

        async loadSound(url) {
            try {
                const response = await fetch(url);
                const arrayBuffer = await response.arrayBuffer();
                const audioBuffer = await audioCtx.decodeAudioData(arrayBuffer);
                const id = nextBufferId++;
                audioBuffers.set(id, audioBuffer);
                return id;
            } catch (e) {
                console.warn('Failed to load sound:', url, e);
                return -1;
            }
        },

        playSound(bufferId, volume, loop) {
            const buffer = audioBuffers.get(bufferId);
            if (!buffer) return -1;

            const source = audioCtx.createBufferSource();
            source.buffer = buffer;
            source.loop = loop;

            const gain = audioCtx.createGain();
            gain.gain.value = volume;
            source.connect(gain);
            gain.connect(masterGain);

            const instanceId = nextInstanceId++;
            activeInstances.set(instanceId, { source, gain, type: 'sound' });

            source.onended = () => {
                activeInstances.delete(instanceId);
            };

            source.start(0);
            return instanceId;
        },

        stopSound(instanceId) {
            const inst = activeInstances.get(instanceId);
            if (inst && inst.type === 'sound') {
                try { inst.source.stop(); } catch (e) { /* already stopped */ }
                activeInstances.delete(instanceId);
            }
        },

        setSoundVolume(instanceId, volume) {
            const inst = activeInstances.get(instanceId);
            if (inst) {
                inst.gain.gain.value = volume;
            }
        },

        setSoundLoop(instanceId, loop) {
            const inst = activeInstances.get(instanceId);
            if (inst && inst.source) {
                inst.source.loop = loop;
            }
        },

        pauseSound(instanceId) {
            // Web Audio API doesn't support pause on BufferSource, handled in C#
        },

        playMusic(url, volume, loop, fadeInMs) {
            this.stopMusic(0);

            musicElement = new Audio();
            musicElement.crossOrigin = 'anonymous';
            musicElement.loop = loop;
            musicElement.src = url;

            const source = audioCtx.createMediaElementSource(musicElement);
            source.connect(musicGain);

            if (fadeInMs > 0) {
                musicGain.gain.value = 0;
                musicGain.gain.linearRampToValueAtTime(volume, audioCtx.currentTime + fadeInMs / 1000);
            } else {
                musicGain.gain.value = volume;
            }

            musicElement.play().catch(e => console.warn('Music play failed:', e));
        },

        stopMusic(fadeOutMs) {
            if (!musicElement) return;
            if (fadeOutMs > 0) {
                musicGain.gain.linearRampToValueAtTime(0, audioCtx.currentTime + fadeOutMs / 1000);
                setTimeout(() => {
                    if (musicElement) {
                        musicElement.pause();
                        musicElement = null;
                    }
                }, fadeOutMs);
            } else {
                musicElement.pause();
                musicElement = null;
            }
        },

        setMusicVolume(volume) {
            if (musicGain) musicGain.gain.value = volume;
        },

        setMasterVolume(volume) {
            if (masterGain) masterGain.gain.value = volume;
        },

        unloadSound(bufferId) {
            audioBuffers.delete(bufferId);
        },

        getState(instanceId) {
            // 0=Stopped, 1=Playing, 2=Paused, 3=Disposed
            const inst = activeInstances.get(instanceId);
            if (!inst) return 0;
            return 1;
        }
    };
})();
