// Plays a song as its separate stems, mixed here, instead of one stream the host already mixed.
//
// Why the page rather than the host: the host's mix is compiled into an ffmpeg filter graph, so
// moving one voice means a new encode and reopening the stream at the playhead. Here it is a gain.
//
// Why decoded buffers rather than one media element per stem: measured, elements will not hold
// together. Three of them on this engine sat 100-350ms apart and drifted further the longer the
// song ran, with the trailing ones pinned at the fastest correction available and still losing
// ground — the stems each keep their own media clock and nothing makes them agree. Sources
// scheduled against a single AudioContext clock share one clock by construction and cannot drift
// at all. The cost is the whole song resident as PCM and about a second of decoding at load.

/// Enough for every stem to be scheduled before the first is due to sound.
const START_LEAD_SECONDS = 0.12;

/// How long the mix takes to come up from silence when sources start, and to go back to it
/// before they stop. A source started or stopped mid-waveform is a step in the signal, which is
/// a click; this is short enough to hear as instant and long enough not to be one.
const EDGE_FADE_SECONDS = 0.015;

/// How long a level change takes to land. A step in a playing mix clicks the same way a start does.
const LEVEL_RAMP_SECONDS = 0.015;

// One output for the screen's whole life. Opening an AudioContext opens an output stream, and on
// Windows that wakes the audio endpoint, which many laptop codecs answer with an audible pop — so
// a context per song popped at the top of every song.
let sharedStemContext = null;

/// The screen's context, opened on first use and again only once closed. A suspended or
/// interrupted one is asked to resume rather than replaced, since replacing it is the pop.
function stemAudioContext() {
    if (!sharedStemContext || sharedStemContext.state === 'closed') {
        sharedStemContext = new (window.AudioContext || window.webkitAudioContext)();
    }

    if (sharedStemContext.state !== 'running') {
        try { Promise.resolve(sharedStemContext.resume()).catch(() => {}); } catch { /* retried on play */ }
    }

    return sharedStemContext;
}

/// Lets go of `context` as the shared one, so the next mixer opens a fresh context. Only for a
/// wake: a context that lived through a sleep can read 'running' and play nothing, so resuming
/// it is no help. Returns whether it was the shared one.
function retireStemAudioContext(context) {
    if (!context || sharedStemContext !== context) return false;

    sharedStemContext = null;
    return true;
}

/// Whether a stem-volume message is for this stem. Voices compare exactly, and a missing voice
/// on either side is the same as null — the host omits it for a stem no singer is named on.
function stemMatches(stem, role, voice) {
    return stem.role === role && (stem.voice ?? null) === (voice ?? null);
}

function clampLevel(value) {
    return Number.isFinite(value) ? Math.max(0, Math.min(1, value)) : 1;
}

