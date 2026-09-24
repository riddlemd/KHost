namespace KHost.UserInterface.Interactions.Handlers;

/// <summary>Keeps a cancellation registration alive until the request it guards completes by any
/// path. A `using` scoped to the handler method disposes the registration the moment the method
/// returns the still-pending task, which deregisters the callback before a cancel raised after
/// that point has anything left to call.</summary>
internal static class InteractionCompletion
{
    public static void LinkCancellation<T>(TaskCompletionSource<T> tcs, CancellationToken cancellationToken)
        => Dispose(tcs.Task, cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)));

    public static void LinkCancellation(TaskCompletionSource tcs, CancellationToken cancellationToken)
        => Dispose(tcs.Task, cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)));

    private static void Dispose(Task task, CancellationTokenRegistration registration)
        => task.ContinueWith(
            static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
            registration,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
