#!/usr/bin/env python3
"""Synthesises the shared sound effects, so they are original and reproducible.

    tools/sfx.py            # writes Packages/net.wirenook.assets/Sounds/*.wav

Every sound is built from sine partials and filtered noise here rather than
taken from a sample library, which keeps the package free of third-party
audio and its licences. Mono, 44.1 kHz, 16-bit, each under a second: these
play from an avatar, where short and quiet is polite.
"""

import pathlib
import wave

import numpy as np

RATE = 44100
OUT = pathlib.Path(__file__).resolve().parents[1] / "Packages" / "net.wirenook.assets" / "Sounds"


def tone(frequency, seconds, phase=0.0):
    """A sine whose frequency may be a constant or an array (one value per sample)."""
    samples = int(RATE * seconds)
    frequencies = np.broadcast_to(np.asarray(frequency, dtype=float), (samples,))
    return np.sin(2 * np.pi * np.cumsum(frequencies) / RATE + phase)


def envelope(seconds, attack, release, curve=2.0):
    """Linear attack, then a power-curve decay to silence over `release`."""
    samples = int(RATE * seconds)
    time = np.arange(samples) / RATE
    rise = np.clip(time / max(attack, 1e-6), 0, 1)
    fall = np.clip((seconds - time) / max(release, 1e-6), 0, 1) ** curve
    return rise * fall


def sweep(start, end, seconds):
    """Exponential glide: equal musical distance per unit time."""
    samples = int(RATE * seconds)
    return start * (end / start) ** (np.arange(samples) / samples)


def noise(seconds, seed):
    return np.random.default_rng(seed).uniform(-1, 1, int(RATE * seconds))


def lowpass(signal, cutoff):
    """One-pole low-pass; enough to take the hiss off noise."""
    alpha = 1 - np.exp(-2 * np.pi * cutoff / RATE)
    out = np.empty_like(signal)
    value = 0.0
    for index, sample in enumerate(signal):
        value += alpha * (sample - value)
        out[index] = value
    return out


def pop_in():
    """A rising blip: something has appeared."""
    seconds = 0.16
    return tone(sweep(420, 980, seconds), seconds) * envelope(seconds, 0.004, 0.15) * 0.8


def pop_out():
    """The same blip falling: something has gone."""
    seconds = 0.14
    return tone(sweep(880, 330, seconds), seconds) * envelope(seconds, 0.004, 0.13) * 0.7


def click():
    """A soft switch click for torches and pointers."""
    seconds = 0.05
    body = tone(1800, seconds) * envelope(seconds, 0.0005, 0.045, 3)
    tick = lowpass(noise(seconds, 1), 6000) * envelope(seconds, 0.0005, 0.02, 3)
    return (body * 0.5 + tick * 0.8) * 0.8


def chime():
    """Two bell notes a fifth apart: a notice has gone up."""
    seconds = 0.9

    def bell(frequency, delay):
        length = seconds - delay
        partials = sum(
            tone(frequency * ratio, length) * level
            for ratio, level in ((1, 1.0), (2.0, 0.35), (3.01, 0.18), (4.2, 0.08))
        )
        voiced = partials * envelope(length, 0.003, length, 3)
        return np.concatenate([np.zeros(int(RATE * delay)), voiced])[: int(RATE * seconds)]

    mix = bell(880, 0.0) + bell(1318.5, 0.12)
    return mix / np.max(np.abs(mix)) * 0.7


def portal():
    """A swelling shimmer with a falling tail: a doorway opening."""
    seconds = 0.9
    glide = sweep(180, 720, seconds)
    shimmer = tone(glide, seconds) + 0.5 * tone(glide * 1.5, seconds) + 0.3 * tone(glide * 2.01, seconds)
    wobble = 1 + 0.35 * tone(9, seconds)
    air = lowpass(noise(seconds, 2), 2500) * 0.5
    mix = (shimmer * wobble * 0.5 + air) * envelope(seconds, 0.25, 0.5)
    return mix / np.max(np.abs(mix)) * 0.7


def megaphone():
    """Two-tone attention bleep with a touch of horn buzz."""
    seconds = 0.42
    half = seconds / 2
    notes = np.concatenate([tone(660, half), tone(880, half)])
    # A clipped sine is rich in odd harmonics: the nasal horn sound.
    buzz = np.clip(notes * 2.2, -1, 1)
    gate = np.concatenate([envelope(half, 0.005, 0.05), envelope(half, 0.005, 0.12)])
    return buzz * gate * 0.55


def ping():
    """Two quick rising notes with a glassy ring: look over here."""
    seconds = 0.55

    def note(frequency, delay, length):
        partials = tone(frequency, length) + 0.3 * tone(frequency * 2.0, length) + 0.12 * tone(frequency * 3.0, length)
        voiced = partials * envelope(length, 0.002, length, 2.5)
        return np.concatenate([np.zeros(int(RATE * delay)), voiced, np.zeros(int(RATE * seconds))])[: int(RATE * seconds)]

    mix = note(1174.7, 0.0, 0.12) + note(1760.0, 0.09, 0.46)
    return mix / np.max(np.abs(mix)) * 0.7


def bonk():
    """A toy mallet landing: a hollow thump with a squeak on top."""
    seconds = 0.3
    thump = tone(sweep(310, 85, seconds), seconds) * envelope(seconds, 0.002, 0.22, 3)
    knock = lowpass(noise(seconds, 3), 1800) * envelope(seconds, 0.001, 0.05, 3)
    rise, fall = 0.06, seconds - 0.06
    squeak = tone(np.concatenate([sweep(950, 1650, rise), sweep(1650, 820, fall)]), seconds)
    squeak = squeak * envelope(seconds, 0.004, 0.2, 2)
    mix = thump + knock * 0.7 + squeak * 0.45
    return mix / np.max(np.abs(mix)) * 0.8


def poof():
    """A soft burst of air that trails off: something has vanished in smoke."""
    seconds = 0.55
    air = lowpass(noise(seconds, 4), 1400) * envelope(seconds, 0.012, 0.5, 2.5)
    hiss = (noise(seconds, 5) - lowpass(noise(seconds, 5), 3000)) * envelope(seconds, 0.004, 0.2, 3) * 0.25
    body = tone(sweep(160, 70, seconds), seconds) * envelope(seconds, 0.006, 0.3, 3) * 0.6
    mix = air + hiss + body
    return mix / np.max(np.abs(mix)) * 0.75


SOUNDS = {
    "pop-in": pop_in,
    "pop-out": pop_out,
    "click": click,
    "chime": chime,
    "portal": portal,
    "megaphone": megaphone,
    "ping": ping,
    "bonk": bonk,
    "poof": poof,
}


def write(name, signal):
    OUT.mkdir(parents=True, exist_ok=True)
    # A few milliseconds of fade at both ends guarantees no click at the cut.
    edge = int(RATE * 0.003)
    signal = signal.copy()
    signal[:edge] *= np.linspace(0, 1, edge)
    signal[-edge:] *= np.linspace(1, 0, edge)
    data = (np.clip(signal, -1, 1) * 32767).astype("<i2")
    with wave.open(str(OUT / f"{name}.wav"), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(data.tobytes())
    print(f"sfx: {name}.wav ({len(data) / RATE:.2f}s)")


if __name__ == "__main__":
    for sound_name, build in SOUNDS.items():
        write(sound_name, build())