/// Mixes stems into one voice the rest of the page can drive like a single media element.
///
/// The returned object is deliberately shaped like one — `currentTime`, `play`, `pause`,
/// `readyState`, `src` — because the transport, the drift correction and the lyrics clock all
/// address whatever `target()` hands them, and none of them should learn what a stem is.
///
/// `startOffsetSeconds` is where the stream this replaces would have begun. The stems are always
/// the whole song from zero, so everything exposed here is shifted back by it and the rest of the
/// page goes on reading stream-relative time.
///
/// `volume` is the room's level. The venue sends it only on connect and on an edit, so a mix
/// that started at unity would play every song after the first at full level.
function createStemMixer(stems, startOffsetSeconds, reportError, volume = 1) {
    const ctx = stemAudioContext();

    // The room's level and the stop fade. What the visualiser taps, so it hears what the room does.
    const master = ctx.createGain();
    master.gain.setValueAtTime(clampLevel(volume), ctx.currentTime);
    master.connect(ctx.destination);

    // The start and stop envelope, in series before the master rather than on it: the two then
    // multiply, so neither a level change nor the stop fade can cancel an edge or be cancelled by one.
    const edge = ctx.createGain();
    edge.gain.setValueAtTime(0, ctx.currentTime);
    edge.connect(master);

    const parts = stems.map((stem) => {
        const gain = ctx.createGain();
        gain.gain.value = levelOf(stem);
        gain.connect(edge);

        // The level as the host last set it, kept so a rebuilt mix can start where this one is.
        return { stem, gain, buffer: null, source: null, volume: stem.volume };
    });

    // Song seconds, absolute. Where the mix sits when it is not running, and what it was started
    // from when it is; `startedAt` is the context clock reading it began against.
    let position = startOffsetSeconds;
    let startedAt = null;
    let decoded = false;

    const ready = decodeAll().catch((e) => { reportError(`stems: ${e}`); throw e; });

    /// The music is the reference and carries no level of its own; the voices ride against it.
    function levelOf(stem) {
        return stem.role === 'Music' ? 1 : Math.max(0, Math.min(100, stem.volume)) / 100;
    }

    async function decodeAll() {
        await Promise.all(parts.map(async (part) => {
            const response = await fetch(part.stem.url);
            if (!response.ok) throw new Error(`${part.stem.url} answered ${response.status}`);

            // decodeAudioData detaches what it is given, so each stem needs its own bytes.
            part.buffer = await ctx.decodeAudioData(await response.arrayBuffer());
        }));

        decoded = true;
    }

    /// The song position now, from the context clock while running and from the mark when not.
    function songTime() {
        if (startedAt === null) return position;

        // The start lead is not song time: reported, it reads as a playhead before zero.
        return position + Math.max(0, ctx.currentTime - startedAt);
    }

    /// Started is not sounding: a suspended or interrupted context freezes its clock and plays
    /// nothing, and reporting that as playing pins the host's playhead to wherever it froze.
    function sounding() {
        return startedAt !== null && ctx.state === 'running';
    }

    function longest() {
        return parts.reduce((most, part) => Math.max(most, part.buffer ? part.buffer.duration : 0), 0);
    }

    /// Every stem started against one instant on one clock, which is what makes them stay together.
    function startSources(from) {
        const when = ctx.currentTime + START_LEAD_SECONDS;

        for (const part of parts) {
            if (!part.buffer) continue;

            const source = ctx.createBufferSource();
            source.buffer = part.buffer;
            source.connect(part.gain);

            // Past the end of this stem there is nothing to schedule; a shorter stem simply stops.
            if (from < part.buffer.duration) source.start(when, from);

            part.source = source;
        }

        // From `when` on only: a seek has just scheduled the old sources' fade-out before it.
        edge.gain.cancelScheduledValues(when);
        edge.gain.setValueAtTime(0, when);
        edge.gain.linearRampToValueAtTime(1, when + EDGE_FADE_SECONDS);

        position = from;
        startedAt = when;
    }

    /// Rides the envelope to silence and stops every source once it is there. Returns at once:
    /// the mix reads as stopped now, and the sound ends a few milliseconds later on the audio clock.
    function stopSources() {
        const now = ctx.currentTime;
        const silentAt = now + EDGE_FADE_SECONDS;

        edge.gain.cancelScheduledValues(now);
        edge.gain.setValueAtTime(edge.gain.value, now);
        edge.gain.linearRampToValueAtTime(0, silentAt);

        for (const part of parts) {
            if (!part.source) continue;

            const source = part.source;
            part.source = null;

            // Disconnected once it has finished: cut loose now, it would stop mid-waveform after all.
            source.onended = () => { try { source.disconnect(); } catch { /* already gone */ } };
            try { source.stop(silentAt); } catch { try { source.disconnect(); } catch { /* already gone */ } }
        }
    }

    return {
        /// True so the lyrics clock counts this as something holding the song; it guards on a
        /// source being present and a bare mixer has neither `src` nor `srcObject` of its own.
        src: 'stems:',

        /// Nothing to play until the stems are decoded, and everything once they are. The lyrics
        /// clock and the drift loop both read this before trusting the time below.
        get readyState() { return decoded ? 4 : 0; },

        /// The decoded length, which is exact — unlike a media element's, which this engine
        /// estimates from a nominal bitrate and reads minutes long on a variable-rate stem.
        get duration() { return Math.max(0, longest() - startOffsetSeconds); },

        get paused() { return !sounding(); },

        /// Started and run past its longest stem. The context clock runs on past the last sample,
        /// so without this a finished mix reads as playing forever.
        get ended() { return startedAt !== null && songTime() >= longest(); },

        /// What the room hears, after the level and any fade, for a visualiser to listen to.
        /// A tap fans out from it; the route to the speakers is left as it is.
        analysisSource() { return { context: ctx, node: master }; },

        /// Carried on the state report, so a mix stalled on its context shows in a debug log.
        get audioState() { return ctx.state; },

        /// Never mid-seek: a seek here is arithmetic and a fresh set of sources, not a fetch.
        get seeking() { return false; },

        get currentTime() { return songTime() - startOffsetSeconds; },
        set currentTime(value) {
            const to = Math.max(0, value + startOffsetSeconds);

            if (startedAt === null) { position = to; return; }

            stopSources();
            startSources(to);
        },

        get volume() { return master.gain.value; },
        set volume(value) {
            // A fade still scheduled would win over a bare `.value`, pulling a resumed song down.
            const now = ctx.currentTime;
            master.gain.cancelScheduledValues(now);
            master.gain.setValueAtTime(master.gain.value, now);
            master.gain.linearRampToValueAtTime(clampLevel(value), now + LEVEL_RAMP_SECONDS);
        },

        /// Rides the whole mix down to silence on the context clock, from wherever it is now.
        /// Scheduled rather than stepped from a timer, so it keeps time with the audio it fades.
        fadeOut(ms) {
            const now = ctx.currentTime;
            const from = master.gain.value;

            master.gain.cancelScheduledValues(now);
            master.gain.setValueAtTime(from, now);
            master.gain.linearRampToValueAtTime(0, now + Math.max(0, ms) / 1000);
        },

        get playbackRate() { return 1; },
        set playbackRate(_) { /* the stems play at written speed; the host retimes with ffmpeg */ },

        async play() {
            // Started but not sounding: the host's play is the only retry a stopped context gets.
            if (startedAt !== null) {
                await ctx.resume();
                return;
            }

            await ready;
            await ctx.resume();

            startSources(position);
        },

        pause() {
            if (startedAt === null) return;

            const at = songTime();
            stopSources();
            startedAt = null;
            position = Math.min(at, longest());
        },

        /// Moves one voice without re-encoding anything, which is the point of mixing here.
        /// A null voice moves only stems carrying none: a named singer's lead has its own level.
        setStemVolume(role, voice, volume) {
            let moved = 0;

            for (const part of parts) {
                if (!stemMatches(part.stem, role, voice)) continue;

                part.gain.gain.value = levelOf({ role, volume });
                part.volume = volume;
                moved++;
            }

            return moved;
        },

        /// Everything a fresh mixer needs to carry on from here: the stems at their current levels,
        /// the position, and whether the host has it playing — started, not sounding, since a
        /// context a wake left behind may report either.
        snapshot() {
            return {
                stems: parts.map((part) => ({ ...part.stem, volume: part.volume })),
                currentTime: songTime() - startOffsetSeconds,
                playing: startedAt !== null,
            };
        },

        /// Lets go of this mix's own nodes, never the context: that is the screen's, and closing it
        /// is the pop this exists to avoid. Resolves once the nodes are off the graph, which is
        /// after the stop edge has run, so a caller closing a retired context cannot cut it short.
        destroy() {
            stopSources();
            startedAt = null;

            // The buffers are the song held as PCM, and there is one of these per song: dropped
            // here rather than left for the collector to notice.
            for (const part of parts) part.buffer = null;

            return new Promise((resolve) => {
                // A margin past the edge: the timer and the audio clock are not the same clock.
                setTimeout(() => {
                    for (const part of parts) { try { part.gain.disconnect(); } catch { /* already gone */ } }
                    try { edge.disconnect(); } catch { /* already gone */ }
                    try { master.disconnect(); } catch { /* already gone */ }
                    resolve();
                }, EDGE_FADE_SECONDS * 1000 + 50);
            });
        },
    };
}

/// Replaces a mix with one on a fresh context, at the same place and levels, for a machine that
/// slept under it. Resolves null when `stillWanted()` says a load or stop replaced it meanwhile.
///
/// The old context is closed before the new one opens: a context opened while another is still
/// open may be handed that one's output, and a wake is what leaves an output dead. This is the one
/// place the screen's context is replaced while still open.
async function rebuildStemMixer(mixer, create, stillWanted) {
    const state = mixer.snapshot();
    const dead = mixer.analysisSource().context;

    // Retired before the wait, so a load landing during it cannot open onto the dead context.
    const owned = retireStemAudioContext(dead);
    await mixer.destroy();
    if (owned) {
        try { await Promise.resolve(dead.close()).catch(() => {}); } catch { /* already closed */ }
    }

    if (!stillWanted()) return null;

    const next = create(state.stems);
    next.currentTime = state.currentTime;

    return { mixer: next, playing: state.playing, position: state.currentTime };
}
