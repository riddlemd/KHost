namespace KHost.Abstractions.Exceptions;

/// <summary>
/// A failure a host can be told about in their own words. The message stays for logs and callers
/// that only understand <see cref="Exception"/>; the three properties are what the error dialog
/// shows, so throwing this is what makes a failure presentable rather than a stack trace.
/// </summary>
/// <remarks>
/// In the SDK rather than Abstractions because it is the only way a plugin can report a failure
/// the host can act on, which makes it a plugin contract. A plugin reaching into Abstractions for
/// it would pull PolyForm-licensed code into an assembly redistributed under the SDK's MIT grant.
/// </remarks>
public class KHostException : Exception
{
    public KHostException(
        string whatHappened,
        string suggestion,
        string referenceCode,
        Exception? innerException = null)
        : base(whatHappened, innerException)
    {
        WhatHappened = whatHappened;
        Suggestion = suggestion;
        ReferenceCode = referenceCode;
    }

    /// <summary>Plain words, no jargon: what went wrong from the host's point of view.</summary>
    public string WhatHappened { get; }

    /// <summary>What to try next. Empty when there is genuinely nothing the host can do.</summary>
    public string Suggestion { get; }

    /// <summary>
    /// Short, stable, quotable — the thing a host reads down the phone, and what a bug report will
    /// be filed against once reporting exists.
    /// </summary>
    public string ReferenceCode { get; }
}
