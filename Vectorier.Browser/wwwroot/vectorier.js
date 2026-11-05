// Small helpers used by the browser build of Vectorier.
let currentAudio = null;
let currentDone = null;

// Plays a sound; the returned promise resolves when it ends or is stopped.
export function playSound(base64, mime, volume) {
    stopSound();
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    const url = URL.createObjectURL(new Blob([bytes], { type: mime }));
    const audio = new Audio(url);
    audio.volume = Math.max(0, Math.min(1, volume));
    currentAudio = audio;
    return new Promise(resolve => {
        const finish = () => {
            URL.revokeObjectURL(url);
            if (currentAudio === audio) { currentAudio = null; currentDone = null; }
            resolve();
        };
        currentDone = finish;
        audio.onended = finish;
        audio.onerror = finish;
        audio.play().catch(finish);
    });
}

export function setSoundVolume(volume) {
    if (currentAudio) currentAudio.volume = Math.max(0, Math.min(1, volume));
}

export function stopSound() {
    if (currentAudio) {
        currentAudio.pause();
        currentAudio = null;
    }
    if (currentDone) {
        const done = currentDone;
        currentDone = null;
        done();
    }
}

// ================= The game's sound (WebAudioOutput) ================= //
// Blocks of interleaved stereo float samples from the game's mixer, played one after the other.
let audioContext = null;
let audioNext = 0;

export function audioStart(sampleRate) {
    const Context = globalThis.AudioContext || globalThis.webkitAudioContext;
    if (!Context) return false;
    if (!audioContext) {
        audioContext = new Context({ sampleRate });
        audioNext = 0;
        // A page may only start sound after the user interacted with it.
        const resume = () => { if (audioContext && audioContext.state === "suspended") audioContext.resume(); };
        document.addEventListener("pointerdown", resume, true);
        document.addEventListener("keydown", resume, true);
    }
    if (audioContext.state === "suspended") audioContext.resume().catch(() => { });
    return true;
}

// Seconds of sound queued after what is playing now.
export function audioQueued() {
    if (!audioContext || audioContext.state !== "running") return 1e9; // nothing is queued until sound may play
    return Math.max(0, audioNext - audioContext.currentTime);
}

export function audioPush(view, frames) {
    if (!audioContext || audioContext.state !== "running") return false;
    const samples = new Float32Array(view.slice().buffer);
    const buffer = audioContext.createBuffer(2, frames, audioContext.sampleRate);
    const left = buffer.getChannelData(0), right = buffer.getChannelData(1);
    for (let i = 0; i < frames; i++) {
        left[i] = samples[i * 2];
        right[i] = samples[i * 2 + 1];
    }
    const source = audioContext.createBufferSource();
    source.buffer = buffer;
    source.connect(audioContext.destination);
    const now = audioContext.currentTime;
    if (audioNext < now + 0.01) audioNext = now + 0.03; // late (a hidden tab): start again a little ahead
    source.start(audioNext);
    audioNext += buffer.duration;
    return true;
}

export function audioStop() {
    if (audioContext) {
        audioContext.close();
        audioContext = null;
    }
}
