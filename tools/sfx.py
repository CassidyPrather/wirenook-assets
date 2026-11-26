#!/usr/bin/env python3
"""Synthesises the shared sound effects, so they are original and reproducible.

    tools/sfx.py            # writes Packages/net.wirenook.assets/Sounds/*.wav

Every sound is built from sine partials and filtered noise here rather than
taken from a sample library, which keeps the package free of third-party
audio and its licences. Mono, 44.1 kHz, 16-bit, each under a second: these
play from an avatar, where short is polite.

Every sound is written at the same loudness (see LOUDNESS), so features can
all play them at one volume and none drowns another. How loud they are in
VRChat is then set in one place, WirenookSounds.cs.
"""

import pathlib
import wave

import numpy as np

RATE = 44100
OUT = pathlib.Path(__file__).resolve().parents[1] / "Packages" / "net.wirenook.assets" / "Sounds"

# The loudness every sound is brought to, in dB relative to full scale: the
# level of its loudest 200 ms after K-weighting (the ITU-R BS.1770 filter,
# which follows the ear better than plain RMS: a bell and a thump of equal
# RMS are not equally loud). 200 ms is about how long the ear takes to judge
# loudness; the standard's own 400 ms window under-reads sounds that decay
# as fast as these do.
LOUDNESS = -17.0

# Sounds that are meant to stand out from the rest, in dB over LOUDNESS.
TRIM = {"megaphone": 2.0, "buzz": -10.0}

# Sounds that play round and round. They are written so that the end runs
# into the start, and are not faded at the ends.
LOOPS = {"buzz"}

# No sample goes above this, in dB relative to full scale. VRChat runs avatar
# audio through a compressor and advises files normalised to between -6 and
# -12 dB to stay clear of it. A sound too short and sharp to reach LOUDNESS
# under this ceiling (the click) is left quieter instead.
PEAK = -6.0


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


def collect():
    """Three quick notes up a chord: something has been picked up."""
    seconds = 0.5

    def note(frequency, delay):
        length = seconds - delay
        partials = tone(frequency, length) + 0.25 * tone(frequency * 2.0, length) + 0.1 * tone(frequency * 3.01, length)
        voiced = partials * envelope(length, 0.002, min(length, 0.3), 2.5)
        return np.concatenate([np.zeros(int(RATE * delay)), voiced])[: int(RATE * seconds)]

    mix = note(1046.5, 0.0) + note(1318.5, 0.07) + note(1568.0, 0.14)
    return mix / np.max(np.abs(mix)) * 0.7


def _device_note(frequency, delay, seconds):
    """One note of the two-note device cues: a soft mallet with a little thump under it."""
    length = seconds - delay
    partials = tone(frequency, length) + 0.3 * tone(frequency * 2.0, length) + 0.08 * tone(frequency * 4.0, length)
    voiced = partials * envelope(length, 0.004, min(length, 0.22), 2.2)
    thump = tone(sweep(frequency * 0.5, frequency * 0.25, 0.05), 0.05) * envelope(0.05, 0.002, 0.045, 2) * 0.35
    voiced[: len(thump)] += thump
    return np.concatenate([np.zeros(int(RATE * delay)), voiced])[: int(RATE * seconds)]


def unplug():
    """Two notes stepping down: something that was connected has gone."""
    seconds = 0.42
    mix = _device_note(783.99, 0.0, seconds) + _device_note(523.25, 0.13, seconds)
    return mix / np.max(np.abs(mix)) * 0.7


def plug():
    """The same two notes stepping up: it is back."""
    seconds = 0.42
    mix = _device_note(523.25, 0.0, seconds) + _device_note(783.99, 0.13, seconds)
    return mix / np.max(np.abs(mix)) * 0.7


def buzz():
    """A low mains hum with a rasp in it, to loop under something that has stopped working."""
    seconds = 1.0
    # Whole numbers of cycles in the second, so the loop has no seam.
    hum = sum(tone(55.0 * harmonic, seconds) * level
              for harmonic, level in ((1, 1.0), (2, 0.55), (3, 0.4), (4, 0.18), (5, 0.22), (7, 0.1)))
    flutter = 1 + 0.18 * tone(3.0, seconds) + 0.08 * tone(11.0, seconds)
    # The rasp is noise, which has no period: its tail is folded over its head instead.
    overlap = int(RATE * 0.12)
    rasp = lowpass(noise(seconds + 0.12, 6), 900) * 2.0
    rasp[:overlap] = rasp[:overlap] * np.linspace(0, 1, overlap) + rasp[-overlap:] * np.linspace(1, 0, overlap)
    rasp = rasp[: int(RATE * seconds)] * (0.5 + 0.5 * np.clip(tone(110.0, seconds), 0, 1))
    mix = hum * flutter * 0.5 + rasp * 0.35
    return mix / np.max(np.abs(mix)) * 0.6


def sizzle():
    """Something hot against something that minds: a hiss that crackles out."""
    seconds = 0.6
    hiss = noise(seconds, 7) - lowpass(noise(seconds, 7), 2500)
    crackle = (np.abs(noise(seconds, 8)) > 0.985) * noise(seconds, 9)
    mix = hiss * envelope(seconds, 0.01, 0.55, 2.0) * 0.6 + lowpass(crackle, 5000) * envelope(seconds, 0.02, 0.5, 1.5) * 2.5
    return mix / np.max(np.abs(mix)) * 0.7


