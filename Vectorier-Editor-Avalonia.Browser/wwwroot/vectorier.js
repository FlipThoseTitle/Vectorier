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
