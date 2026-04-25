# Singer Queue Rotation Strategies

Reference doc for rotation strategy options and implementation requirements.

---

## Strategy Catalogue

| # | Name | Description |
|---|---|---|
| 1 | FIFO | Singers perform in the order they joined. No rotation. |
| 2 | Round-robin | After performing, singer moves to the bottom. Everyone gets equal turns before anyone sings twice. |
| 3 | Weighted round-robin | Singers get slots proportional to a weight (e.g. paid VIP gets 2 slots per cycle, regular gets 1). |
| 4 | Fair-share with late-joiner catch-up | New singers are inserted at a position that reflects how many songs have already been sung, rather than always at the back. |
| 5 | Least-recently-sung | Queue always sorted by who sang longest ago; adding a song re-ranks you automatically. |
| 6 | Random / lottery | Each rotation the next singer is drawn at random from the pool; optional bias toward those who haven't sung recently. |
| 7 | Performance-count equalizer | Automatically sorts so that singers with fewer total songs tonight are always higher in the queue. |
| 8 | Cooldown-enforced | A singer can't return to an eligible position until N minutes or N rotations have passed since their last performance. |
| 9 | Interleaved newcomer | Every Nth slot is reserved for someone singing for the first time that night, injecting new singers between regulars. |
| 10 | Host-curated with auto-fill | The KJ manually locks the top N slots; remaining slots fill automatically by one of the other strategies. |
| 11 | Time-slice | Queue managed by clock time rather than song count; each singer gets a fixed window (e.g. 7 minutes) and the next singer starts when the window expires. |
| 12 | Bidding / tip-based | Singers earn or spend credits (tips, tokens) to move up; the queue is sorted descending by credit balance. |
| 13 | Group/team rotation | Singers are grouped into teams; teams rotate in round-robin order and within each team the internal order is FIFO. |
| 14 | Alternating category | The queue enforces an alternating pattern (e.g. solo → duet → solo, or category A → B → A) to create variety in the show. |

---

## Implementation Analysis

### 1. FIFO
**Status: already implemented.** Default behavior of `AddSingerAsync` appending to `_singerIds`. No changes needed.

---

### 2. Round-robin
**Status: partially implemented.**

`PlaybackService.ServiceOptions` already has `MoveSingerToBottomAfterPerformance`, and `SingerQueueService` already has `MoveSingerToEndAsync`. The missing piece is the completion trigger — `PlaybackService` needs to detect when a song ends and invoke `MoveSingerToEndAsync` on the current singer. Since `KHost.Screen` and `KHost.UserInterface` are currently disconnected, this signal never arrives.

Work needed:
- A song-completion event on `IPlaybackService` (or detect the `Playing → Stopped` state transition)
- When `MoveSingerToBottomAfterPerformance` is true, call `SingerQueueService.MoveSingerToEndAsync(CurrentSingerId)` on completion
- No new data model needed

---

### 4. Fair-share with late-joiner catch-up
**Status: not started.**

When a singer joins mid-show, instead of appending to the back, calculate a fair insertion point based on songs already sung tonight.

Work needed:
- `PerformanceService` already stores history — query how many songs each current queue member has sung tonight
- New `AddSingerAsync` overload (or a settings toggle) that computes insertion index: find the first position where the new singer's song count (0) ≤ the song count of the singer at that slot
- No schema changes needed — `PerformanceService` already has the data
- UI: a toggle in settings for "fair late-join" vs. "always add to back"

---

### 9. Interleaved newcomer
**Status: not started.**

Every Nth slot is reserved for a singer who hasn't sung yet tonight.

Work needed:
- `PerformanceService` can identify who has performed tonight (query by session/date)
- A new `RebalanceQueueAsync` method that walks `_singerIds` and, every N positions, ensures a newcomer is present — swapping if needed
- This rebalance runs after each song completes (same trigger as round-robin)
- Newcomer definition needs a decision: never sung at this venue ever (needs DB history) or not yet tonight (session-scoped set in `SingerQueueService`)
- N is a configurable option on `ServiceOptions`
- Edge case: if there are no newcomers left, fall back to normal order

---

### 10. Host-curated with auto-fill
**Status: not started (single-slot version partially exists).**

`IsTopSlotLocked` already locks slot 0 from being moved or dragged out. Full implementation requires locking an arbitrary set of singers.

Work needed:
- Replace `bool _isTopSlotLocked` with `HashSet<Guid> _lockedSingerIds` (or a `LockedSlotCount` int for "lock the top N")
- `MoveSingerToIndexAsync`, `MoveSingerUpAsync`, etc. skip over locked positions
- New `LockSingerAsync(Guid singerId)` / `UnlockSingerAsync(Guid singerId)` on `ISingerQueueService` and the service
- The auto-fill portion (below the locked block) runs whichever rotation strategy is active — it only operates on the unlocked tail
- Locked singer IDs persisted in `QueueCacheData` alongside `SingerIds`
- UI: a lock/pin icon button on each singer row (alongside the existing remove button), and a visual indicator (pin icon or locked border) on pinned rows

---

## Shared Infrastructure

| Need | Required by |
|---|---|
| Song-completion trigger from `PlaybackService` | 2, 9 |
| Session-scoped "has sung tonight" tracking | 4, 9 |
| `RotationMode` enum on `SingerQueueService.ServiceOptions` | 2, 4, 9, 10 |

The `RotationMode` setting is the biggest shared piece — an enum that gates which rebalancing logic runs after each performance, allowing the strategies to share the same completion trigger and session-tracking infrastructure.
