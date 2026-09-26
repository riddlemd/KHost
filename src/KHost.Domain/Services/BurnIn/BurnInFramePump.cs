using System.Collections.Concurrent;

namespace KHost.Domain.Services.BurnIn;

/// <summary>Paints a song's words frame by frame and writes them, in order, down the pipe an encode
/// reads its overlay from.</summary>
/// <remarks>Painting runs on several threads, because an encode that only keeps pace with the song
/// leaves nothing in hand for a seek or a slow network. Worker <c>w</c> paints frames
/// <c>w, w+N, w+2N…</c>, so taking one finished frame from each in turn gives the pipe exact frame
/// order with no reordering buffer to keep.</remarks>
internal static class BurnInFramePump
{
    /// <summary>Frames a painting thread may have in hand: one being painted, one queued for the pipe.
    /// </summary>
    /// <remarks>Each is a full frame of pinned memory, so more only raises the floor on memory once
    /// the jitter between painting and writing is covered.</remarks>
    private const int SlotsPerWorker = 2;

    /// <summary>How many threads paint.</summary>
    /// <remarks>Measured at 1280x720 on a ten-core Apple M5: one thread paints a four-line page at
    /// about 600 frames a second, twenty times the song, and a second keeps painting off x264's
    /// critical path — a five-minute song then encoded at 25x real time over black and 10x over a
    /// background clip. More threads only compete with x264 for the same processors.</remarks>
    internal static int WorkersFor(int processorCount) => processorCount >= 4 ? 2 : 1;

    /// <summary>How many frames cover the song from <paramref name="start"/> to <paramref name="end"/>,
    /// played at <paramref name="rate"/>.</summary>
    internal static int FramesFor(double end, double start, double rate, int fps)
        => (int)Math.Ceiling(Math.Max(0, end - start) / rate * fps);

    /// <summary>The song position frame <paramref name="frame"/> shows.</summary>
    /// <remarks>Output time runs at <paramref name="rate"/> against the song, so the words keep pace
    /// with audio that has been sped up or slowed down by the same factor.</remarks>
    internal static double SongTimeOf(int frame, double start, double rate, int fps)
        => start + frame / (double)fps * rate;

    /// <summary>Paints and writes every frame, then closes <paramref name="output"/>.</summary>
    /// <remarks>Returns quietly when the reader goes away — the encode ending with its picture, or
    /// the session being torn down — since either way nothing is left to paint for. Cancelling
    /// <paramref name="cancellationToken"/> stops the painters; a write already blocked in the pipe is
    /// released by whoever cancels killing the reader.</remarks>
    public static void Run(
        TimedLyricsPainter painter,
        Stream output,
        double startSeconds,
        double rate,
        int fps,
        int frameCount,
        int workers,
        CancellationToken cancellationToken)
    {
        workers = Math.Max(1, workers);
        var slots = new List<(TimedLyricsPainter.Worker Painter, TimedLyricsPainter.Frame[] Frames)>();

        try
        {
            for (var w = 0; w < workers; w++)
            {
                var frames = new TimedLyricsPainter.Frame[SlotsPerWorker];
                slots.Add((painter.CreateWorker(), frames));
                for (var s = 0; s < SlotsPerWorker; s++) frames[s] = painter.CreateFrame();
            }

            var ready = new BlockingCollection<TimedLyricsPainter.Frame>[workers];
            var free = new BlockingCollection<TimedLyricsPainter.Frame>[workers];

            for (var w = 0; w < workers; w++)
            {
                ready[w] = new BlockingCollection<TimedLyricsPainter.Frame>(SlotsPerWorker);
                free[w] = new BlockingCollection<TimedLyricsPainter.Frame>(SlotsPerWorker);
                foreach (var frame in slots[w].Frames) free[w].Add(frame, CancellationToken.None);
            }

            // A painter that throws must not leave the writer waiting on a frame nobody will paint,
            // and a writer that stops must not leave painters waiting on a slot nobody will return.
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var painting = new Task[workers];

            for (var w = 0; w < workers; w++)
            {
                var worker = w;
                painting[w] = Task.Run(() =>
                {
                    try
                    {
                        for (var f = worker; f < frameCount; f += workers)
                        {
                            var slot = free[worker].Take(stop.Token);
                            slots[worker].Painter.Paint(slot, SongTimeOf(f, startSeconds, rate, fps));
                            ready[worker].Add(slot, stop.Token);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch { stop.Cancel(); throw; }
                }, CancellationToken.None);
            }

            try
            {
                for (var f = 0; f < frameCount; f++)
                {
                    var worker = f % workers;
                    var slot = ready[worker].Take(stop.Token);
                    output.Write(slot.Pixels, 0, slot.Pixels.Length);
                    free[worker].Add(slot, stop.Token);
                }

                output.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The reader is gone or the caller stopped us; a painter fault surfaces below.
            }
            finally
            {
                stop.Cancel();
                try { output.Dispose(); } catch (IOException) { }
            }

            // After the writer, so a painting fault surfaces as itself rather than as a broken pipe.
            Task.WaitAll(painting, CancellationToken.None);
        }
        finally
        {
            foreach (var (worker, frames) in slots)
            {
                foreach (var frame in frames) frame?.Dispose();
                worker.Dispose();
            }
        }
    }
}