def frost():
    """Ice forming: a few thin glassy notes falling over one another."""
    seconds = 0.6

    def crystal(frequency, delay):
        length = seconds - delay
        voiced = (tone(frequency, length) + 0.4 * tone(frequency * 2.76, length)) * envelope(length, 0.001, min(length, 0.25), 3)
        return np.concatenate([np.zeros(int(RATE * delay)), voiced])[: int(RATE * seconds)]

    mix = crystal(2637.0, 0.0) + crystal(3136.0, 0.06) + crystal(2349.3, 0.13) + crystal(3520.0, 0.19)
    return mix / np.max(np.abs(mix)) * 0.6


def splash():
    """A drop into water: a low plop and the spray after it."""
    seconds = 0.5
    plop = tone(sweep(520, 140, 0.12), 0.12) * envelope(0.12, 0.002, 0.11, 2)
    plop = np.concatenate([plop, np.zeros(int(RATE * seconds) - len(plop))])
    spray = (lowpass(noise(seconds, 10), 4200) - lowpass(noise(seconds, 10), 600)) * envelope(seconds, 0.03, 0.45, 2.5)
    mix = plop + spray * 1.6
    return mix / np.max(np.abs(mix)) * 0.7


def whoosh():
    """A gust going past: air that swells and thins, rising as it comes and falling as it goes."""
    seconds = 0.7
    air = noise(seconds, 11)
    # A band of the noise whose centre moves: low-pass at a moving cutoff, less the rumble under it.
    time = np.arange(int(RATE * seconds)) / RATE
    cutoff = 500 + 2600 * np.sin(np.pi * time / seconds) ** 2
    out = np.empty_like(air)
    value = 0.0
    for index, sample in enumerate(air):
        value += (1 - np.exp(-2 * np.pi * cutoff[index] / RATE)) * (sample - value)
        out[index] = value
    mix = (out - lowpass(out, 250)) * np.sin(np.pi * time / seconds) ** 1.5
    return mix / np.max(np.abs(mix)) * 0.7


def boing():
    """A spring let go: one note that wobbles up and down as it dies."""
    seconds = 0.5
    time = np.arange(int(RATE * seconds)) / RATE
    wobble = 300 * (1 + 0.45 * np.sin(2 * np.pi * 13 * time) * np.exp(-time * 5))
    mix = (tone(wobble, seconds) + 0.3 * tone(wobble * 2, seconds)) * envelope(seconds, 0.003, 0.48, 2)
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
    "collect": collect,
    "unplug": unplug,
    "plug": plug,
    "buzz": buzz,
    "sizzle": sizzle,
    "frost": frost,
    "splash": splash,
    "whoosh": whoosh,
    "boing": boing,
}


def biquad(signal, b, a):
    """One second-order filter section, direct form 1; `a[0]` is taken as 1."""
    out = np.empty_like(signal)
    x1 = x2 = y1 = y2 = 0.0
    for index, x in enumerate(signal):
        y = b[0] * x + b[1] * x1 + b[2] * x2 - a[1] * y1 - a[2] * y2
        x2, x1 = x1, x
        y2, y1 = y1, y
        out[index] = y
    return out


def k_weighted(signal):
    """The ITU-R BS.1770 pre-filter: a high shelf for the head, then a high-pass.

    The standard tabulates coefficients for 48 kHz only; these are the same
    two filters derived for RATE from their analogue prototypes.
    """
    k = np.tan(np.pi * 1681.974450955533 / RATE)
    q = 0.7071752369554196
    high = 10 ** (3.999843853973347 / 20)
    band = high**0.4996667741545416
    a0 = 1 + k / q + k * k
    shelved = biquad(
        signal,
        [(high + band * k / q + k * k) / a0, 2 * (k * k - high) / a0, (high - band * k / q + k * k) / a0],
        [1.0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0],
    )
    k = np.tan(np.pi * 38.13547087602444 / RATE)
    q = 0.5003270373238773
    a0 = 1 + k / q + k * k
    return biquad(shelved, [1.0, -2.0, 1.0], [1.0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0])


def loudness(signal, seconds=0.2):
    """K-weighted level of the loudest `seconds` of a sound, in dB relative to full scale.

    A sound shorter than the window is measured as if followed by silence,
    so a 50 ms click does not count as loud as 200 ms of the same level.
    """
    window = int(RATE * seconds)
    weighted = k_weighted(signal)
    weighted = np.concatenate([weighted, np.zeros(max(0, window - len(weighted)))])
    energy = np.cumsum(np.concatenate([[0.0], weighted**2]))
    mean_square = np.max(energy[window:] - energy[:-window]) / window
    # The constant is the standard's: a full-scale 997 Hz sine reads -3.01.
    return -0.691 + 10 * np.log10(max(mean_square, 1e-12))


def peak(signal):
    return 20 * np.log10(max(np.max(np.abs(signal)), 1e-12))


def finish(name, signal):
    """Fades the ends and brings the sound to the shared loudness."""
    # A few milliseconds of fade at both ends guarantees no click at the cut.
    edge = int(RATE * 0.003)
    signal = signal.copy()
    if name not in LOOPS:
        signal[:edge] *= np.linspace(0, 1, edge)
        signal[-edge:] *= np.linspace(1, 0, edge)

    gain = min(LOUDNESS + TRIM.get(name, 0.0) - loudness(signal), PEAK - peak(signal))
    return signal * 10 ** (gain / 20)


def write(name, signal):
    OUT.mkdir(parents=True, exist_ok=True)
    data = np.round(np.clip(signal, -1, 1) * 32767).astype("<i2")
    with wave.open(str(OUT / f"{name}.wav"), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(data.tobytes())
    written = data / 32767
    print(f"sfx: {name}.wav ({len(data) / RATE:.2f}s, loudness {loudness(written):.1f}, peak {peak(written):.1f} dB)")


if __name__ == "__main__":
    for sound_name, build in SOUNDS.items():
        write(sound_name, finish(sound_name, build()))
