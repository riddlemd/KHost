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

/// Mixes stems into one voice the rest of the page can drive like a single media element.
///
/// The returned object is deliberately shaped like one — `currentTime`, `play`, `pause`,
/// `readyState`, `src` — because the transport, the drift correction and the lyrics clock all
/// address whatever `target()` hands them, and none of them should learn what a stem is.
///
/// `startOffsetSeconds` is where the stream this replaces would have begun. The stems are always
/// the whole song from zero, so everything exposed here is shifted back by it and the rest of the
/// page goes on reading stream-relative time.
function createStemMixer(stems, startOffsetSeconds, reportError) {
    const ctx = new (window.AudioContext || window.webkitAudioContext)();
    const master = ctx.createGain();
    master.connect(ctx.destination);

    const parts = stems.map((stem) => {
        const gain = ctx.createGain();
        gain.gain.value = levelOf(stem);
        gain.connect(master);

        return { stem, gain, buffer: null, source: null };
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

        position = from;
        startedAt = when;
    }

    function stopSources() {
        for (const part of parts) {
            if (!part.source) continue;

            try { part.source.stop(); } catch { /* never started, or already done */ }
            try { part.source.disconnect(); } catch { /* already gone */ }
            part.source = null;
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
        set volume(value) { master.gain.value = Math.max(0, Math.min(1, value)); },

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
        setStemVolume(role, volume) {
            let moved = 0;

            for (const part of parts) {
                if (part.stem.role !== role) continue;

                part.gain.gain.value = levelOf({ role, volume });
                moved++;
            }

            return moved;
        },

        destroy() {
            stopSources();
            startedAt = null;

            // The buffers are the song held as PCM, and there is one of these per song: dropped
            // here rather than left for the collector to notice.
            for (const part of parts) part.buffer = null;

            try { ctx.close(); } catch { /* already gone with the page */ }
        },
    };
}
