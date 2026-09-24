namespace KHost.Abstractions.Exceptions;

/// <summary>A failure told in plain words: the three properties an error dialog reads.</summary>
/// <remarks>Stays in Abstractions: it is how a plugin reports a failure the host can act on.</remarks>
public class KHostException : Exception
{
    /// <summary>Builds the exception from the three reader-facing fields.</summary>
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

    /// <summary>Short, stable, quotable: the thing a host reads down the phone.</summary>
    public string ReferenceCode { get; }
}
